#!/usr/bin/env bash
# Xuất openapi/openapi.json bằng Swashbuckle CLI ở build time (không cần DB, không mở cổng).
#
# Dùng (từ gốc repo):  bash deploy/scripts/export-openapi.sh
# Yêu cầu: `dotnet tool restore` + `dotnet build src/CarDealership.CoreService.HttpApi.Host -c Release` đã chạy.
# EDGE_KEY / SERVICE_JWT_PUBLIC_KEY là giá trị dùng-một-lần chỉ để host khởi tạo qua bước kiểm cấu hình bắt buộc;
# chúng không đi vào file xuất.
set -euo pipefail

DLL="src/CarDealership.CoreService.HttpApi.Host/bin/Release/net10.0/CarDealership.CoreService.HttpApi.Host.dll"
OUT="openapi/openapi.json"

mkdir -p "$(dirname "$OUT")"

export ASPNETCORE_ENVIRONMENT=Development
export EDGE_KEY="openapi-export-throwaway-edge-key-0000"
SERVICE_JWT_PUBLIC_KEY="$(openssl ecparam -name prime256v1 -genkey -noout | openssl ec -pubout -outform DER 2>/dev/null | base64 -w0)"
export SERVICE_JWT_PUBLIC_KEY

dotnet tool run swagger tofile --output "$OUT" "$DLL" v1
