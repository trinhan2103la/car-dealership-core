using System;
using Volo.Abp.Domain.Entities;

namespace CarDealership.CoreService.Audit;

/// <summary>
/// Bảng <c>aud_entries</c>: MỘT dòng audit nghiệp vụ (Audit_Log), chỉ thêm, không sửa, không xóa.
/// Append-only được ép ở tầng DB (role ứng dụng không có UPDATE/DELETE), nên entity này cố ý
/// KHÔNG có setter công khai, không có ConcurrencyStamp và không dùng <c>ISoftDelete</c>.
/// Khác với <c>AbpAuditLogs</c> của ABP (log request kỹ thuật). <see cref="ActorId"/> không có FK
/// (khác module và có thể rỗng với security event/job). Mọi thời điểm là UTC.
/// </summary>
public class AuditEntry : Entity<Guid>
{
    public const int MaxActionLength = 100;
    public const int MaxEntityTypeLength = 100;
    public const int MaxEntityIdLength = 100;
    public const int MaxCorrelationIdLength = 128;

    /// <summary>Chuỗi JSON rỗng khi không có thay đổi chi tiết (ví dụ security event).</summary>
    public const string EmptyChanges = "[]";

    public DateTime OccurredAt { get; private set; }

    /// <summary>Người thực hiện; null với security event không gắn user hoặc tác vụ hệ thống.</summary>
    public Guid? ActorId { get; private set; }

    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>Mảng JSON <c>[{field, before, after}]</c>, lưu ở cột <c>jsonb</c>.</summary>
    public string Changes { get; private set; } = EmptyChanges;

    private AuditEntry()
    {
        // EF Core
    }

    public AuditEntry(
        Guid id,
        DateTime occurredAtUtc,
        Guid? actorId,
        string action,
        string entityType,
        string entityId,
        string correlationId,
        string? changesJson = null) : base(id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id không được rỗng.", nameof(id));
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Thời điểm phải là UTC.", nameof(occurredAtUtc));
        }

        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("ActorId không được là Guid rỗng (dùng null nếu không có người thực hiện).", nameof(actorId));
        }

        // PostgreSQL lưu timestamptz tới micro giây; làm tròn ngay để giá trị đọc lại khớp giá trị đã ghi.
        OccurredAt = new DateTime(occurredAtUtc.Ticks - occurredAtUtc.Ticks % 10, DateTimeKind.Utc);
        ActorId = actorId;
        Action = Required(action, nameof(action), MaxActionLength);
        EntityType = Required(entityType, nameof(entityType), MaxEntityTypeLength);
        EntityId = Required(entityId, nameof(entityId), MaxEntityIdLength);
        CorrelationId = Required(correlationId, nameof(correlationId), MaxCorrelationIdLength);
        Changes = string.IsNullOrWhiteSpace(changesJson) ? EmptyChanges : changesJson;
    }

    private static string Required(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Giá trị không được rỗng.", name);
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"Giá trị dài tối đa {maxLength} ký tự.", name);
        }

        return value;
    }
}
