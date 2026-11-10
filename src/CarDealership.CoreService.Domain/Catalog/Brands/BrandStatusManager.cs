using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.CarModels;
using Volo.Abp.Domain.Services;

namespace CarDealership.CoreService.Catalog.Brands;

/// <summary>
/// Domain service đổi trạng thái Brand (Req 7.5). Cần repository CarModel nên không thể là phương thức của Brand.
/// Brand chuyển Inactive khi còn CarModel Published: giữ Active và ném 422 kèm DANH SÁCH ĐẦY ĐỦ tên và Slug các CarModel cần ẩn.
/// </summary>
public class BrandStatusManager : DomainService
{
    private readonly ICarModelRepository _carModels;

    public BrandStatusManager(ICarModelRepository carModels)
    {
        _carModels = carModels;
    }

    public async Task ChangeStatusAsync(Brand brand, BrandStatus target)
    {
        if (target == BrandStatus.Inactive && brand.Status != BrandStatus.Inactive)
        {
            var blocking = await _carModels.GetPublishedSummariesByBrandAsync(brand.Id);
            if (blocking.Count > 0)
            {
                throw CatalogExceptions.BrandHasPublishedCarModels(blocking.OrderBy(c => c.Slug, System.StringComparer.Ordinal).ToList());
            }
        }

        brand.SetStatus(target);
    }
}
