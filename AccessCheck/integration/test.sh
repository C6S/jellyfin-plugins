#!/usr/bin/env bash
# Checks the running stack from compose.yaml, set up by setup.sh. Requests
# come from the runner, which Jellyfin sees as remote; see setup.sh.
#
# Every failed login rate-limits that username for a couple of seconds, so
# the order of the checks matters.
# $(as user) expands to two curl arguments on purpose.
# shellcheck disable=SC2046
set -uo pipefail

: "${STATE:?}"
# shellcheck disable=SC1090
source "$STATE"

NGINX=http://127.0.0.1:8080
JF=http://127.0.0.1:8096
NETWORK=accesscheck-it
CLIENT='MediaBrowser Client="AccessCheck CI", Device="CI", DeviceId="accesscheck-ci", Version="1.0"'

passed=0
failed=0

pass() {
    passed=$((passed + 1))
    echo "ok    $1"
}

fail() {
    failed=$((failed + 1))
    echo "FAIL  $1"
    # Also as a GitHub annotation, which can be read without logging in.
    echo "::error title=Integration test (Jellyfin ${JELLYFIN_TAG:-?})::${1//$'\n'/ }"
}

# check description expected-status curl-args...
check() {
    local description=$1 expected=$2 got
    shift 2
    got=$(curl -s -o /dev/null -w '%{http_code}' --path-as-is "$@")
    if [ "$got" = "$expected" ]; then
        pass "$description"
    else
        fail "$description: expected $expected, got $got"
    fi
}

# expect description expected actual
expect() {
    if [ "$3" = "$2" ]; then
        pass "$1"
    else
        fail "$1: expected '$2', got '$3'"
    fi
}

admin_api() { # method path [curl args...]
    local method=$1 path=$2
    shift 2
    curl -fsS -X "$method" "$JF$path" \
        -H "Authorization: $CLIENT, Token=\"$TOKEN\"" \
        -H 'Content-Type: application/json' \
        "$@"
}

as() { # user -> curl credentials, password "<user>-pw"
    echo "-u $1:$1-pw"
}

echo "== Logins"
check "no credentials" 401 "$NGINX/media/Movies/a.mkv"
expect "realm is the server name in ASCII" 'Basic realm="Cafe Test"' \
    "$(curl -s -o /dev/null -w '%header{www-authenticate}' "$NGINX/media/Movies/a.mkv")"
check "unknown user" 401 -u nobody:wrong "$NGINX/media/Movies/a.mkv"
check "retry straight away is rate-limited" 403 -u nobody:wrong "$NGINX/media/Movies/a.mkv"
check "disabled account" 401 $(as eve) "$NGINX/media/Movies/a.mkv"
check "outside the access schedule" 401 $(as dave) "$NGINX/media/Movies/a.mkv"

echo "== Libraries"
expect "enabled library serves the file" movie-a "$(curl -s $(as alice) "$NGINX/media/Movies/a.mkv")"
check "library not enabled" 403 $(as alice) "$NGINX/media/Shows/s.mkv"
check "listing above the libraries" 200 $(as alice) "$NGINX/media/"
check "folder that isn't a library" 403 $(as alice) "$NGINX/media/NotALibrary/x.mkv"
check "library name in the wrong case" 403 $(as alice) "$NGINX/media/movies/a.mkv"
check "all libraries" 200 $(as admin) "$NGINX/media/Private/p.mkv"
check "downloads disabled" 403 $(as bob) "$NGINX/media/Movies/a.mkv"
check "downloads disabled, listing above the libraries" 200 $(as bob) "$NGINX/media/"

