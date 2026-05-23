#!/usr/bin/env bash
# Download and extract the StockLensDatabaseMCP stock-lens catalog from
# GitHub Releases. Defaults to the latest release; pass a tag like
# "v1.0.0" as $1 to pin to a specific version.
#
# Usage:
#   ./fetch-catalog.sh                  # latest, into ./catalogs/
#   ./fetch-catalog.sh v1.0.0           # specific tag, into ./catalogs/
#   ./fetch-catalog.sh latest ~/lenshh  # latest, into ~/lenshh/catalogs/

set -euo pipefail

VERSION="${1:-latest}"
DEST_PARENT="${2:-.}"
REPO="SynapseOptics/StockLensDatabaseMCP"

if [ "$VERSION" = "latest" ]; then
    URL="https://github.com/${REPO}/releases/latest/download/catalogs.zip"
else
    URL="https://github.com/${REPO}/releases/download/${VERSION}/catalogs.zip"
fi

DEST="${DEST_PARENT%/}/catalogs"
TMP="$(mktemp -t stocklens-catalog.XXXXXX.zip)"
trap 'rm -f "$TMP"' EXIT

echo "Fetching $URL"
if command -v curl >/dev/null 2>&1; then
    curl -fL -o "$TMP" "$URL"
elif command -v wget >/dev/null 2>&1; then
    wget -O "$TMP" "$URL"
else
    echo "Need curl or wget to download the catalog." >&2
    exit 1
fi

# Extract. The archive contains a top-level "catalogs/" entry, so we
# unzip into DEST_PARENT and the directory pops out where we want it.
mkdir -p "$DEST_PARENT"
if [ -d "$DEST" ]; then
    echo "Warning: $DEST already exists — overwriting." >&2
fi

if command -v unzip >/dev/null 2>&1; then
    unzip -q -o "$TMP" -d "$DEST_PARENT"
else
    echo "Need unzip to extract the catalog." >&2
    exit 1
fi

DB="$DEST/stock-lens-catalog.sqlite"
if [ ! -f "$DB" ]; then
    echo "Extraction failed: $DB not found." >&2
    exit 1
fi

SIZE=$(du -h "$DB" | awk '{print $1}')
COUNT=$(find "$DEST/Lenses" -name '*.lhlt' 2>/dev/null | wc -l | awk '{print $1}')

echo
echo "Catalog installed to: $DEST"
echo "  stock-lens-catalog.sqlite  $SIZE"
echo "  Lenses/*.lhlt              $COUNT files"
echo
echo "Point your MCP at it via:  export LENSHH_CATALOGS_DIR=\"$(cd "$DEST" && pwd)\""
