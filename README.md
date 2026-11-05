# car-dealership-core

Core API (ABP Framework 10.7 / .NET 10) của dự án **Car Dealership Platform** — tầng duy nhất thực thi quy tắc nghiệp vụ, truy cập PostgreSQL qua EF Core. Repo này cũng chứa Docker Compose xuyên service để chạy toàn hệ local.

## Hệ 3 repo

Clone cả ba repo **ngang hàng (sibling)** trong cùng một thư mục cha:

```
D:\WorkSpace\FRT\PROJECT_PERSONAL\
├─ car-dealership-web\     # Next.js + React (pnpm monorepo)
├─ car-dealership-bff\     # NestJS BFF
└─ car-dealership-core\    # ABP / .NET 10 (repo này) + deploy/docker-compose.yml
```

Docker Compose dùng build context tương đối tới `../car-dealership-bff` và `../car-dealership-web`, nên bắt buộc ba repo phải sibling.

## Chạy local

Yêu cầu: Docker Desktop đang chạy, .NET 10 SDK, psql.

### Chỉ PostgreSQL

```powershell
# 1) Chuẩn bị biến môi trường
Copy-Item deploy/.env.example deploy/.env   # rồi chỉnh nếu cần

# 2) Khởi động PostgreSQL 18
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d postgres

# 3) Kiểm tra
docker compose -f deploy/docker-compose.yml ps
psql -h localhost -p 5432 -U cdp -d cdp -c "select version();"
```

### Toàn bộ Core_API qua Docker

Thứ tự tự động: `postgres` (healthy) → `migrator` (chạy migration + seed, thoát 0) → `core-api`.

```powershell
# Dựng lại từ đầu (xóa cả dữ liệu cũ) rồi build + chạy
docker compose -f deploy/docker-compose.yml down -v
docker compose -f deploy/docker-compose.yml up -d --build

# Theo dõi tới khi migrator thoát và core-api lên
docker compose -f deploy/docker-compose.yml ps

# Health (từ host, qua cổng đã map). live không phụ thuộc DB; ready kiểm DB.
curl.exe -i http://localhost:8080/health/live     # 200, body rỗng
curl.exe -i http://localhost:8080/health/ready    # 200 khi DB sẵn sàng
```

Mục tiêu nghiệm thu: `/health/ready` trả 200 trong vòng < 5 phút kể từ `up`.

Swagger UI chỉ bật khi `ASPNETCORE_ENVIRONMENT=Development`. Compose mặc định là `Production` nên `/swagger`
trả 404; muốn xem thì đặt `ASPNETCORE_ENVIRONMENT=Development` trong `deploy/.env` rồi `up -d` lại.

Dừng: `docker compose -f deploy/docker-compose.yml down` (thêm `-v` để xóa cả dữ liệu).

### SuperAdmin ban đầu

Migrator seed 4 Role cố định (SuperAdmin, ContentEditor, SalesStaff, Viewer), grant theo ma trận quyền và
một SuperAdmin đầu tiên. Tài khoản này lấy từ biến môi trường, **không có mặc định trong mã nguồn**:

| Biến                       | Ý nghĩa                                          |
| -------------------------- | ------------------------------------------------ |
| `SEED_SUPERADMIN_EMAIL`    | Email (cũng là UserName) của SuperAdmin          |
| `SEED_SUPERADMIN_PASSWORD` | 12–128 ký tự, có hoa, thường, số, ký tự đặc biệt |

Đặt hai biến trong `deploy/.env` (xem `deploy/.env.example`) **trước** khi `up -d --build`. Thiếu hoặc sai thì migrator
dừng với exit code khác 0, thông báo chỉ nêu tên biến (không in giá trị) và không ghi gì vào DB. Nếu email đã tồn tại,
tài khoản và mật khẩu được giữ nguyên.

### Dùng DB riêng ở local (đổi tên DB, mật khẩu)

`deploy/.env` (đã gitignore) quyết định tên DB, user, mật khẩu của container Postgres, ví dụ `POSTGRES_DB=car_dealership_local`.
Image Postgres chỉ tạo DB ở lần khởi tạo đầu của volume, nên đổi `POSTGRES_DB` khi volume đã có thì phải `down -v` rồi `up -d postgres` lại.
Thêm một DB khác trong cùng container: `docker exec -it cdp-postgres psql -U <user> -d postgres -c "CREATE DATABASE <ten>;"`, rồi chạy migrator với
chuỗi kết nối trỏ vào DB đó. Cổng Postgres chỉ mở trên `127.0.0.1`.

Host và migrator đọc chuỗi kết nối từ biến môi trường, không sửa `appsettings.json`:

```powershell
$env:ConnectionStrings__Default = 'Host=localhost;Port=5432;Database=<ten>;User ID=<user>;Password=<mat khau>;'
$env:SEED_SUPERADMIN_EMAIL = 'superadmin@example.com'
$env:SEED_SUPERADMIN_PASSWORD = '<12-128 ky tu, du hoa/thuong/so/ky tu dac biet>'
$env:EDGE_KEY = '<chuoi ngau nhien >= 32 ky tu, vi du: openssl rand -hex 32>'   # chi host can, migrator khong can
$env:SERVICE_JWT_PUBLIC_KEY = '<khoa CONG KHAI ES256 P-256: PEM mot dong voi \n, hoac base64 SubjectPublicKeyInfo>'
$env:STRING_ENCRYPTION_PASSPHRASE = '<>= 16 ky tu ngau nhien, vi du: openssl rand -hex 16>'   # chi host can
dotnet run --project src/CarDealership.CoreService.DbMigrator
dotnet run --project src/CarDealership.CoreService.HttpApi.Host --no-launch-profile   # ASPNETCORE_URLS=http://127.0.0.1:5099
```

