using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Showrooms;

/// <summary>
/// Ngày 16 (ADR-003): cấu hình EF Core cho module Showrooms. Hiện rỗng; bảng tiền tố "shr_".
/// </summary>
public static class ShowroomsDbContextModelCreatingExtensions
{
    public const string TablePrefix = "shr_";

    public static void ConfigureShowrooms(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
    }
}
