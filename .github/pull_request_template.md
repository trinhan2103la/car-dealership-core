<!-- Tiêu đề PR theo Conventional Commits, ví dụ: feat(catalog): them aggregate CarModel -->

## Tóm tắt

<!-- PR này làm gì? Liên quan task/Ngày nào trong .kiro/specs/car-dealership-platform/tasks.md? -->

## Checklist tự review

- [ ] Các acceptance criteria liên quan đã được kiểm chứng (test xanh / smoke / checklist thủ công)
- [ ] Đã thêm hoặc cập nhật test cho thay đổi này
- [ ] Đã cập nhật tài liệu tương ứng trong `.kiro/docs` nếu có đổi API / schema / kiến trúc
- [ ] Không commit secret (API key, mật khẩu, connection string, khóa ký)
- [ ] Migration EF Core (nếu có) tương thích ngược với phiên bản Core_API đang chạy

## Cách kiểm thử

<!-- Lệnh chạy test / .http / psql / bước thao tác để người review xác minh -->
