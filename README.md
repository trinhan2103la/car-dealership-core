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

### Chỉ PostgreSQL (Ngày 3)

```powershell
# 1) Chuẩn bị biến môi trường
Copy-Item deploy/.env.example deploy/.env   # rồi chỉnh nếu cần

# 2) Khởi động PostgreSQL 18
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d postgres

# 3) Kiểm tra
docker compose -f deploy/docker-compose.yml ps
psql -h localhost -p 5432 -U cdp -d cdp -c "select version();"
```

### Toàn bộ Core_API qua Docker (Ngày 15)

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

Thiết kế, ERD, ADR, roadmap, kế hoạch theo ngày: xem spec `car-dealership-platform` trong Kiro (`.kiro/specs` và `.kiro/docs`). Source code là nguồn sự thật cuối cùng; khi code lệch tài liệu thì cập nhật tài liệu.
