#!/usr/bin/env bash
# Ngày 20 (Req 29.1–29.3): chặn package ABP thương mại / ngoài allowlist.
#
# Dùng:
#   bash deploy/scripts/check-abp-packages.sh                 # quét solution thật (cần đã `dotnet restore`)
#   bash deploy/scripts/check-abp-packages.sh path/to.json    # quét file JSON có sẵn (dùng cho fixture/test)
#
# Đầu vào: JSON của `dotnet list <sln> package --include-transitive --format json`
# (quét cả package trực tiếp lẫn transitive).
#
# Luật (design "Giấy phép ABP và danh sách package bị cấm"):
#   1. Denylist theo tên: Volo.Abp.Commercial*, Volo.Saas*, Volo.Payment*, Volo.Abp.LeptonTheme*,
#      Volo.FileManagement*, Volo.Chat*, Volo.Forms*, Volo.Abp.Gdpr*, thành phần ".Pro." / hậu tố ".Pro",
#      và tên chứa "Commercial".
#   2. Allowlist: mọi package bắt đầu bằng "Volo." phải có trong abp-allowlist.txt.
#   3. npm scope "@volo/" (commercial) không được xuất hiện trong package.json của repo.
# Thoát 0 nếu sạch, 1 nếu vi phạm, 2 nếu không đọc được đầu vào.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ALLOWLIST="${ABP_ALLOWLIST:-$SCRIPT_DIR/abp-allowlist.txt}"
INPUT_JSON="${1:-}"

DENY_REGEX='^(Volo\.Abp\.Commercial|Volo\.Saas|Volo\.Payment|Volo\.Abp\.LeptonTheme|Volo\.FileManagement|Volo\.Chat|Volo\.Forms|Volo\.Abp\.Gdpr)|\.Pro(\.|$)|Commercial'

TMP_JSON=""
cleanup() { [ -n "$TMP_JSON" ] && rm -f "$TMP_JSON"; return 0; }
trap cleanup EXIT

if [ -z "$INPUT_JSON" ]; then
  TMP_JSON="$(mktemp)"
  dotnet list "$REPO_ROOT/CarDealership.CoreService.slnx" package --include-transitive --format json > "$TMP_JSON"
  INPUT_JSON="$TMP_JSON"
fi

if [ ! -s "$INPUT_JSON" ]; then
  echo "ERROR: khong doc duoc dau vao package JSON: $INPUT_JSON" >&2
  exit 2
fi
if [ ! -f "$ALLOWLIST" ]; then
  echo "ERROR: khong thay allowlist: $ALLOWLIST" >&2
  exit 2
fi

# Danh sách id package duy nhất (loại \r nếu file sinh trên Windows).
ids="$(grep -o '"id": *"[^"]*"' "$INPUT_JSON" | sed -E 's/"id": *"([^"]*)"/\1/' | tr -d '\r' | LC_ALL=C sort -u || true)"
if [ -z "$ids" ]; then
  echo "ERROR: khong tim thay package nao trong $INPUT_JSON (JSON sai dinh dang?)" >&2
  exit 2
fi

violations=0

# 1) Denylist theo tên.
denied="$(printf '%s\n' "$ids" | grep -Ei "$DENY_REGEX" || true)"
if [ -n "$denied" ]; then
  echo "VI PHAM [denylist]: package thuong mai/bi cam:" >&2
  printf '  - %s\n' $denied >&2
  violations=1
fi

# 2) Allowlist cho mọi Volo.*
allow="$(grep -v '^[[:space:]]*#' "$ALLOWLIST" | tr -d '\r' | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//' | grep -v '^$' | LC_ALL=C sort -u || true)"
volo="$(printf '%s\n' "$ids" | grep -E '^Volo\.' | LC_ALL=C sort -u || true)"
if [ -n "$volo" ]; then
  not_allowed="$(LC_ALL=C comm -23 <(printf '%s\n' "$volo") <(printf '%s\n' "$allow") || true)"
  if [ -n "$not_allowed" ]; then
    echo "VI PHAM [allowlist]: package Volo.* ngoai abp-allowlist.txt (can duyet va them neu hop le):" >&2
    printf '  - %s\n' $not_allowed >&2
    violations=1
  fi
fi

# 3) npm scope @volo/ trong package.json (bo qua node_modules).
npm_hits="$(find "$REPO_ROOT" -name package.json -not -path '*/node_modules/*' -print0 | xargs -0 grep -l '"@volo/' 2>/dev/null || true)"
if [ -n "$npm_hits" ]; then
  echo "VI PHAM [npm]: scope @volo/ (commercial) trong:" >&2
  printf '  - %s\n' $npm_hits >&2
  violations=1
fi

if [ "$violations" -ne 0 ]; then
  exit 1
fi

echo "OK: $(printf '%s\n' "$ids" | wc -l | tr -d ' ') package, $(printf '%s\n' "$volo" | grep -c . || true) Volo.* deu nam trong allowlist, khong co package thuong mai."
