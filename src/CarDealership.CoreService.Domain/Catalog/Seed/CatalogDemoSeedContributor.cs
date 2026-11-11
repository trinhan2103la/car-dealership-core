using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Images;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace CarDealership.CoreService.Catalog.Seed;

/// <summary>
/// Seed Demo_Data catalog (Ngày 92) từ <c>seed/demo/catalog.json</c> + <c>images/</c>.
/// - Chỉ chạy khi <c>SEED_ENVIRONMENT</c> là <c>dev</c> hoặc <c>demo</c>; <c>prod</c> hoặc không đặt thì bỏ qua; giá trị lạ thì lỗi.
/// - Kiểm TOÀN BỘ dữ liệu (manifest có source + license, đủ 3 WebP) trước khi ghi gì.
/// - Idempotent theo khóa tự nhiên: Brand theo slug, CarModel theo (brand, slug), Variant theo (model, lower(name)).
/// - Tạo qua chính aggregate nên dữ liệu seed cũng phải qua Publish_Validation; mọi bản ghi có <c>isDemo = true</c>.
/// - Khóa object ảnh TẤT ĐỊNH (hash brand/model/file): chạy lại chỉ ghi đè cùng object, lần chạy lỗi không để object mồ côi.
/// - Một unit of work: lỗi giữa chừng thì rollback toàn bộ, DbMigrator thoát mã khác 0.
/// </summary>
public class CatalogDemoSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<Brand, Guid> _brands;
    private readonly ICarModelRepository _carModels;
    private readonly IGuidGenerator _guids;
    private readonly IClock _clock;
    private readonly IOptions<CatalogDemoSeedOptions> _options;
    private readonly IAbpLazyServiceProvider _lazy;

    public ILogger<CatalogDemoSeedContributor> Logger { get; set; } = NullLogger<CatalogDemoSeedContributor>.Instance;

    public CatalogDemoSeedContributor(
        IRepository<Brand, Guid> brands,
        ICarModelRepository carModels,
        IGuidGenerator guids,
        IClock clock,
        IOptions<CatalogDemoSeedOptions> options,
        IAbpLazyServiceProvider lazy)
    {
        _brands = brands;
        _carModels = carModels;
        _guids = guids;
        _clock = clock;
        _options = options;
        _lazy = lazy;
    }

    public virtual Task SeedAsync(DataSeedContext context) => SeedAsync(_options.Value);

    [UnitOfWork(isTransactional: true)]
    public virtual async Task SeedAsync(CatalogDemoSeedOptions options)
    {
        var environment = options.Environment?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(environment))
        {
            Logger.LogInformation("{Variable} chưa đặt: bỏ qua seed catalog demo.", CatalogDemoSeedOptions.EnvironmentVariable);
            return;
        }

        if (!CatalogDemoSeedOptions.KnownEnvironments.Contains(environment))
        {
            throw new InvalidOperationException(
                $"{CatalogDemoSeedOptions.EnvironmentVariable} phải là dev, demo hoặc prod.");
        }

        if (!CatalogDemoSeedOptions.SeedingEnvironments.Contains(environment))
        {
            Logger.LogInformation("Môi trường {Environment}: không seed catalog demo.", environment);
            return;
        }

        // Kiểm hết trước khi ghi.
        var catalog = CatalogDemoSeedLoader.Load(options.DataPath, ImageStorageKeys.Sizes);
        var storage = _lazy.LazyGetRequiredService<IImageStorage>();

        foreach (var seedBrand in catalog.Brands)
        {
            var brand = await EnsureBrandAsync(seedBrand);
            foreach (var seedModel in seedBrand.CarModels)
            {
                await EnsureCarModelAsync(brand, seedModel, options.DataPath, storage);
            }
        }
    }

    private async Task<Brand> EnsureBrandAsync(SeedBrand seed)
    {
        var slug = Slugs.Slug.Normalize(seed.Slug);
        var existing = await _brands.FirstOrDefaultAsync(b => b.Slug == slug);
        if (existing is not null)
        {
            return existing;
        }

        var brand = new Brand(_guids.Create(), seed.Name, slug, displayOrder: seed.DisplayOrder, isDemo: true);
        return await _brands.InsertAsync(brand, autoSave: true);
    }

    private async Task EnsureCarModelAsync(Brand brand, SeedCarModel seed, string dataPath, IImageStorage storage)
    {
        var slug = Slugs.Slug.Normalize(seed.Slug);
        var existingId = (await _carModels.FirstOrDefaultAsync(m => m.BrandId == brand.Id && m.Slug == slug))?.Id;
        if (existingId is { } id)
        {
            // Model đã có: chỉ bổ sung variant còn thiếu (ảnh không có khóa tự nhiên nên không thêm lại).
            var model = (await _carModels.FindWithDetailsAsync(id, lockForUpdate: true))!;
            if (AddMissingVariants(model, seed) > 0)
            {
                model.RefreshDerived(brand.Name);
                await _carModels.UpdateAsync(model, autoSave: true);
            }

            return;
        }

        var created = new CarModel(
            _guids.Create(), brand.Id, brand.Name, seed.Name, slug, seed.Description,
            isFeatured: seed.IsFeatured, displayOrder: seed.DisplayOrder, isDemo: true);
        AddMissingVariants(created, seed);

        foreach (var seedImage in seed.Images)
        {
            var baseKey = DeterministicKey(brand.Slug, slug, seedImage.File);
            foreach (var size in ImageStorageKeys.Sizes)
            {
                await using var stream = File.OpenRead(CatalogDemoSeedLoader.VariantPath(dataPath, seedImage.File, size));
                await storage.PutAsync(ImageStorageKeys.ObjectKey(created.Id, baseKey, size), stream, "image/webp");
            }

            var image = created.AddImage(_guids.Create(), baseKey, seedImage.Alt);
            if (seedImage.Cover)
            {
                created.SetCover(image.Id);
            }
        }

        created.RefreshDerived(brand.Name);
        created.ChangeStatus(CatalogStatus.Published, brand.Status, _clock.Now);
        await _carModels.InsertAsync(created, autoSave: true);
        Logger.LogInformation("Seed demo: {Brand} {Model} ({Variants} phiên bản, {Images} ảnh).",
            brand.Name, created.Name, created.Variants.Count, created.Images.Count);
    }

    private int AddMissingVariants(CarModel model, SeedCarModel seed)
    {
        var added = 0;
        foreach (var v in seed.Variants)
        {
            if (model.Variants.Any(existing => string.Equals(existing.Name, v.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var variant = model.AddVariant(_guids.Create(), new CarVariantData(
                v.Name, v.BodyType, v.Fuel, v.Transmission, v.Seats, v.Engine, v.Power,
                v.ContactForPrice ? null : v.ReferencePrice, v.ContactForPrice, null));
            model.ChangeVariantStatus(variant.Id, CatalogStatus.Published);
            added++;
        }

        return added;
    }

    /// <summary>UUID (dạng N) suy từ brand/model/file: ổn định giữa các lần chạy, không chứa tên file.</summary>
    public static string DeterministicKey(string brandSlug, string modelSlug, string file)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"cdp-demo-seed:{brandSlug}/{modelSlug}/{file}"));
        return new Guid(hash.AsSpan(0, 16)).ToString("N");
    }
}
