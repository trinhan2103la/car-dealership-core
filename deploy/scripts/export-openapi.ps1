# Bản PowerShell của export-openapi.sh: xuất openapi/openapi.json bằng Swashbuckle CLI (không cần DB, không mở cổng).
#
# Dùng (từ gốc repo):  powershell -File deploy/scripts/export-openapi.ps1
# Script tự chạy `dotnet tool restore` và build Release của host.
# Các biến môi trường dưới đây là giá trị dùng-một-lần để host qua được bước kiểm cấu hình bắt buộc; chúng không đi vào
# file xuất. SERVICE_JWT_PUBLIC_KEY là khóa CÔNG KHAI sinh mới mỗi lần chạy (P-256, SubjectPublicKeyInfo DER base64).
$ErrorActionPreference = 'Stop'

$dll = 'src/CarDealership.CoreService.HttpApi.Host/bin/Release/net10.0/CarDealership.CoreService.HttpApi.Host.dll'
$out = 'openapi/openapi.json'

dotnet tool restore | Out-Null
dotnet build src/CarDealership.CoreService.HttpApi.Host -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build Release thất bại.' }

New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

$ec = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$env:SERVICE_JWT_PUBLIC_KEY = if ($ec.PSObject.Methods['ExportSubjectPublicKeyInfo']) {
    [Convert]::ToBase64String($ec.ExportSubjectPublicKeyInfo())
} else {
    # Windows PowerShell 5.1 (.NET Framework) không có ExportSubjectPublicKeyInfo: ghép SPKI P-256 bằng tay.
    # EccPublicBlob = 8 byte header + X (32) + Y (32); SPKI = tiền tố DER cố định của id-ecPublicKey/prime256v1 + 04 X Y.
    $blob = $ec.Key.Export([System.Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    $prefix = [byte[]](0x30,0x59,0x30,0x13,0x06,0x07,0x2A,0x86,0x48,0xCE,0x3D,0x02,0x01,0x06,0x08,0x2A,0x86,0x48,0xCE,0x3D,0x03,0x01,0x07,0x03,0x42,0x00,0x04)
    [Convert]::ToBase64String($prefix + $blob[8..71])
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:EDGE_KEY = 'openapi-export-throwaway-edge-key-0000'
$env:STRING_ENCRYPTION_PASSPHRASE = 'openapi-export-throwaway-passphrase'

dotnet tool run swagger tofile --output $out $dll v1
if ($LASTEXITCODE -ne 0) { throw 'Xuất OpenAPI thất bại.' }