echo "== Paths"
check "dot segments" 403 $(as alice) "$NGINX/media/Movies/../Shows/s.mkv"
check "encoded dot segments" 403 $(as alice) "$NGINX/media/Movies/%2e%2e/Shows/s.mkv"
check "dot segment with encoded slash" 403 $(as alice) "$NGINX/media/Movies/..%2fShows/s.mkv"
check "dot segment above the prefix" 403 $(as alice) "$NGINX/media/../media/Shows/s.mkv"
check "dots in the query are ignored" 200 $(as alice) "$NGINX/media/Movies/a.mkv?x=../../Shows"
check "check endpoint isn't public" 404 $(as alice) "$NGINX/AccessCheck"
check "check endpoint with a path isn't public" 404 $(as alice) "$NGINX/AccessCheck/media/Movies/a.mkv"

echo "== Direct requests to Jellyfin"
check "no path" 400 $(as alice) "$JF/AccessCheck"
check "path both in the URL and the header" 400 $(as alice) -H 'X-Request-URI: /media/Movies/a.mkv' \
    "$JF/AccessCheck/media/Movies/a.mkv"
check "path in the URL" 200 $(as alice) "$JF/AccessCheck/media/Movies/a.mkv"
check "path outside the browse root" 403 $(as alice) -H 'X-Request-URI: /mnt' "$JF/AccessCheck"
check "folders above the browse root" 403 $(as admin) -H 'X-Request-URI: /' "$JF/AccessCheck"

echo "== WebDAV"
check "OPTIONS without credentials" 200 -X OPTIONS "$NGINX/media/"
propfind=$(curl -s $(as alice) -X PROPFIND -H 'Depth: 1' "$NGINX/media/Movies/")
expect "PROPFIND lists the library" yes "$(grep -q '/media/Movies/a.mkv' <<<"$propfind" && echo yes)"
check "PROPFIND without download permission" 403 $(as bob) -X PROPFIND -H 'Depth: 1' "$NGINX/media/Movies/"
check "byte range at the end of a 5 GiB file" 206 $(as alice) -r 5368709000-5368709119 "$NGINX/media/Movies/big.mkv"

echo "== Remote access"
# carol may only connect locally. A first login from the runner fails inside
# Jellyfin's own login check; a login from the local address is then cached,
# and the plugin must still refuse the same credentials from the runner.
check "remote, first login" 401 $(as carol) "$NGINX/media/Movies/a.mkv"
sleep 3
expect "local" 200 "$(docker run --rm --network "$NETWORK" --ip 172.30.0.100 curlimages/curl \
    -s -o /dev/null -w '%{http_code}' -u carol:carol-pw http://172.30.0.20/media/Movies/a.mkv)"
check "remote, with the login cached" 403 $(as carol) "$NGINX/media/Movies/a.mkv"

echo "== Changes apply immediately"
policy=$(admin_api GET "/Users/$ALICE" | jq ".Policy | .EnabledFolders += [\"$SHOWS\"]")
admin_api POST "/Users/$ALICE/Policy" -d "$policy"
check "library enabled after the login was cached" 200 $(as alice) "$NGINX/media/Shows/s.mkv"
admin_api POST "/Users/Password?userId=$ALICE" -d '{"NewPw": "alice-new"}'
check "old password after a change" 401 $(as alice) "$NGINX/media/Movies/a.mkv"
sleep 3
check "new password" 200 -u alice:alice-new "$NGINX/media/Movies/a.mkv"

echo "== rclone"
rclone() {
    docker run --rm --network "$NETWORK" \
        -e RCLONE_WEBDAV_URL=http://172.30.0.20/media/ \
        -e RCLONE_WEBDAV_VENDOR=other \
        -e RCLONE_WEBDAV_USER=alice \
        -e RCLONE_WEBDAV_PASS="$(docker run --rm rclone/rclone obscure alice-new)" \
        rclone/rclone "$@"
}
expect "rclone lists a library" yes "$(rclone lsf :webdav:Movies | grep -qx a.mkv && echo yes)"
expect "rclone reads a file" movie-a "$(rclone cat :webdav:Movies/a.mkv)"
expect "rclone can't list a library not enabled" no "$(rclone lsf :webdav:Private >/dev/null 2>&1 && echo yes || echo no)"

echo
echo "$passed passed, $failed failed"
[ "$failed" = 0 ]
