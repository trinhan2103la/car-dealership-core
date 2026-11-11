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

    /// <summary>422. Data: currentState, allowedTargets.</summary>
    public const string InvalidTransition = "Catalog:InvalidTransition";

    /// <summary>
    /// 422. Data: rules (mỗi phần tử {code, passed = false, message}, mã xem <see cref="PublishRuleCodes"/>), invalidImageIds.
    /// </summary>
    public const string PublishValidationFailed = "Catalog:PublishValidationFailed";

    /// <summary>422. Hạ CarVariant Published cuối cùng của CarModel Published.</summary>
    public const string LastPublishedVariant = "Catalog:LastPublishedVariant";

    /// <summary>422. Data: items (tên và slug các CarModel Published cần ẩn trước).</summary>
    public const string BrandHasPublishedCarModels = "Catalog:BrandHasPublishedCarModels";

    /// <summary>422. CarModel đã Archived: không nhận ảnh mới.</summary>
    public const string CarModelArchived = "Catalog:CarModelArchived";

    /// <summary>422. Đã đủ 30 ảnh.</summary>
    public const string ImageLimitReached = "Catalog:ImageLimitReached";
    
    /// <summary>503. Object_Storage không khả dụng: không tạo bản ghi, client thử lại được.</summary>
    public const string StorageUnavailable = "Catalog:StorageUnavailable";
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

/// <summary>Câu tiếng Việt cho từng mã quy tắc (trường <c>message</c> trong <c>rules</c> của 422 PublishValidationFailed).</summary>
public static class PublishRuleMessages
{
    public static string For(string code) => code switch
    {
        PublishRuleCodes.BrandActive => "Brand phải đang Active",
        PublishRuleCodes.DescriptionLength => $"Mô tả cần từ {CatalogConsts.DescriptionPublishMinLength} đến {CatalogConsts.DescriptionMaxLength} ký tự",
        PublishRuleCodes.SingleCoverImage => "Cần đúng một ảnh cover",
        PublishRuleCodes.ImageAlt => $"Mọi ảnh cần alt từ {CatalogConsts.ImageAltMinLength} đến {CatalogConsts.ImageAltMaxLength} ký tự",
        PublishRuleCodes.HasPublishedVariant => "Cần ít nhất một phiên bản Published",
        PublishRuleCodes.KeySpecComplete => "Cần đủ 6 thông số chính",
        PublishRuleCodes.PriceValid => "Cần giá tham khảo hợp lệ hoặc chọn giá liên hệ",
        _ => "Chưa đạt điều kiện xuất bản",
    };
}
