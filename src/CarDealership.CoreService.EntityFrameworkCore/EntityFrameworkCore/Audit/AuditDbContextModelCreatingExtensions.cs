using CarDealership.CoreService.Audit;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Audit;

/// <summary>
/// Cấu hình EF Core cho module Audit CỦA DỰ ÁN (aud_entries, append-only).
/// Bảng tiền tố "aud_", tên bảng/cột snake_case ghi tường minh. Tên method có tiền tố "Project"
/// để không nhầm với ConfigureAuditLogging() của ABP.
/// Quyền DB (REVOKE UPDATE, DELETE cho role ứng dụng) nằm trong migration AddAuditEntries.
/// </summary>
public static class AuditDbContextModelCreatingExtensions
{
    public const string TablePrefix = "aud_";

    public static void ConfigureProjectAudit(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<AuditEntry>(b =>
        {
            b.ToTable(TablePrefix + "entries");

            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            // Template bật Npgsql.EnableLegacyTimestampBehavior nên timestamptz đọc về có Kind=Local.
            // Ép về UTC khi đọc (không đổi schema), giống AdminSession.
            b.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone").IsRequired()
                .HasConversion(v => v, v => v.ToUniversalTime());
            // Không FK: ActorId thuộc module Identity (và null với security event / tác vụ hệ thống).
            b.Property(x => x.ActorId).HasColumnName("actor_id");
            b.Property(x => x.Action).HasColumnName("action").HasColumnType("text").HasMaxLength(AuditEntry.MaxActionLength).IsRequired();
            b.Property(x => x.EntityType).HasColumnName("entity_type").HasColumnType("text").HasMaxLength(AuditEntry.MaxEntityTypeLength).IsRequired();
            b.Property(x => x.EntityId).HasColumnName("entity_id").HasColumnType("text").HasMaxLength(AuditEntry.MaxEntityIdLength).IsRequired();
            b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasColumnType("text").HasMaxLength(AuditEntry.MaxCorrelationIdLength).IsRequired();
            b.Property(x => x.Changes).HasColumnName("changes").HasColumnType("jsonb").IsRequired();

            // Danh sách mới nhất, lọc theo thời gian.
            b.HasIndex(x => x.OccurredAt).IsDescending(true).HasDatabaseName("ix_aud_entries_occurred_at");
            // Lịch sử của một đối tượng.
            b.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("ix_aud_entries_entity");
            // Hoạt động của một người, mới nhất trước.
            b.HasIndex(x => new { x.ActorId, x.OccurredAt }).IsDescending(false, true).HasDatabaseName("ix_aud_entries_actor_occurred_at");
        });
    }
}
