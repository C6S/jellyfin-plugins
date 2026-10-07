#!/usr/bin/env bash
# Builds a plugin, publishes it as a GitHub release and adds it to the plugin
# repository manifest on the gh-pages branch. Run by the plugin's workflow for
# a tag <plugin>/v<version>, after its tests pass.
#
# Usage: scripts/release.sh <plugin> <version>, e.g. AccessCheck 1.0.0
set -euo pipefail

: "${GITHUB_REPOSITORY:?}" "${GH_TOKEN:?}"

plugin=$1
version=$2
tag="$plugin/v$version"
# Jellyfin versions have four parts.
full_version="$version.0"
zip="${plugin}_$full_version.zip"
work=$(mktemp -d)

echo "== Build"
dotnet publish "$plugin/src/" -c Release -o "$work/build"
# envsubst takes the variable names literally.
# shellcheck disable=SC2016
PLUGIN=$plugin VERSION=$full_version TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%S.0000000Z)" \
    envsubst '${PLUGIN} ${VERSION} ${TIMESTAMP}' <"$plugin/meta.json.tpl" >"$work/build/meta.json"
zip -q -j "$work/$zip" "$work/build"/*

echo "== GitHub release"
gh release create "$tag" "$work/$zip" --verify-tag --title "$plugin $version" --generate-notes

echo "== Manifest"
meta="$work/build/meta.json"
entry=$(jq \
    --arg checksum "$(md5sum "$work/$zip" | cut -d' ' -f1)" \
    --arg sourceUrl "https://github.com/$GITHUB_REPOSITORY/releases/download/$tag/$zip" \
    '{version, targetAbi, timestamp, changelog: "", checksum: $checksum, sourceUrl: $sourceUrl}' "$meta")
info=$(jq '{guid, name, description, overview, owner, category, imageUrl: .imagePath}' "$meta")

pages="$work/pages"
if git fetch --quiet origin gh-pages; then
    git worktree add --quiet -B gh-pages "$pages" FETCH_HEAD
else
    git worktree add --quiet --orphan -b gh-pages "$pages"
fi
manifest="$pages/manifest.json"
[ -f "$manifest" ] || echo '[]' >"$manifest"
touch "$pages/.nojekyll"

# Add the plugin if it's new, refresh its details, and put this version first
# (replacing a previous build of the same version).
jq --argjson info "$info" --argjson entry "$entry" '
    (if any(.[]; .guid == $info.guid) then . else . + [$info + {versions: []}] end)
    | map(if .guid == $info.guid
          then . + $info
               | .versions = [$entry] + [.versions[] | select(.version != $entry.version)]
          else . end)
' "$manifest" >"$work/manifest.json"
mv "$work/manifest.json" "$manifest"

git -C "$pages" add manifest.json .nojekyll
git -C "$pages" \
    -c user.name="github-actions[bot]" \
    -c user.email="41898282+github-actions[bot]@users.noreply.github.com" \
    commit --quiet -m "$plugin $version"
git -C "$pages" push --quiet origin gh-pages

echo "== Released $plugin $version"
