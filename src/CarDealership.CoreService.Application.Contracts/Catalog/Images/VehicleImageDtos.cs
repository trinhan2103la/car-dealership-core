using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.CarModels;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace CarDealership.CoreService.Catalog.Images;

public class UploadVehicleImageInput
{
    public IRemoteStreamContent? Sm { get; set; }
    public IRemoteStreamContent? Md { get; set; }
    public IRemoteStreamContent? Lg { get; set; }
    public string? Alt { get; set; }
    public Guid? CarVariantId { get; set; }
}

public class ReorderImagesInput
{
    public List<Guid> ImageIds { get; set; } = new();
    public Guid? CoverImageId { get; set; }
}

public class UpdateImageInput
{
    public string? Alt { get; set; }
}

public interface IVehicleImageAppService : IApplicationService
{
    Task<VehicleImageDto> UploadAsync(Guid carModelId, UploadVehicleImageInput input);
    Task<List<VehicleImageDto>> ReorderAsync(Guid carModelId, ReorderImagesInput input);
    Task<VehicleImageDto> UpdateAsync(Guid carModelId, Guid imageId, UpdateImageInput input);
    Task DeleteAsync(Guid carModelId, Guid imageId);
}