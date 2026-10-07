# Runs a recipe in every plugin folder. Releases are built on GitHub; see README.md.

build:
    @just _each build

test:
    @just _each test

clean:
    @just _each clean

_each recipe:
    #!/usr/bin/env bash
    set -euo pipefail
    for f in */justfile; do
        just --justfile "$f" --working-directory "$(dirname "$f")" {{recipe}}
    done
