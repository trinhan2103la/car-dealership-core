# Phiên bản công cụ và thư viện

- **Ghi ngày:** 2026-10-29 (Ngày 22)
- **Nguồn sự thật:** các file `*.csproj`, `common.props`, `deploy/docker-compose.yml`, `Dockerfile*`. File này chỉ là bản đọc nhanh; lệch thì sửa file này theo code.
- Đặt ở repo core theo ADR-011; phần BFF và web ghi ở mục cuối, nguồn sự thật của chúng là `package.json` từng repo.

## Công cụ trên máy dev

| Công cụ        | Phiên bản                | Ghi chú                                                |
| -------------- | ------------------------ | ------------------------------------------------------ |
| .NET SDK       | 10.0.401                 | Target `net10.0` (LTS)                                 |
| ABP CLI        | 10.7.0                   | Package `Volo.Abp.Studio.Cli`, lệnh vẫn là `abp`       |
| Node.js        | 24.22.0                  | LTS                                                    |
| pnpm           | 10.11.1                  | Khai báo `packageManager` trong `package.json`         |
| Docker         | 29.6.1                   |                                                        |
| Docker Compose | v5.2.0                   |                                                        |
| psql           | 18.1                     | Khớp PostgreSQL 18                                     |
| dotnet-ef      | 10.0.13                  | Công cụ global, ghi ở Ngày 9                           |
| git            | 2.53.0.windows.2         | Đo ở Ngày 1, chưa đo lại                               |

## Core_API (đo từ solution thật bằng `dotnet list package --include-transitive`)

| Thành phần                                 | Phiên bản | Ghi chú                                                                  |
| ------------------------------------------ | --------- | ------------------------------------------------------------------------ |
| ABP Framework (`Volo.Abp.*`)               | 10.7.0    | Dòng 10.7.x, không phải 10.6.x như design ghim ban đầu                   |
| Microsoft.EntityFrameworkCore              | 10.0.11   | Transitive                                                               |
| Npgsql.EntityFrameworkCore.PostgreSQL      | 10.0.3    | Transitive qua `Volo.Abp.EntityFrameworkCore.PostgreSql`                 |
| Npgsql                                     | 10.0.3    |                                                                          |
| Serilog                                    | 4.3.0     | Có thêm 4.2.0 trong đồ thị transitive                                    |
| Serilog.AspNetCore                         | 9.0.0     | Ghim trong `HttpApi.Host.csproj`                                         |
| Serilog.Sinks.Async                        | 2.1.0     |                                                                          |
| Serilog.Formatting.Compact                 | 3.0.0     | Log JSON một dòng                                                        |
| Swashbuckle.AspNetCore                     | 10.2.3    | Transitive qua `Volo.Abp.Swashbuckle`                                    |
| System.Security.Cryptography.Pkcs / .Xml   | 10.0.13   | **Pin bảo mật** trong `common.props` (CVE HIGH ở ≤ 10.0.12); gỡ khi ABP nâng |

## Thư viện test

| Thư viện                              | Phiên bản | Ghi chú                                                                                            |
| ------------------------------------- | --------- | -------------------------------------------------------------------------------------------------- |
| xUnit                                 | 2.9.3     | Chưa có `Assert.SkipUnless` (API của v3); test cần DB dùng `DbProbe` và trả sớm khi không có Postgres |
| Shouldly                              | 4.3.0     |                                                                                                    |
| NSubstitute                           | 5.3.0     |                                                                                                    |
| Microsoft.NET.Test.Sdk                | 17.14.1   |                                                                                                    |
| NetArchTest.Rules                     | 1.3.2     |                                                                                                    |
| Microsoft.AspNetCore.Mvc.Testing      | 10.0.0    | Chọn ở Ngày 11 mà chưa đối chiếu; bản ổn định mới nhất là 10.0.14, nên nâng cho khớp runtime       |
| Serilog.Sinks.InMemory                | 0.11.0    | Bản ổn định mới nhất là 2.0.0, chưa nâng                                                           |

## Container và CI

| Thành phần                                 | Giá trị              | Ghi chú                                                          |
| ------------------------------------------ | -------------------- | ---------------------------------------------------------------- |
| PostgreSQL                                 | `postgres:18`        | Tag theo major. Volume phải mount `/var/lib/postgresql` (không phải `/data`) |
| Image build / runtime core-api             | `dotnet/sdk:10.0`, `dotnet/aspnet:10.0` | Tag nổi theo minor, **chưa ghim patch hay digest**  |
| Image runtime migrator                     | `dotnet/runtime:10.0` | Như trên                                                         |
| GitHub Actions                             | `checkout@v4`, `setup-dotnet@v4` (`10.0.x`), `setup-node@v4` (24), `pnpm/action-setup@v4`, `gitleaks-action@v2` | Ghim theo major, không theo SHA |

## Quyết định lệch so với design

1. **Quản lý version package:** ghim exact trong từng `.csproj` kèm `common.props`, **không** dùng `Directory.Packages.props` (central package management) như design và ADR-011 mô tả ban đầu. Quyết định từ Ngày 5; có thể chuyển sang CPM sau mà không đổi hành vi.
2. **Tag image nổi** (`postgres:18`, `dotnet/*:10.0`) trái với nguyên tắc "ghim exact version" của design. Chấp nhận trong giai đoạn dev; cần ghim lại trước khi deploy demo.
3. **Định dạng solution:** `.slnx` (không phải `.sln`); build bằng `dotnet build CarDealership.CoreService.slnx`.

## BFF (`car-dealership-bff`, ghim exact trong `package.json`)

| Thành phần                         | Phiên bản | Ghi chú                                              |
| ---------------------------------- | --------- | ---------------------------------------------------- |
| NestJS (`@nestjs/*`)               | 12.3.2    | **ESM-only**, nên khác plan gốc (NestJS 11); xem task 1.17 |
| `@nestjs/terminus` / `@nestjs/config` | 12.1.0 / 12.0.1 |                                                |
| TypeScript                         | 5.9.3     | `module`/`moduleResolution`: `NodeNext`              |
| vitest + `unplugin-swc` + `@swc/core` | 5.0.5 / 2.0.0 / 1.16.20 | Thay Jest/ts-jest vì không chạy được ESM |
| pino / pino-http / nestjs-pino     | 9.14.0 / 10.5.0 / 5.3.1 | Log JSON                               |

## Web (`car-dealership-web`)

Chưa có ứng dụng nào. `apps/public-web` (Ngày 113+) và `apps/admin-portal` (Ngày 140+) sẽ ghi phiên bản khi tạo.

## Cách cập nhật

Khi đổi version package, đổi trong `.csproj` hoặc `package.json` trước, rồi sửa file này trong cùng PR. Bảng "Tech stack" ở `design.md` và `.kiro/docs/car-dealership-platform/versions-draft.md` đã được ghi chú để tham chiếu về đây.
