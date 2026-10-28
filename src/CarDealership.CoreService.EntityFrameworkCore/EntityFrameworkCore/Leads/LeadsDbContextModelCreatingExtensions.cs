using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Leads;

/// <summary>
/// Ngày 16 (ADR-003): cấu hình EF Core cho module Leads. Hiện rỗng; bảng tiền tố "led_".
/// </summary>
public static class LeadsDbContextModelCreatingExtensions
{
    public const string TablePrefix = "led_";

    public static void ConfigureLeads(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
    }
}
