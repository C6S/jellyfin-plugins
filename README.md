# Jellyfin plugins

Plugins for [Jellyfin](https://jellyfin.org).

| Plugin | What it does |
|---|---|
| [AccessCheck](AccessCheck) | Lets other software, such as nginx serving files or WebDAV, check a Jellyfin login and the user's library permissions before each request. |

## Installing

In Jellyfin, open **Dashboard → Plugins → Repositories**, add

```
https://c6s.github.io/jellyfin-plugins/manifest.json
```

and install the plugins from the catalog. Each plugin's README covers its
setup.

## Development

The repository includes a [devenv](https://devenv.sh) shell with the .NET 10
SDK and [just](https://github.com/casey/just). From the root, `just build`,
`just test` and `just clean` run in every plugin folder.

Each plugin has its own GitHub Actions workflow in `.github/workflows/`,
which runs its tests on every push.

### Releasing

Push a tag `<plugin>/v<version>`, for example:

```sh
git tag AccessCheck/v1.0.0
git push origin AccessCheck/v1.0.0
```

The plugin's workflow runs all its tests, then
[`scripts/release.sh`](scripts/release.sh) builds the plugin, publishes the
GitHub release and adds the version to `manifest.json` on the `gh-pages`
branch, which GitHub Pages serves as the plugin repository above.

## License

[CC0 1.0](LICENSE): public domain, no conditions.
