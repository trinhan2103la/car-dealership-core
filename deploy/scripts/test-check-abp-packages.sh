#!/usr/bin/env bash
# Ngày 20: tự kiểm script check-abp-packages.sh bằng fixture JSON.
#   - pro-package.json    (có Volo.Abp.Identity.Pro.Domain)  -> PHẢI thoát 1 (denylist)
#   - unlisted-volo.json  (Volo.* ngoài allowlist)           -> PHẢI thoát 1 (allowlist)
#   - clean.json          (chỉ package hợp lệ)                -> PHẢI thoát 0
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CHECK="$SCRIPT_DIR/check-abp-packages.sh"
FIX="$SCRIPT_DIR/fixtures"
failures=0

expect() { # expect <fixture> <exit mong đợi> <chuỗi phải có trong output (rỗng = bỏ qua)>
  local fixture="$1" want="$2" needle="$3" out code
  out="$(bash "$CHECK" "$FIX/$fixture" 2>&1)"; code=$?
  if [ "$code" -ne "$want" ]; then
    echo "FAIL  $fixture: exit=$code, mong doi $want"; echo "$out" | sed 's/^/      /'
    failures=$((failures + 1)); return
  fi
  if [ -n "$needle" ] && ! printf '%s' "$out" | grep -qF "$needle"; then
    echo "FAIL  $fixture: output thieu '$needle'"; echo "$out" | sed 's/^/      /'
    failures=$((failures + 1)); return
  fi
  echo "PASS  $fixture (exit=$code)"
}

expect pro-package.json   1 "Volo.Abp.Identity.Pro.Domain"
expect unlisted-volo.json 1 "Volo.Abp.SomethingNotReviewed"
expect clean.json         0 "OK:"

if [ "$failures" -ne 0 ]; then
  echo "$failures test that bai"; exit 1
fi
echo "Tat ca test check-abp-packages dat."
