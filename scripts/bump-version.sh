#!/usr/bin/env bash
# bump-version.sh — Standardised version bumping for Allow2 C# SDK
# Usage: ./scripts/bump-version.sh [prerelease|patch|minor|major] [--preid alpha|beta|rc]
#
# Examples:
#   ./scripts/bump-version.sh prerelease --preid alpha   # 2.0.0-alpha.1 → 2.0.0-alpha.2
#   ./scripts/bump-version.sh patch                       # 2.0.0 → 2.0.1
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION_FILE="src/Allow2/Allow2.csproj"
BUMP="${1:-prerelease}"
PREID=""
if [[ "${2:-}" == "--preid" ]]; then PREID="${3:-alpha}"; fi

# Read current version from .csproj
OLD=$(grep '<Version>' "$VERSION_FILE" | head -1 | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/')

# Parse: 2.0.0 or 2.0.0-alpha.1
if [[ "$OLD" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)(-([a-z]+)\.([0-9]+))?$ ]]; then
  MAJOR="${BASH_REMATCH[1]}"
  MINOR="${BASH_REMATCH[2]}"
  PATCH="${BASH_REMATCH[3]}"
  PRE_TYPE="${BASH_REMATCH[5]:-}"
  PRE_NUM="${BASH_REMATCH[6]:-}"
else
  echo "Cannot parse version: $OLD" >&2; exit 1
fi

case "$BUMP" in
  prerelease)
    PRE="${PREID:-${PRE_TYPE:-alpha}}"
    if [[ "$PRE_TYPE" == "$PRE" && -n "$PRE_NUM" ]]; then
      NEW="${MAJOR}.${MINOR}.${PATCH}-${PRE}.$((PRE_NUM + 1))"
    else
      NEW="${MAJOR}.${MINOR}.${PATCH}-${PRE}.1"
    fi
    ;;
  patch) NEW="$MAJOR.$MINOR.$((PATCH + 1))" ;;
  minor) NEW="$MAJOR.$((MINOR + 1)).0" ;;
  major) NEW="$((MAJOR + 1)).0.0" ;;
  *) echo "Usage: $0 [prerelease|patch|minor|major] [--preid alpha|beta|rc]" >&2; exit 1 ;;
esac

sed -i "s/<Version>${OLD}<\/Version>/<Version>${NEW}<\/Version>/" "$VERSION_FILE"

echo "$OLD → $NEW"
git add "$VERSION_FILE"
git commit -m "v$NEW"
git tag "v$NEW"
echo "Tagged v$NEW — push with: git push origin master --tags"
