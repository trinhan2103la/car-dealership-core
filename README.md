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

## Chạy local (Ngày 3: mới có PostgreSQL)

Yêu cầu: Docker Desktop đang chạy, .NET 10 SDK, psql.

```powershell
# 1) Chuẩn bị biến môi trường
Copy-Item deploy/.env.example deploy/.env   # rồi chỉnh nếu cần

# 2) Khởi động PostgreSQL 18
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d postgres

# 3) Kiểm tra
docker compose -f deploy/docker-compose.yml ps
psql -h localhost -p 5432 -U cdp -d cdp -c "select version();"
```

Dừng: `docker compose -f deploy/docker-compose.yml down` (thêm `-v` để xóa cả dữ liệu).

## Tài liệu

Thiết kế, ERD, ADR, roadmap, kế hoạch theo ngày: xem spec `car-dealership-platform` trong Kiro (`.kiro/specs` và `.kiro/docs`). Source code là nguồn sự thật cuối cùng; khi code lệch tài liệu thì cập nhật tài liệu.
