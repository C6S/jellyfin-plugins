#!/usr/bin/env bash
# Prints the index page of the plugin repository site, which sends visitors
# to the GitHub repository. Jellyfin itself only reads manifest.json.
#
# Usage: scripts/pages-index.sh <repository URL>
set -euo pipefail

repo=$1

cat <<HTML
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Jellyfin plugins</title>
<meta http-equiv="refresh" content="0; url=$repo">
<link rel="canonical" href="$repo">
</head>
<body>
<p>Jellyfin plugin repository. The plugins and their documentation are at
<a href="$repo">$repo</a>.</p>
<p>To install them, add <code>manifest.json</code> from this address under
Dashboard → Plugins → Repositories in Jellyfin.</p>
</body>
</html>
HTML
