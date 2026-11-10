using System;
using System.Collections.Generic;
using Volo.Abp.Domain.Entities;

namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>Dữ liệu CarVariant như client gửi (chưa chuẩn hóa/kiểm). Giá là decimal để bắt được số lẻ ở mức trường.</summary>
public sealed record CarVariantData(
    string? Name,
    string? BodyType,
    string? Fuel,
    string? Transmission,
    int? Seats,
    string? Engine,
    string? Power,
    decimal? ReferencePrice,
    bool ContactForPrice,
    IReadOnlyList<ExtraSpecInput>? ExtraSpecs);

/// <summary>
/// Entity con của aggregate <see cref="CarModel"/> (bảng <c>cat_car_variants</c>, Req 8). KHÔNG có repository riêng:
/// mọi thay đổi đi qua <see cref="CarModel"/> để giữ bất biến (Publish_Validation, giá min/max, search_text).
/// Có <c>ConcurrencyStamp</c> riêng vì Admin sửa variant độc lập với model (Req 8.11).
/// </summary>
public class CarVariant : Entity<Guid>, IHasConcurrencyStamp
{
    public Guid CarModelId { get; private set; }

    /// <summary>Tên đã trim, 1–100, duy nhất trong CarModel (không phân biệt hoa thường).</summary>
    public string Name { get; private set; } = string.Empty;

    public CatalogStatus Status { get; private set; }

    /// <summary>VND (bigint 1..100 tỷ). Null khi <see cref="ContactForPrice"/>. Đúng một trong hai.</summary>
    public long? ReferencePrice { get; private set; }

    public bool ContactForPrice { get; private set; }

    public string BodyType { get; private set; } = string.Empty;
    public string Fuel { get; private set; } = string.Empty;
    public string Transmission { get; private set; } = string.Empty;
    public int Seats { get; private set; }
    public string? Engine { get; private set; }
    public string? Power { get; private set; }

    /// <summary>Thay nguyên cả danh sách khi sửa (record bất biến), nên EF phát hiện thay đổi bằng so sánh theo giá trị.</summary>
    public IReadOnlyList<ExtraSpec> ExtraSpecs { get; private set; } = Array.Empty<ExtraSpec>();

    public bool IsDemo { get; private set; }

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    public KeySpec KeySpec => new(BodyType, Fuel, Transmission, Seats, Engine, Power);

    private CarVariant()
    {
        // EF Core
    }

    /// <summary>Chỉ <see cref="CarModel"/> được tạo variant (factory <c>AddVariant</c>).</summary>
    internal CarVariant(Guid id, Guid carModelId, NormalizedData data, bool isDemo) : base(id)
    {
        CarModelId = carModelId;
        Status = CatalogStatus.Draft;
        IsDemo = isDemo;
        Apply(data);
    }

    /// <summary>Gán dữ liệu ĐÃ kiểm hợp lệ (xem <see cref="Normalize"/>).</summary>
    internal void Apply(NormalizedData n)
    {
        Name = n.Name;
        BodyType = n.BodyType;
        Fuel = n.Fuel;
        Transmission = n.Transmission;
        Seats = n.Seats;
        Engine = n.Engine;
        Power = n.Power;
        ReferencePrice = n.ReferencePrice;
        ContactForPrice = n.ContactForPrice;
        ExtraSpecs = n.ExtraSpecs;
    }

    internal void SetStatus(CatalogStatus status) => Status = status;

    /// <summary>Kết quả chuẩn hóa một lần kiểm hợp lệ.</summary>
    internal sealed record NormalizedData(
        string Name, string BodyType, string Fuel, string Transmission, int Seats,
        string? Engine, string? Power, long? ReferencePrice, bool ContactForPrice,
        IReadOnlyList<ExtraSpec> ExtraSpecs);

    /// <summary>
    /// Kiểm toàn bộ quy tắc Req 8.3–8.8 mà không ném: gom lỗi theo trường vào <paramref name="errors"/>.
    /// Tính duy nhất tên trong CarModel do <see cref="CarModel"/> kiểm (cần các variant khác).
    /// </summary>
    internal static NormalizedData Normalize(CarVariantData data, FieldErrors errors)
    {
        var name = data.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors.Add("name", "Tên không được rỗng.");
        }
        else if (name.Length > CatalogConsts.NameMaxLength)
        {
            errors.Add("name", $"Tên dài tối đa {CatalogConsts.NameMaxLength} ký tự.");
        }

        var bodyType = RequiredText(errors, "bodyType", data.BodyType, "Kiểu thân xe");
        var fuel = RequiredText(errors, "fuel", data.Fuel, "Nhiên liệu");
        var transmission = RequiredText(errors, "transmission", data.Transmission, "Hộp số");
        var engine = OptionalText(errors, "engine", data.Engine, "Động cơ");
        var power = OptionalText(errors, "power", data.Power, "Công suất");

        var seats = 0;
        if (data.Seats is null)
        {
            errors.Add("seats", "Số chỗ ngồi là bắt buộc.");
        }
        else if (data.Seats is < CatalogConsts.SeatsMin or > CatalogConsts.SeatsMax)
        {
            errors.Add("seats", $"Số chỗ ngồi phải từ {CatalogConsts.SeatsMin} đến {CatalogConsts.SeatsMax}.");
        }
        else
        {
            seats = data.Seats.Value;
        }

        var referencePrice = ValidatePrice(errors, data.ReferencePrice, data.ContactForPrice);
        var extraSpecs = ExtraSpec.Normalize(data.ExtraSpecs, errors);

        return new NormalizedData(
            name, bodyType, fuel, transmission, seats, engine, power,
            referencePrice, data.ContactForPrice, extraSpecs);
    }

    /// <summary>Đúng một trong Reference_Price hoặc cờ giá liên hệ (Req 8.4–8.6). Trả giá đã chuyển sang long.</summary>
    private static long? ValidatePrice(FieldErrors errors, decimal? price, bool contactForPrice)
    {
        if (price is null && !contactForPrice)
        {
            errors.Add("referencePrice", "Cần nhập giá tham khảo hoặc chọn giá liên hệ.");
            return null;
        }

        if (price is not null && contactForPrice)
        {
            errors.Add("referencePrice", "Không được vừa có giá tham khảo vừa chọn giá liên hệ.");
            return null;
        }

        if (price is null)
        {
            return null;
        }

        if (price.Value != decimal.Truncate(price.Value))
        {
            errors.Add("referencePrice", "Giá tham khảo phải là số nguyên VND.");
            return null;
        }

        if (price.Value < CatalogConsts.PriceMin || price.Value > CatalogConsts.PriceMax)
        {
            errors.Add("referencePrice", $"Giá tham khảo phải từ {CatalogConsts.PriceMin} đến {CatalogConsts.PriceMax} VND.");
            return null;
        }

        return (long)price.Value;
    }

    private static string RequiredText(FieldErrors errors, string member, string? value, string label)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            errors.Add(member, $"{label} là bắt buộc.");
        }
        else if (text.Length > CatalogConsts.KeySpecTextMaxLength)
        {
            errors.Add(member, $"{label} dài tối đa {CatalogConsts.KeySpecTextMaxLength} ký tự.");
        }

        return text;
    }

    private static string? OptionalText(FieldErrors errors, string member, string? value, string label)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > CatalogConsts.KeySpecTextMaxLength)
        {
            errors.Add(member, $"{label} dài tối đa {CatalogConsts.KeySpecTextMaxLength} ký tự.");
        }

        return text;
    }
}
