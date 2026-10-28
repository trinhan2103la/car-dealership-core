using Microsoft.EntityFrameworkCore;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Catalog;

/// <summary>
/// Ngày 16 (ADR-003): cấu hình EF Core cho module Catalog. Hiện rỗng (chưa có entity);
/// entity + mapping thêm ở P2. Bảng Catalog dùng tiền tố "cat_".
/// </summary>
public static class CatalogDbContextModelCreatingExtensions
{
    public const string TablePrefix = "cat_";

    public static void ConfigureCatalog(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        // Chưa có entity. Khi thêm: builder.Entity<X>(b => b.ToTable(TablePrefix + "x"));
    }
}
