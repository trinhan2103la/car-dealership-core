using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Analytics;

/// <summary>
/// Ngày 16 (ADR-003): cấu hình EF Core cho module Analytics. Hiện rỗng; bảng tiền tố "ana_".
/// </summary>
public static class AnalyticsDbContextModelCreatingExtensions
{
    public const string TablePrefix = "ana_";

    public static void ConfigureAnalytics(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
    }
}
