using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.CarModels;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;

namespace CarDealership.CoreService.Catalog.Brands;

/// <summary>
/// Brand đổi tên thì <c>search_text</c> của mọi CarModel thuộc Brand phải đổi theo (Req 2.8: tìm theo tên Brand).
/// Local event chạy trong CÙNG UoW với việc đổi tên, nên cùng commit hoặc cùng rollback.
/// </summary>
public class BrandRenamedHandler : ILocalEventHandler<BrandRenamedEvent>, ITransientDependency
{
    private readonly ICarModelRepository _carModels;

    public BrandRenamedHandler(ICarModelRepository carModels)
    {
        _carModels = carModels;
    }

    public async Task HandleEventAsync(BrandRenamedEvent eventData)
    {
        var models = await _carModels.GetListByBrandWithVariantsAsync(eventData.BrandId);
        foreach (var model in models)
        {
            model.RecomputeSearchText(eventData.NewName);
        }

        if (models.Count > 0)
        {
            await _carModels.UpdateManyAsync(models);
        }
    }
}
