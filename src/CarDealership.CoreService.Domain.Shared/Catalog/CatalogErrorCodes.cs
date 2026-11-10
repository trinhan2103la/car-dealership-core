namespace CarDealership.CoreService.Catalog;

/// <summary>
/// Mã lỗi nghiệp vụ của Catalog (BusinessException.Code). Ánh xạ HTTP ở <c>CoreServiceHttpApiModule</c>:
/// trùng → 409, còn lại → 422. Lỗi validation theo trường là 400 (AbpValidationException), không tồn tại là 404,
/// lệch ConcurrencyStamp là 409 (AbpDbConcurrencyException).
/// </summary>
public static class CatalogErrorCodes
{
    /// <summary>409. Data["field"] = "name" hoặc "slug".</summary>
    public const string Duplicate = "Catalog:Duplicate";

    /// <summary>422. Data: currentStatus, allowedTargets.</summary>
    public const string InvalidTransition = "Catalog:InvalidTransition";

    /// <summary>422. Data: violations (danh sách mã quy tắc chưa thỏa, xem <see cref="PublishRuleCodes"/>).</summary>
    public const string PublishValidationFailed = "Catalog:PublishValidationFailed";

    /// <summary>422. Hạ CarVariant Published cuối cùng của CarModel Published.</summary>
    public const string LastPublishedVariant = "Catalog:LastPublishedVariant";

    /// <summary>422. Data: carModels (tên và slug các CarModel Published cần ẩn trước).</summary>
    public const string BrandHasPublishedCarModels = "Catalog:BrandHasPublishedCarModels";

    /// <summary>422. CarModel đã Archived: không nhận ảnh mới.</summary>
    public const string CarModelArchived = "Catalog:CarModelArchived";

    /// <summary>422. Đã đủ 30 ảnh.</summary>
    public const string ImageLimitReached = "Catalog:ImageLimitReached";
}

/// <summary>Mã quy tắc Publish_Validation (Req 10.3, 10.4). Phản hồi 422 trả về danh sách các mã chưa thỏa.</summary>
public static class PublishRuleCodes
{
    // CarModel
    public const string BrandActive = "BrandActive";
    public const string DescriptionLength = "DescriptionLength";
    public const string SingleCoverImage = "SingleCoverImage";
    public const string ImageAlt = "ImageAlt";
    public const string HasPublishedVariant = "HasPublishedVariant";

    // CarVariant
    public const string KeySpecComplete = "KeySpecComplete";
    public const string PriceValid = "PriceValid";
}
