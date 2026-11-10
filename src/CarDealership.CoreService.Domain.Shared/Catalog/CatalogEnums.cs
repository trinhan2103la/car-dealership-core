using System.Text.Json.Serialization;

namespace CarDealership.CoreService.Catalog;

/// <summary>Trạng thái Brand (Req 7). Lưu dạng text trong cột <c>status</c>; JSON là chuỗi (<c>"Active"</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BrandStatus
{
    Active = 0,
    Inactive = 1,
}

/// <summary>
/// Trạng thái CarModel và CarVariant (Req 10). Lưu dạng text; JSON là chuỗi. Chỉ 5 chuyển được phép,
/// xem <c>CatalogStatusMachine</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CatalogStatus
{
    Draft = 0,
    Published = 1,
    Hidden = 2,
    Archived = 3,
}
