#!/usr/bin/env bash
# Configures the fresh Jellyfin from compose.yaml through its API: the
# startup wizard, three libraries, one user per case under test, and the
# network settings. Writes what test.sh needs to $STATE.
set -euo pipefail

: "${STATE:?}" "${COMPOSE:?}"

JF=http://127.0.0.1:8096
CLIENT='MediaBrowser Client="AccessCheck CI", Device="CI", DeviceId="accesscheck-ci", Version="1.0"'
TOKEN=

# Failures are reported as a GitHub annotation, which can be read without
# logging in. api() mostly runs in a subshell, so the reason goes through a
# file to the exit trap of the main shell.
REASON=$(mktemp)
report() {
    local status=$?
    if [ "$status" != 0 ]; then
        local reason
        reason=$(cat "$REASON")
        echo "::error title=Integration setup (Jellyfin ${JELLYFIN_TAG:-?})::${reason:-setup.sh failed with status $status}"
    fi
    rm -f "$REASON"
}
trap report EXIT

die() {
    echo "$1" >&2
    printf '%s' "${1//$'\n'/ }" >"$REASON"
    exit 1
}

api() { # method path [curl args...]
    local method=$1 path=$2 response status
    shift 2
    response=$(curl -sS -w '\n%{http_code}' -X "$method" "$JF$path" \
        -H "Authorization: $CLIENT${TOKEN:+, Token=\"$TOKEN\"}" \
        -H 'Content-Type: application/json' \
        "$@") || die "$method $path: request failed"
    status=${response##*$'\n'}
    response=${response%$'\n'*}
    [ "$status" -lt 400 ] || die "$method $path: $status ${response:0:300}"
    printf '%s' "$response"
}

wait_healthy() {
    for _ in $(seq 120); do
        [ "$(curl -fsS "$JF/health" 2>/dev/null)" = Healthy ] && return
        sleep 1
    done
    die "Jellyfin did not become healthy within 2 minutes"
}

new_user() { # name -> id; the password is "<name>-pw"
    api POST /Users/New -d "$(jq -n --arg n "$1" '{Name: $n, Password: ($n + "-pw")}')" | jq -r .Id
}

set_policy() { # id jq-filter
    local policy
    policy=$(api GET "/Users/$1" | jq ".Policy | $2")
    api POST "/Users/$1/Policy" -d "$policy"
}

echo "== Startup wizard"
wait_healthy
# The accent tests the realm's ASCII fallback: the plugin should send "Cafe Test".
api POST /Startup/Configuration \
    -d '{"ServerName": "Café Test", "UICulture": "en-US", "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"}'
api GET /Startup/User >/dev/null
api POST /Startup/User -d '{"Name": "admin", "Password": "admin-pw"}'
api POST /Startup/RemoteAccess -d '{"EnableRemoteAccess": true}'
api POST /Startup/Complete
TOKEN=$(api POST /Users/AuthenticateByName -d '{"Username": "admin", "Pw": "admin-pw"}' | jq -r .AccessToken)

echo "== Libraries"
for lib in Movies Shows Private; do
    api POST "/Library/VirtualFolders?name=$lib&collectionType=movies&paths=/media/$lib&refreshLibrary=false" -d '{}'
done
# Library item ids, which user policies refer to, exist once the scan has run.
api POST /Library/Refresh
for _ in $(seq 120); do
    folders=$(api GET /Library/VirtualFolders)
    [ "$(jq '[.[] | select(.ItemId != null and .ItemId != "")] | length' <<<"$folders")" = 3 ] && break
    sleep 1
done
# ItemId comes without dashes; policies hold Guids, which Jellyfin writes
# with them.
library_id() {
    jq -r --arg name "$1" '.[] | select(.Name == $name) | .ItemId' <<<"$folders" \
        | sed -E 's/^(.{8})(.{4})(.{4})(.{4})(.{12})$/\1-\2-\3-\4-\5/'
}
movies=$(library_id Movies)
shows=$(library_id Shows)
if [ -z "$movies" ] || [ -z "$shows" ]; then
    die "Libraries have no item ids after 2 minutes: $folders"
fi

echo "== Users"
# Two days ahead, so the schedule can't become current during the run.
not_today=$(date -u -d '+2 days' +%A)

alice=$(new_user alice)
set_policy "$alice" ".EnableAllFolders = false | .EnabledFolders = [\"$movies\"] | .EnableContentDownloading = true"
bob=$(new_user bob)
set_policy "$bob" '.EnableAllFolders = true | .EnableContentDownloading = false'
carol=$(new_user carol)
set_policy "$carol" '.EnableAllFolders = true | .EnableContentDownloading = true | .EnableRemoteAccess = false'
dave=$(new_user dave)
set_policy "$dave" ".EnableAllFolders = true | .EnableContentDownloading = true
    | .AccessSchedules = [{DayOfWeek: \"$not_today\", StartHour: 0, EndHour: 24, UserId: \"$dave\"}]"
eve=$(new_user eve)
set_policy "$eve" '.EnableAllFolders = true | .EnableContentDownloading = true | .IsDisabled = true'
admin=$(api GET /Users/Me | jq -r .Id)
set_policy "$admin" '.EnableContentDownloading = true'

echo "== Network"
# nginx is the only trusted proxy. Only 172.30.0.100 counts as local, so the
# test runner (the Docker gateway) is remote and a container started with
# that address is local.
network=$(api GET /System/Configuration/network \
    | jq '.KnownProxies = ["172.30.0.20"] | .LocalNetworkSubnets = ["172.30.0.100/32"]')
api POST /System/Configuration/network -d "$network"

# Known proxies are only read at startup.
$COMPOSE restart jellyfin
wait_healthy

cat >"$STATE" <<STATE
TOKEN=$TOKEN
ALICE=$alice
SHOWS=$shows
STATE
echo "== Ready"
