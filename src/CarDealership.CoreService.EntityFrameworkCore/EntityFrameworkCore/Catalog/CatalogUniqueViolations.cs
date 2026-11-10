using System.Collections.Generic;
using CarDealership.CoreService.Catalog;
using Npgsql;
using Volo.Abp;

namespace CarDealership.CoreService.EntityFrameworkCore.Catalog;

/// <summary>
/// DB là nơi quyết định cuối về tên/Slug trùng, kể cả khi hai request đến đồng thời (Req 7.3, 7.4): kiểm bằng
/// <c>AnyAsync</c> trước khi insert luôn có race. Vi phạm UNIQUE (SQLSTATE 23505) của các ràng buộc Catalog được dịch
/// sang <see cref="BusinessException"/> mã <see cref="CatalogErrorCodes.Duplicate"/> kèm tên trường (HTTP 409).
/// </summary>
public static class CatalogUniqueViolations
{
    public const string UniqueViolationSqlState = "23505";

    private static readonly IReadOnlyDictionary<string, string> FieldByConstraint = new Dictionary<string, string>
    {
        [CatalogConstraints.BrandName] = "name",
        [CatalogConstraints.BrandSlug] = "slug",
        [CatalogConstraints.CarModelBrandName] = "name",
        [CatalogConstraints.CarModelBrandSlug] = "slug",
        [CatalogConstraints.CarVariantModelName] = "name",
        [CatalogConstraints.SlugHistoryOldSlug] = "slug",
    };

    /// <summary>Trả lỗi 409 nếu <paramref name="exception"/> là vi phạm UNIQUE của một ràng buộc Catalog đã biết, ngược lại null.</summary>
    public static BusinessException? TryTranslate(PostgresException exception)
    {
        if (exception.SqlState != UniqueViolationSqlState
            || exception.ConstraintName is null
            || !FieldByConstraint.TryGetValue(exception.ConstraintName, out var field))
        {
            return null;
        }

        return CatalogExceptions.Duplicate(field);
    }
}
