using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Settings;

/// <summary>
/// Ngày 16 (ADR-003): cấu hình EF Core cho module Settings CỦA DỰ ÁN (Website_Setting).
/// Hiện rỗng; bảng tiền tố "set_". Tên method có tiền tố "Project" để không nhầm với
/// ConfigureSettingManagement() của ABP.
/// </summary>
public static class SettingsDbContextModelCreatingExtensions
{
    public const string TablePrefix = "set_";

    public static void ConfigureProjectSettings(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
    }
}
