using CarDealership.CoreService.Catalog.Seed;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;

namespace CarDealership.CoreService.Catalog;

/// <summary>Module Catalog (Brand/CarModel/Variant/ảnh). Bảng dùng tiền tố "cat_".</summary>
public class CatalogDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Seed Demo_Data catalog: môi trường và thư mục dữ liệu lấy từ biến môi trường (Ngày 92).
        var configuration = context.Services.GetConfiguration();
        Configure<CatalogDemoSeedOptions>(options =>
        {
            options.Environment = configuration[CatalogDemoSeedOptions.EnvironmentVariable];
            var path = configuration[CatalogDemoSeedOptions.DataPathVariable];
            if (!string.IsNullOrWhiteSpace(path))
            {
                options.DataPath = path;
            }
        });
    }
}
