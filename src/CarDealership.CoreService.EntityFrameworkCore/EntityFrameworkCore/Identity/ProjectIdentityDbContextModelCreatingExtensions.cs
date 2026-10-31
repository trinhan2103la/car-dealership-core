using CarDealership.CoreService.Identity.Sessions;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Ngày 16 (ADR-003): cấu hình EF Core cho module Identity CỦA DỰ ÁN (AdminSession,
/// LoginAttempt, Showroom_Scope...). Bảng tiền tố "idn_", tên bảng/cột snake_case ghi tường minh.
/// Tên lớp có tiền tố "Project" để không nhầm với ConfigureIdentity() của ABP.
/// </summary>
public static class ProjectIdentityDbContextModelCreatingExtensions
{
    public const string TablePrefix = "idn_";

    public static void ConfigureProjectIdentity(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        // Ngày 30 (ADR-005): chỉ lưu SHA-256 của session id.
        builder.Entity<AdminSession>(b =>
        {
            b.ToTable(TablePrefix + "admin_sessions", t =>
            {
                // Chỉ chấp nhận đúng 32 byte (SHA-256): chặn ghi nhầm giá trị gốc hay hash sai ở tầng DB.
                t.HasCheckConstraint("ck_idn_admin_sessions_token_hash_len", "octet_length(token_hash) = 32");
            });

            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.TokenHash).HasColumnName("token_hash").HasColumnType("bytea").IsRequired();
            b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            // Template bật Npgsql.EnableLegacyTimestampBehavior nên timestamptz đọc về có Kind=Local.
            // Ép về UTC khi đọc để mọi thời điểm của entity luôn là UTC (không đổi schema).
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired()
                .HasConversion(v => v, v => v.ToUniversalTime());
            b.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("timestamp with time zone").IsRequired()
                .HasConversion(v => v, v => v.ToUniversalTime());
            b.Property(x => x.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone")
                .HasConversion(v => v, v => v.HasValue ? v.Value.ToUniversalTime() : v);
            b.Ignore(x => x.IsRevoked);

            // Tra session mỗi request theo hash.
            b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_idn_admin_sessions_token_hash");
            // Thu hồi mọi session còn hiệu lực của một user (vô hiệu hóa tài khoản).
            b.HasIndex(x => x.UserId).HasDatabaseName("ix_idn_admin_sessions_user_active")
                .HasFilter("revoked_at IS NULL");
        });
    }
}
