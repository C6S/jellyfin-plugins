#!/usr/bin/env bash
# Creates the media folder the integration tests serve.
set -euo pipefail

dir=$1
mkdir -p "$dir"/{Movies,Shows,Private,NotALibrary}
printf movie-a > "$dir/Movies/a.mkv"
printf show-s > "$dir/Shows/s.mkv"
printf private-p > "$dir/Private/p.mkv"
printf other-x > "$dir/NotALibrary/x.mkv"
# Sparse: 5 GiB in size, no space on disk.
truncate -s 5G "$dir/Movies/big.mkv"
