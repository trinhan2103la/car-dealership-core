#!/bin/sh
# Tạo role DB RIÊNG cho ứng dụng (core-api), tách khỏi role migrator ($POSTGRES_USER).
# Vì sao: owner của bảng luôn có mọi quyền, nên nếu app dùng chung role với migrator thì
# REVOKE UPDATE, DELETE trên aud_entries vô tác dụng. Migration AddAuditEntries thu quyền
# trên chính role này (biến DB_APP_ROLE của service migrator).
#
# Postgres chỉ chạy script trong /docker-entrypoint-initdb.d khi volume còn TRỐNG.
# Volume đã có dữ liệu: chạy tay bằng
#   docker exec -i cdp-postgres sh < deploy/postgres/init-app-role.sh
# hoặc `docker compose down -v` (xóa dữ liệu dev).
set -eu

: "${DB_APP_USER:?DB_APP_USER chua dat}"
: "${DB_APP_PASSWORD:?DB_APP_PASSWORD chua dat}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    -v app_role="$DB_APP_USER" -v app_pw="$DB_APP_PASSWORD" <<'SQL'
-- Tạo role nếu chưa có (chạy lại không lỗi). \gexec chạy câu lệnh do SELECT sinh ra.
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'app_role', :'app_pw')
 WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_role') \gexec

SELECT format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), :'app_role') \gexec

GRANT USAGE ON SCHEMA public TO :"app_role";

-- Bảng/sequence đã có và bảng/sequence do role migrator tạo SAU NÀY (mọi migration).
-- Không cấp TRUNCATE, REFERENCES, TRIGGER. Migration AddAuditEntries còn REVOKE ALL rồi chỉ cấp SELECT, INSERT cho aud_entries.
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"app_role";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"app_role";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_role";
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO :"app_role";
SQL
