namespace CarDealership.CoreService.Catalog;

/// <summary>Kiểm phân trang quản trị: trang từ 1, kích thước 1..100 (Req 7.8). Lỗi theo từng tham số, ném 400.</summary>
internal static class CatalogPaging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Skip, int Take) Validate(int page, int pageSize)
    {
        var errors = new FieldErrors();

        if (page < 1)
        {
            errors.Add("page", "Số trang bắt đầu từ 1.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Kích thước trang phải từ 1 đến {MaxPageSize}.");
        }

        errors.ThrowIfAny();
        return ((page - 1) * pageSize, pageSize);
    }
}
