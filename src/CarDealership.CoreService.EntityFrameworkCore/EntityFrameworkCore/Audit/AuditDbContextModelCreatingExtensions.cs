using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Audit;

/// <summary>
/// Ngày 16 (ADR-003): cấu hình EF Core cho module Audit CỦA DỰ ÁN (security event).
/// Hiện rỗng; bảng tiền tố "aud_". Tên method có tiền tố "Project" để không nhầm với
/// ConfigureAuditLogging() của ABP.
/// </summary>
public static class AuditDbContextModelCreatingExtensions
{
    public const string TablePrefix = "aud_";

    public static void ConfigureProjectAudit(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
    }
}
