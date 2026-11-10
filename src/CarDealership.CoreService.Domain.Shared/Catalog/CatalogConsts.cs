namespace CarDealership.CoreService.Catalog;

/// <summary>Giới hạn của module Catalog (Req 7, 8, 9, 10). Một nguồn duy nhất cho validation, mapping và test.</summary>
public static class CatalogConsts
{
    public const int NameMaxLength = 100;
    public const int SlugMaxLength = 100;
    public const int LogoKeyMaxLength = 200;

    public const int DescriptionMaxLength = 5000;

    /// <summary>Mô tả tối thiểu khi xuất bản CarModel (Req 10.3b).</summary>
    public const int DescriptionPublishMinLength = 50;

    public const int DisplayOrderMin = 0;
    public const int DisplayOrderMax = 9999;

    /// <summary>Reference_Price: số nguyên VND trong (0, 100 tỷ].</summary>
    public const long PriceMin = 1;
    public const long PriceMax = 100_000_000_000;

    public const int SeatsMin = 2;
    public const int SeatsMax = 16;

    /// <summary>Độ dài tối đa của Key_Spec dạng text (kiểu thân xe, nhiên liệu, hộp số, động cơ, công suất).</summary>
    public const int KeySpecTextMaxLength = 100;

    public const int ExtraSpecMaxCount = 200;
    public const int ExtraSpecGroupMaxLength = 100;
    public const int ExtraSpecNameMaxLength = 100;
    public const int ExtraSpecValueMaxLength = 255;
    public const int ExtraSpecUnitMaxLength = 20;

    public const int ImageMaxCount = 30;
    public const int ImageAltMinLength = 5;
    public const int ImageAltMaxLength = 150;
    public const int StorageKeyMaxLength = 100;

    /// <summary>Từ khóa tìm kiếm công khai tối đa 100 ký tự (Req 2.8).</summary>
    public const int SearchKeywordMaxLength = 100;

    public const int PublicPageSizeDefault = 12;
    public const int PublicPageSizeMax = 48;

    /// <summary>Phạm vi (scope) của slug history cho Brand: Brand không thuộc Brand nào nên dùng UUID zero.</summary>
    public static readonly System.Guid BrandSlugScope = System.Guid.Empty;
}

public static class CatalogEntityTypes
{
    public const string Brand = "Brand";
    public const string CarModel = "CarModel";
    public const string CarVariant = "CarVariant";
    public const string VehicleImage = "VehicleImage";
}