### Endpoint xác thực

Chỉ BFF gọi Core_API trong hệ thống thật. Mọi request (trừ `GET /health/live`) phải kèm:

- `X-Edge-Key: <giá trị EDGE_KEY>` — thiếu hoặc sai thì nhận **404 rỗng** (giống route không tồn tại).
- `Authorization: Bearer <service JWT>` do BFF ký bằng khóa riêng ES256 — thiếu/sai/hết hạn thì 401, đúng JWT nhưng sai `scope` thì 403 (`scope=bff` cho endpoint thường, `scope=jobs` chỉ cho `/api/jobs/*`).

Vì vậy không curl thẳng được nếu không tự ký token; `/health/ready` cũng cần JWT.

Quy tắc service JWT: thuật toán **chỉ ES256**; `iss=bff`, `aud=core-api`; `exp − iat` ≤ 60 giây; bắt buộc `iat`, `exp`, `jti`; đúng một giá trị `scope`; lệch đồng hồ cho phép 5 giây. Khi thay mặt một Admin_User, token kèm `sub` (userId) và `sid` (mã phiên). `SERVICE_JWT_PUBLIC_KEY` là biến **bắt buộc** của host (PEM hoặc base64 SPKI, đường cong P-256): thiếu, sai định dạng, sai đường cong hoặc là khóa **riêng** thì process thoát mã 1 và chỉ log tên biến. Khóa riêng chỉ nằm ở BFF (`SERVICE_JWT_PRIVATE_KEY` trong `car-dealership-bff/.env`, đã gitignore).

Endpoint dưới `/api/jobs/*` tự động dùng policy `ServiceJobs` (convention); endpoint nào quên khai báo bảo mật rơi vào policy mặc định `ServiceBff` thay vì mở công khai; muốn mở công khai phải ghi `[AllowAnonymous]` có chủ đích.

`EDGE_KEY` và `STRING_ENCRYPTION_PASSPHRASE` là biến môi trường **bắt buộc** của host: thiếu (hoặc Edge key ngắn hơn 32 ký tự, passphrase ngắn hơn 16) thì process thoát mã 1 ngay khi khởi động và log tên biến (không in giá trị).

| Method | Route                            | Ý nghĩa                                                                                                |
| ------ | -------------------------------- | ------------------------------------------------------------------------------------------------------ |
| POST   | `/api/app/auth/sessions`         | Đăng nhập `{email, password}` → 200 `{refreshToken, sessionId, ...}`; mọi thất bại → 401 cùng một body |
| POST   | `/api/app/auth/sessions/refresh` | Đổi refresh token lấy token mới (xoay vòng); dùng lại token cũ → thu hồi cả phiên                      |
| POST   | `/api/app/auth/sessions/revoke`  | Đăng xuất: thu hồi phiên của refresh token, 204                                                        |
| GET    | `/api/app/auth/sessions/current` | Danh tính + quyền của phiên trong claim `sub`/`sid` của service JWT                                     |

Sai 5 lần liên tiếp trong 15 phút thì email đó bị khóa 15 phút (kể cả khi đúng mật khẩu). Phiên hết hạn sau 3 ngày không dùng, tối đa 7 ngày kể từ lúc đăng nhập, hoặc khi user bị Inactive.

### Chạy test

```powershell
dotnet test CarDealership.CoreService.slnx -c Release
```

- Test EF Core dùng Testcontainers nên **cần Docker đang chạy** (đỏ nếu Docker tắt, không tự bỏ qua).
- Test Host cần PostgreSQL ở `localhost:5432` đã migrate. Nếu DB của bạn khác mặc định trong `appsettings.json` (`cdp`), đặt
  `ConnectionStrings__Default` trỏ đúng DB trước khi chạy, nếu không sẽ lỗi `28P01 password authentication failed`.
- Cổng 5432 đóng (không có Postgres) thì các test cần DB **tự bỏ qua và vẫn hiện xanh**. Muốn chắc chúng chạy thật, bật Postgres và migrate trước.

## Kiểm tra package ABP (giấy phép)

Chỉ dùng ABP open-source. Script chặn package thương mại (`.Pro.`, `Commercial`, `Volo.Saas`...) và
mọi `Volo.*` ngoài `deploy/scripts/abp-allowlist.txt` (CI chạy ở job `check-abp-packages`).

```bash
dotnet restore CarDealership.CoreService.slnx
bash deploy/scripts/check-abp-packages.sh            # quét solution thật
bash deploy/scripts/test-check-abp-packages.sh       # tự kiểm script bằng fixture
```

Khi thêm package `Volo.*` hợp lệ: xác nhận nó thuộc repo `abpframework/abp`, rồi thêm một dòng vào allowlist.

## Tài liệu

Thiết kế, ERD và ADR được quản lý riêng; source code là nguồn sự thật cuối cùng.
