# Quy ước đóng góp — car-dealership-core

Repo này nằm trong hệ 3 repo của dự án Car Dealership Platform:

- `car-dealership-web` — Public website (Next.js) + Admin portal (React)
- `car-dealership-bff` — NestJS BFF
- `car-dealership-core` — ABP / .NET 10 core API (repo này)

Clone cả ba repo ngang hàng trong cùng một thư mục cha (ví dụ `D:\WorkSpace\FRT\PROJECT_PERSONAL\`) để Docker Compose và quy trình local tham chiếu chéo được.

> Lưu ý: `package.json` trong repo này **chỉ** phục vụ git hook (husky + commitlint). Nó không liên quan tới build ABP/.NET. Phần backend dùng `dotnet`.

## Nhánh

Luôn tạo nhánh từ `main`, không commit thẳng vào `main`.

```
<type>/<mô-tả-kebab-case>
```

`type` ∈ `feat | fix | chore | docs | test | refactor | build | ci | perf | style`.
Ví dụ: `feat/brand-crud`, `fix/publish-validation`, `chore/strip-modules`.

## Commit message — Conventional Commits

```
type(scope): mô tả ngắn
```

`scope` gợi ý theo module: `catalog`, `showrooms`, `leads`, `identity`, `settings`, `audit`, `analytics`.

Ví dụ:

```
feat(catalog): them entity Brand va migration
fix(identity): sua chinh sach khoa dang nhap
chore(ci): them kiem tra allowlist package ABP
```

commitlint sẽ chặn commit sai quy ước (qua husky hook `commit-msg`).

## Pull request

- Mở PR vào `main`, điền đủ checklist trong template.
- Làm một mình vẫn nên qua PR để giữ thói quen và có branch protection bảo vệ.
