namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>
/// 6 Key_Spec của CarVariant (Req 8.3, 10.4): kiểu thân xe, nhiên liệu, hộp số, số chỗ, động cơ, công suất.
/// Mỗi Key_Spec là một CỘT riêng của <c>cat_car_variants</c> (để lọc/sắp xếp bằng index), không phải JSON.
/// Động cơ và công suất tùy chọn khi tạo nhưng bắt buộc khi xuất bản.
/// </summary>
public sealed record KeySpec(
    string BodyType,
    string Fuel,
    string Transmission,
    int Seats,
    string? Engine,
    string? Power)
{
    /// <summary>Đủ cả 6 Key_Spec (Publish_Validation của CarVariant).</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(BodyType)
        && !string.IsNullOrWhiteSpace(Fuel)
        && !string.IsNullOrWhiteSpace(Transmission)
        && Seats is >= CatalogConsts.SeatsMin and <= CatalogConsts.SeatsMax
        && !string.IsNullOrWhiteSpace(Engine)
        && !string.IsNullOrWhiteSpace(Power);
}
