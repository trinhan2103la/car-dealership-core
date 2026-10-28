using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Ngày 16 (ADR-003): cấu hình EF Core cho module Identity CỦA DỰ ÁN (AdminSession,
/// LoginAttempt, Showroom_Scope...). Hiện rỗng; bảng tiền tố "idn_".
/// Tên lớp có tiền tố "Project" để không nhầm với ConfigureIdentity() của ABP.
/// </summary>
public static class ProjectIdentityDbContextModelCreatingExtensions
{
    public const string TablePrefix = "idn_";

    public static void ConfigureProjectIdentity(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
    }
}
