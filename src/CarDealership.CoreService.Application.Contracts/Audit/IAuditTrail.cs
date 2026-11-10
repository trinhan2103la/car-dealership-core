using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CarDealership.CoreService.Audit;

/// <summary>Loại hành động ghi trong <c>aud_entries.action</c> (Req 17.1).</summary>
public static class AuditActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string ChangeStatus = "ChangeStatus";
    public const string Delete = "Delete";
}

/// <summary>
/// Ghi Audit_Log (module Audit, bảng <c>aud_entries</c> append-only).
/// Người thực hiện lấy từ <c>ICurrentUser</c>, Correlation_Id từ <c>ICorrelationIdProvider</c>, thời điểm từ <c>IClock</c>;
/// người gọi chỉ truyền hành động, thực thể và hai ảnh chụp trước/sau.
/// </summary>
public interface IAuditTrail
{
    /// <summary>
    /// Ghi MỘT Audit_Log cho thay đổi nghiệp vụ, trong CÙNG Unit of Work hiện tại: thay đổi nghiệp vụ và audit
    /// cùng commit hoặc cùng rollback (Req 17.3, 17.4). Không bao giờ dùng <c>requiresNew</c> ở đây.
    /// <paramref name="before"/> = null là tạo mới; <paramref name="after"/> = null là xóa.
    /// Trường nhạy cảm chỉ được ghi tên.
    /// </summary>
    /// <returns>false nếu hai ảnh chụp không khác nhau (không ghi gì, Req 17.2); ngược lại true.</returns>
    Task<bool> WriteAsync(
        string action,
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, object?>? before,
        IReadOnlyDictionary<string, object?>? after);

    /// <summary>
    /// Ghi sự kiện bảo mật (đăng nhập thành công/thất bại, khóa tài khoản...) trong UoW RIÊNG đã commit ngay,
    /// vì đăng nhập sai không có thay đổi nghiệp vụ nào để cùng rollback (Req 17.10). Không ghi mật khẩu.
    /// </summary>
    Task WriteSecurityEventAsync(string action, Guid? userId, string? reason);
}
