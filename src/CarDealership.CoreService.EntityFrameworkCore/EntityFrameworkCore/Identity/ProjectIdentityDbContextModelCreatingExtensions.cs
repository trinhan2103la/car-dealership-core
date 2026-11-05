using CarDealership.CoreService.Identity.Login;
using CarDealership.CoreService.Identity.Sessions;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Cấu hình EF Core cho module Identity CỦA DỰ ÁN (AdminSession,
/// LoginAttempt, Showroom_Scope...). Bảng tiền tố "idn_", tên bảng/cột snake_case ghi tường minh.
/// Tên lớp có tiền tố "Project" để không nhầm với ConfigureIdentity() của ABP.
/// </summary>
public static class ProjectIdentityDbContextModelCreatingExtensions
{
    public const string TablePrefix = "idn_";

    public static void ConfigureProjectIdentity(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        // Chỉ lưu SHA-256 của session id.
        builder.Entity<AdminSession>(b =>
        {
            b.ToTable(TablePrefix + "admin_sessions", t =>
            {
                // Chỉ chấp nhận đúng 32 byte (SHA-256): chặn ghi nhầm giá trị gốc hay hash sai ở tầng DB.
                t.HasCheckConstraint("ck_idn_admin_sessions_token_hash_len", "octet_length(token_hash) = 32");
                t.HasCheckConstraint("ck_idn_admin_sessions_previous_token_hash_len",
                    "previous_token_hash IS NULL OR octet_length(previous_token_hash) = 32");
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
            // Refresh token rotation. Token vừa bị thay + thời điểm thay → nhận ra grace 20 s / dùng lại.
            b.Property(x => x.PreviousTokenHash).HasColumnName("previous_token_hash").HasColumnType("bytea");
            b.Property(x => x.RotatedAt).HasColumnName("rotated_at").HasColumnType("timestamp with time zone")
                .HasConversion(v => v, v => v.HasValue ? v.Value.ToUniversalTime() : v);
            b.Property(x => x.RevokeReason).HasColumnName("revoke_reason").HasMaxLength(AdminSession.MaxRevokeReasonLength);
            b.Ignore(x => x.IsRevoked);
            b.Ignore(x => x.AbsoluteExpiresAt);

            // Tra family theo refresh token hiện hành (mỗi lần refresh).
            b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_idn_admin_sessions_token_hash");
            // Tra theo token vừa bị thay (grace / phát hiện dùng lại).
            b.HasIndex(x => x.PreviousTokenHash).HasDatabaseName("ix_idn_admin_sessions_previous_token_hash")
                .HasFilter("previous_token_hash IS NOT NULL");
            // Thu hồi mọi session còn hiệu lực của một user (vô hiệu hóa tài khoản).
            b.HasIndex(x => x.UserId).HasDatabaseName("ix_idn_admin_sessions_user_active")
                .HasFilter("revoked_at IS NULL");
        });

        // Lịch sử đăng nhập để LoginAttemptPolicy phát lại. user_id không FK (khác module,
        // và null khi email không tồn tại).
        builder.Entity<LoginAttempt>(b =>
        {
            b.ToTable(TablePrefix + "login_attempts");

            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasColumnType("text").IsRequired();
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.Succeeded).HasColumnName("succeeded").IsRequired();
            // Cùng lý do với AdminSession: switch legacy của Npgsql trả Kind=Local, ép về UTC khi đọc.
            b.Property(x => x.AttemptedAt).HasColumnName("attempted_at").HasColumnType("timestamp with time zone").IsRequired()
                .HasConversion(v => v, v => v.ToUniversalTime());

            b.HasIndex(x => new { x.NormalizedEmail, x.AttemptedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_idn_login_attempts_email_attempted_at");
        });
    }
}
