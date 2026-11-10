using System.Collections.Generic;
using System.Linq;

namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>Ánh xạ tay entity ↔ DTO và ảnh chụp cho Audit_Log (ảnh chụp chỉ gồm trường nghiệp vụ, không metadata ABP).</summary>
internal static class CatalogMapping
{
    public static CarModelSummaryDto ToSummaryDto(CarModel m) => Fill(new CarModelSummaryDto(), m);

    public static CarModelDto ToDto(CarModel m)
    {
        var dto = Fill(new CarModelDto(), m);
        dto.Description = m.Description;
        dto.IsDemo = m.IsDemo;
        dto.CreationTime = m.CreationTime;
        dto.LastModificationTime = m.LastModificationTime;
        dto.Variants = m.Variants.OrderBy(v => v.Name).Select(ToDto).ToList();
        dto.Images = m.Images.OrderBy(i => i.DisplayOrder).Select(ToDto).ToList();
        return dto;
    }

    private static T Fill<T>(T dto, CarModel m) where T : CarModelSummaryDto
    {
        dto.Id = m.Id;
        dto.BrandId = m.BrandId;
        dto.Name = m.Name;
        dto.Slug = m.Slug;
        dto.Status = m.Status;
        dto.IsFeatured = m.IsFeatured;
        dto.DisplayOrder = m.DisplayOrder;
        dto.MinPublishedPrice = m.MinPublishedPrice;
        dto.MaxPublishedPrice = m.MaxPublishedPrice;
        dto.PublishedAt = m.PublishedAt;
        dto.ConcurrencyStamp = m.ConcurrencyStamp;
        return dto;
    }

    public static CarVariantDto ToDto(CarVariant v) => new()
    {
        Id = v.Id,
        CarModelId = v.CarModelId,
        Name = v.Name,
        Status = v.Status,
        ReferencePrice = v.ReferencePrice,
        ContactForPrice = v.ContactForPrice,
        BodyType = v.BodyType,
        Fuel = v.Fuel,
        Transmission = v.Transmission,
        Seats = v.Seats,
        Engine = v.Engine,
        Power = v.Power,
        ExtraSpecs = v.ExtraSpecs.Select(e => new ExtraSpecDto { Group = e.Group, Name = e.Name, Value = e.Value, Unit = e.Unit }).ToList(),
        IsDemo = v.IsDemo,
        ConcurrencyStamp = v.ConcurrencyStamp,
    };

    public static VehicleImageDto ToDto(VehicleImage i) => new()
    {
        Id = i.Id,
        CarVariantId = i.CarVariantId,
        StorageKey = i.StorageKey,
        Alt = i.Alt,
        DisplayOrder = i.DisplayOrder,
        IsCover = i.IsCover,
    };

    public static CarVariantData ToData(SaveCarVariantInput input) => new(
        input.Name,
        input.BodyType,
        input.Fuel,
        input.Transmission,
        input.Seats,
        input.Engine,
        input.Power,
        input.ReferencePrice,
        input.ContactForPrice,
        input.ExtraSpecs?.Select(e => new ExtraSpecInput(e.Group, e.Name, e.Value, e.Unit)).ToList());

    public static IReadOnlyDictionary<string, object?> Snapshot(CarModel m) => new Dictionary<string, object?>
    {
        ["brandId"] = m.BrandId,
        ["name"] = m.Name,
        ["slug"] = m.Slug,
        ["description"] = m.Description,
        ["status"] = m.Status,
        ["isFeatured"] = m.IsFeatured,
        ["displayOrder"] = m.DisplayOrder,
    };

    public static IReadOnlyDictionary<string, object?> Snapshot(CarVariant v) => new Dictionary<string, object?>
    {
        ["name"] = v.Name,
        ["status"] = v.Status,
        ["referencePrice"] = v.ReferencePrice,
        ["contactForPrice"] = v.ContactForPrice,
        ["bodyType"] = v.BodyType,
        ["fuel"] = v.Fuel,
        ["transmission"] = v.Transmission,
        ["seats"] = v.Seats,
        ["engine"] = v.Engine,
        ["power"] = v.Power,
        ["extraSpecs"] = v.ExtraSpecs.Count == 0 ? null : v.ExtraSpecs.ToList(),
    };

    public static IReadOnlyDictionary<string, object?> Snapshot(VehicleImage i) => new Dictionary<string, object?>
    {
        ["carModelId"] = i.CarModelId,
        ["carVariantId"] = i.CarVariantId,
        ["storageKey"] = i.StorageKey,
        ["alt"] = i.Alt,
        ["displayOrder"] = i.DisplayOrder,
        ["isCover"] = i.IsCover,
    };
}
