#!/usr/bin/env bash
# Publishes the NORS console relay as a self-contained executable for Linux/macOS hosts
# (no .NET runtime required on the target). The admin GUI is Windows-only; on Linux/macOS use
# this console relay (interactive, or --headless for a service).
#
# Usage:  ./publish-server.sh [runtime]
#   runtime defaults to linux-x64. Others: osx-x64, osx-arm64, win-x64.
set -euo pipefail

RID="${1:-linux-x64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="$ROOT/dist/server/$RID"

echo "Publishing NORS console relay -> $RID"
dotnet publish "$ROOT/src/NORS.Server/NORS.Server.csproj" \
    -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$OUT"

chmod +x "$OUT/NORS.Server" 2>/dev/null || true
echo "Done. Run:  $OUT/NORS.Server --port 5555 --name 'My NORS'"
echo "Open UDP 5555 on the host firewall so others can connect."
