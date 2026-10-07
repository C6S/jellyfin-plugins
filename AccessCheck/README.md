# AccessCheck

A Jellyfin plugin that answers one question for other software: may this
Jellyfin user access this path?

It checks a username and password against Jellyfin, then the account's
permissions for the library the path belongs to. Anything that serves files
from your Jellyfin libraries can use it instead of keeping its own user
database: a reverse proxy, a file or WebDAV server, a download tool, or your
own scripts. Users log in with their Jellyfin password and see exactly the
libraries Jellyfin lets them see.

The usual setup, and the example below, is nginx serving the media folders
directly (downloads, directory listings and WebDAV for VLC, Infuse, rclone or
a file manager), asking AccessCheck before every request.

## How it works

The caller sends `GET /AccessCheck` to Jellyfin with:

- the client's `Authorization: Basic …` header;
- the path being requested, either raw in an `X-Request-URI` header
  (`/media/Movies/x.mkv?a=b`, still percent-encoded) or in the URL as
  `/AccessCheck/media/Movies/x.mkv`, but not both;
- `X-Forwarded-For` with the client's address, if the caller is a proxy
  (see [Jellyfin setup](#jellyfin-setup)).

The plugin answers:

| Status | Meaning | The caller should |
|---|---|---|
| 200 | Allowed | serve the request |
| 401 | Missing or wrong credentials | refuse, passing on the `WWW-Authenticate` header so the client asks for a password |
| 403 | Valid login, but no access to this path right now | refuse |
| 400 | No path, or a path given both ways | treat as an error in its own configuration |

A request is allowed when all of these hold:

- The username and password are accepted by Jellyfin.
- The account is not disabled or locked out.
- If the client is outside the local network, the account allows remote
  connections.
- The current time is inside the account's access schedule, if it has one.
- The path is inside a library the account can access, and the account may
  download media ("Allow media downloading"). The folders above libraries,
  up to the browse root, can be listed so clients can navigate down to them.

### Paths

Paths are the ones Jellyfin knows the files by, as shown in its library
settings. If the caller publishes the files under different URLs, it must
translate them before asking (see [Other URL prefixes](#other-url-prefixes)).
With Jellyfin in a container that mounts your media at `/media`, the simplest
setup serves the same folder at `/media/`, so URLs and Jellyfin's paths match.

A raw `X-Request-URI` is resolved the way a web server would: the query string
is dropped, percent-encoding decoded and duplicate slashes merged. Any `.` or
`..` segment is refused outright. Paths are compared case-sensitively.

Anything outside the browse root (default `/media`) is refused.

### Caching

A successful login is cached for 10 minutes, so clients that fire many
parallel requests trigger only one password check. The cache entry is dropped
as soon as Jellyfin reports a change to the user (policy, password, lockout or
deletion). Access schedules and remote access are checked on every request.

Failed logins are rate-limited per username with exponential backoff, on top
of Jellyfin's own lockout.

## Requirements

- Jellyfin 12.1 or newer
- Something to ask it, such as nginx with `auth_request`

## Installation

In Jellyfin, open **Dashboard → Plugins → Repositories**, add

```
https://c6s.github.io/jellyfin-plugins/manifest.json
```

then install **AccessCheck** from the catalog and restart Jellyfin.

To build it yourself instead (needs the .NET 10 SDK), run
`dotnet publish src/ -c Release -o out/`, copy
`out/Jellyfin.Plugin.AccessCheck.dll` into a new folder inside Jellyfin's
plugin directory, for example `<jellyfin config>/plugins/AccessCheck/`, and
restart Jellyfin.

## Jellyfin setup

If the caller is a proxy, add the address it connects to Jellyfin from under
**Dashboard → Networking → Known proxies** and restart Jellyfin. Without it,
Jellyfin sees every request as coming from the proxy, so remote-access
restrictions don't work and logs show the proxy's address instead of the
client's. Add only the proxy's own address, not the whole network.

Users who should reach the files need **Allow media downloading** enabled.
This applies to administrators too.

## Example: nginx with WebDAV

nginx serves the media folder read-only, with directory listings and WebDAV,
and checks every request with
[`auth_request`](https://nginx.org/en/docs/http/ngx_http_auth_request_module.html).
WebDAV needs the
[nginx-dav-ext-module](https://github.com/arut/nginx-dav-ext-module); without
it, drop the `dav_ext_methods` line and the `add_header` lines, and plain
downloads and listings still work.

```nginx
location /media/ {
    alias /srv/media/;

    # Some WebDAV clients send OPTIONS without credentials first.
    if ($request_method = OPTIONS) {
        return 200;
    }

    auth_request /AccessCheck;

    dav_ext_methods PROPFIND OPTIONS;
    autoindex on;

    add_header Allow "OPTIONS, GET, HEAD, PROPFIND" always;
    add_header DAV "1" always;
    add_header MS-Author-Via DAV always;
}

location = /AccessCheck {
    internal;
    proxy_pass http://127.0.0.1:8096;

    proxy_pass_request_body off;
    proxy_set_header Content-Length "";
    proxy_set_header X-Request-URI $request_uri;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
}
```

- `$request_uri` must be passed raw. Don't use `$uri`: the plugin decodes the
  path itself.
- Don't add your own `WWW-Authenticate` header; nginx passes on the plugin's,
  whose realm is the Jellyfin server name.
- `internal` keeps `/AccessCheck` reachable only through `auth_request`.
- nginx treats any answer other than 2xx, 401 or 403 as an error, so a 400
  from the plugin reaches the client as a 500 and is logged as
  `auth request unexpected status: 400`.
- Serve it over HTTPS only: Basic auth sends the password with every request.

### Other URL prefixes

To serve the files at another URL, map it to the Jellyfin path and send that
instead of `$request_uri`:

```nginx
# http level
map $request_uri $media_request_uri {
    ~^/files(?<rest>/.*)?$  /media$rest;
    default                 /..;
}
```

```nginx
location /files/ {
    alias /srv/media/;
    set $access_check_uri $media_request_uri;
    auth_request /AccessCheck;
}

location = /AccessCheck {
    # as above, but:
    proxy_set_header X-Request-URI $access_check_uri;
}
```

Keep the `default`: it makes any URL the pattern doesn't match (for example
one with an encoded prefix) fail instead of being checked as some other path.

### Other callers

Any proxy or program that can make the request described in
[How it works](#how-it-works) can use the plugin. Only nginx has been tested.
A proxy's forward-auth feature must be able to set `X-Request-URI` (or call
`/AccessCheck/<path>`) and pass the client's `Authorization` header through.

## Browsing the files

With the nginx example at `https://example.com/media/`, users log in with
their Jellyfin username and password. A web browser shows plain directory
listings and can download or play single files; for anything more, use a
WebDAV client.

### rclone (Windows, also macOS and Linux)

[rclone](https://rclone.org) can mount the share as a drive, which works with
any program, and copy or sync folders. Create a remote once:

```sh
rclone config create jellyfin webdav url=https://example.com/media/ vendor=other user=alice pass=yourpassword
```

rclone stores the password obscured. Then, on Windows with
[WinFsp](https://winfsp.dev) installed:

```sh
rclone mount jellyfin: J: --read-only --network-mode --vfs-cache-mode full --vfs-cache-max-size 10G
```

`J:` then shows the libraries you can access. `--vfs-cache-mode full` keeps
recently read parts on disk so players can seek smoothly; the size limit keeps
it from filling the disk. To copy instead of mount:
`rclone copy jellyfin:Movies/SomeMovie D:\Movies\SomeMovie --progress`.

### GNOME Files (Nautilus)

In **Other Locations**, enter `davs://example.com/media/` under
**Connect to Server** and log in. The share then appears in the sidebar.

### macOS Finder

**Go → Connect to Server** (⌘K), enter `https://example.com/media/` and log
in as a registered user.

### iPhone, iPad and Apple TV

[Infuse](https://firecore.com/infuse) plays from WebDAV directly: add a share
of type WebDAV with address `example.com`, path `/media`, HTTPS, and the
Jellyfin username and password. The Files app can't connect to WebDAV.

### Windows Explorer: don't

Explorer's WebDAV support ("Map network drive" with an `https://` address)
connects, but fails on large files: Windows' WebClient service refuses files
over 50 MB by default and over 4 GB even when reconfigured. Use rclone instead.

## Configuration

Settings live in `<jellyfin config>/plugins/configurations/Jellyfin.Plugin.AccessCheck.xml`
(there is no settings page yet). Restart Jellyfin after editing.

| Setting | Default | Meaning |
|---|---|---|
| `BrowseRoot` | `/media` | The only part of Jellyfin's file tree the plugin answers for. `/` allows any path; empty or invalid denies everything. |
| `RateLimitBaseSeconds` | `2` | Lockout after the first failed login for a username. |
| `RateLimitExponent` | `1.7` | Growth of the lockout with each further failure. |
| `RateLimitMaxSeconds` | `600` | Longest lockout. Earlier failures are forgotten after this long without new ones, counted from the end of the last lockout. |

## Limitations

- Permissions are per library. Parental rating limits and tag rules don't
  apply to direct file access.
- If one library's folder is inside another's, the first library Jellyfin
  lists decides access.
- The first login outside an access schedule gets 401 rather than 403, because
  Jellyfin reports it the same way as a wrong password.
- Only Jellyfin's built-in username and password login is supported. It is
  not tested with, and not expected to work alongside, plugins that change how
  users log in, such as SSO, two-factor (TOTP) or LDAP: the plugin only ever
  sees a username and password, so a second factor is never asked for, and
  accounts without a Jellyfin password can't log in at all.

## Development

With the .NET 10 SDK and [just](https://github.com/casey/just):

```sh
just build   # debug build into src/bin/
just test    # run the unit tests
just clean
```

Integration tests run on GitHub Actions only
([`accesscheck.yml`](../.github/workflows/accesscheck.yml)): Jellyfin 12.1 and
the newest 12.x with the plugin installed, behind nginx configured as in the
[example](#example-nginx-with-webdav), checked with curl and rclone. The setup
and the checks are in [`integration/`](integration).

Releases are built by the same workflow: pushing a tag `AccessCheck/v<version>`
runs all the tests, then publishes the GitHub release and adds it to the
plugin repository.

## License

[CC0 1.0](../LICENSE): public domain, no conditions.
