#!/usr/bin/env bash
# Publishes the web build (Builds/WebGL) to GitHub Pages: force-pushes it as the only commit of the repository's
# gh-pages branch, then makes sure Pages serves that branch. The build goes up as plain files (no Git LFS: Pages
# would serve LFS pointers instead of the files).
#
#   Tools/deploy_pages.sh            # deploy to the repository at the "origin" remote
#   Tools/deploy_pages.sh --build    # build the web player first (headless Unity)
#
# Needs: a GitHub remote named origin, and the gh CLI signed in (only to switch Pages on the first time).
set -euo pipefail
cd "$(dirname "$0")/.."

UNITY=${UNITY:-/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity}
BUILD=Builds/WebGL

if [[ "${1:-}" == "--build" ]]; then
  echo "Building the web player…"
  "$UNITY" -batchmode -projectPath . -executeMethod Facecast.EditorTools.PlayerBuilds.BuildWeb -logFile Logs/deploy_build.log \
    || { echo "The build failed; see Logs/deploy_build.log"; exit 1; }
fi
[[ -f "$BUILD/index.html" ]] || { echo "No web build in $BUILD. Run with --build, or build it in Unity first (Facecast > Player Builds > Web)."; exit 1; }

REMOTE=$(git remote get-url origin 2>/dev/null) || { echo "No 'origin' remote. Create the GitHub repository first (see README)."; exit 1; }
SLUG=$(echo "$REMOTE" | sed -E 's#(git@github.com:|https://github.com/)##; s#\.git$##')

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
cp -R "$BUILD/." "$STAGE/"
touch "$STAGE/.nojekyll"   # serve files as they are, without Jekyll
(
  cd "$STAGE"
  git init -q -b gh-pages
  git add -A
  git -c user.name="$(git -C "$OLDPWD" config user.name || echo Facecast)" -c user.email="$(git -C "$OLDPWD" config user.email || echo facecast@localhost)" \
    commit -q -m "Facecast web build ($(git -C "$OLDPWD" rev-parse --short HEAD 2>/dev/null || echo unversioned))"
  git push -q -f "$REMOTE" gh-pages
)
echo "Pushed $BUILD to $SLUG (gh-pages)."

if command -v gh >/dev/null; then
  if ! gh api "repos/$SLUG/pages" >/dev/null 2>&1; then
    gh api -X POST "repos/$SLUG/pages" -f "source[branch]=gh-pages" -f "source[path]=/" >/dev/null \
      && echo "Switched GitHub Pages on for the gh-pages branch."
  fi
  URL=$(gh api "repos/$SLUG/pages" --jq .html_url 2>/dev/null || true)
  [[ -n "$URL" ]] && echo "Live in a minute or two at $URL"
fi
