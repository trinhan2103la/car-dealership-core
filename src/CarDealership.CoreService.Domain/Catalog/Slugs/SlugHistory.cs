using System;
using Volo.Abp.Domain.Entities;

namespace CarDealership.CoreService.Catalog.Slugs;

/// <summary>
/// Bảng <c>cat_slug_history</c>: Slug cũ để Public_Website chuyển hướng 301 một bước (Req 3.9, 8.13).
/// <see cref="ScopeId"/> = <c>brand_id</c> với CarModel và <see cref="CatalogConsts.BrandSlugScope"/> (UUID zero) với Brand.
/// Dùng UUID zero thay vì NULL vì <c>NULL &lt;&gt; NULL</c> làm UNIQUE <c>(entity_type, scope_id, old_slug)</c> không chặn trùng.
/// Không có FK (tra cứu theo Slug, không join ngược).
/// </summary>
public class SlugHistory : Entity<Guid>
{
    public string EntityType { get; private set; } = string.Empty;

    public Guid EntityId { get; private set; }

    public Guid ScopeId { get; private set; }

    public string OldSlug { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    private SlugHistory()
    {
        // EF Core
    }

    public SlugHistory(Guid id, string entityType, Guid entityId, Guid scopeId, string oldSlug, DateTime createdAtUtc)
        : base(id)
    {
        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Thời điểm phải là UTC.", nameof(createdAtUtc));
        }

        EntityType = entityType;
        EntityId = entityId;
        ScopeId = scopeId;
        OldSlug = oldSlug;
        CreatedAt = new DateTime(createdAtUtc.Ticks - createdAtUtc.Ticks % 10, DateTimeKind.Utc);
    }
}
