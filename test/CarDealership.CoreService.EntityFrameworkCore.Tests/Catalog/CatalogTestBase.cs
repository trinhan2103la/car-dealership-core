using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Images;
using CarDealership.CoreService.EntityFrameworkCore;
using CarDealership.CoreService.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Tracing;
using Volo.Abp.Uow;

namespace CarDealership.CoreService.Catalog;

/// <summary>
/// Module test cho Catalog: GIỮ nguyên mọi thứ của module EF Core test (PostgreSQL thật) nhưng BẬT lại transaction của UoW
/// (module EF Core test tắt transaction cho nhanh). Rollback, khóa hàng <c>FOR UPDATE</c> và "audit lỗi thì nghiệp vụ mất"
/// chỉ có nghĩa khi có transaction thật, giống request không phải GET trên production.
/// </summary>
[DependsOn(typeof(CoreServiceEntityFrameworkCoreTestModule))]
public class CoreServiceCatalogTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // AddAlwaysDisableUnitOfWorkTransaction() của module EF Core test THAY IUnitOfWorkManager bằng
        // AlwaysDisableTransactionsUnitOfWorkManager (ép IsTransactional = false). Trả lại UnitOfWorkManager mặc định.
        context.Services.Replace(ServiceDescriptor.Singleton<IUnitOfWorkManager, UnitOfWorkManager>());

        // IAuditTrail thật được bọc để test ép "ghi audit lỗi" (Req 17.4).
        context.Services.AddSingleton<AuditFaultSwitch>();
        context.Services.Replace(ServiceDescriptor.Transient<IAuditTrail>(sp =>
            new SwitchableAuditTrail(sp.GetRequiredService<AuditTrail>(), sp.GetRequiredService<AuditFaultSwitch>())));

        // IImageStorage do test chọn (S3Mock, storage chết...). Test không đụng storage thì không bao giờ resolve nó.
        context.Services.AddSingleton<ImageStorageSwitch>();
        context.Services.Replace(ServiceDescriptor.Transient<IImageStorage>(sp =>
            sp.GetRequiredService<ImageStorageSwitch>().Get()));
    }
}

/// <summary>
/// Công tắc cho test: IImageStorage mà VehicleImageAppService sẽ nhận. Dùng method thay vì property public kiểu IImageStorage,
/// vì ABP bật property injection của Autofac: property đó sẽ bị inject chính factory ở trên → đệ quy vô hạn.
/// </summary>
public class ImageStorageSwitch
{
    private IImageStorage? _current;

    public void Use(IImageStorage storage) => _current = storage;

    /// <summary>Chưa chọn storage: vẫn resolve được (app service nhận qua constructor), chỉ lỗi khi thật sự dùng tới.</summary>
    public IImageStorage Get() => _current ?? NotConfiguredImageStorage.Instance;

    private sealed class NotConfiguredImageStorage : IImageStorage
    {
        public static readonly NotConfiguredImageStorage Instance = new();

        private static InvalidOperationException Missing() =>
            new("Test chưa gọi ImageStorageSwitch.Use(...) mà lại đụng tới Object_Storage.");

        public Task PutAsync(string key, System.IO.Stream content, string contentType, System.Threading.CancellationToken cancellationToken = default) =>
            throw Missing();

        public Task DeleteAsync(string key, System.Threading.CancellationToken cancellationToken = default) => throw Missing();

        public Task<bool> ExistsAsync(string key, System.Threading.CancellationToken cancellationToken = default) => throw Missing();
    }
}

/// <summary>Công tắc cho test: bật thì lần <c>WriteAsync</c> tiếp theo ném lỗi SAU khi thay đổi nghiệp vụ đã được flush.</summary>
public class AuditFaultSwitch
{
    public bool FailWrites { get; set; }
}

public class SwitchableAuditTrail : IAuditTrail
{
    private readonly IAuditTrail _inner;
    private readonly AuditFaultSwitch _switch;

    public SwitchableAuditTrail(IAuditTrail inner, AuditFaultSwitch @switch)
    {
        _inner = inner;
        _switch = @switch;
    }

    public Task<bool> WriteAsync(
        string action, string entityType, string entityId,
        IReadOnlyDictionary<string, object?>? before, IReadOnlyDictionary<string, object?>? after)
    {
        if (_switch.FailWrites)
        {
            throw new InvalidOperationException("Audit write failed (injected by test).");
        }

        return _inner.WriteAsync(action, entityType, entityId, before, after);
    }

    public Task WriteSecurityEventAsync(string action, Guid? userId, string? reason) =>
        _inner.WriteSecurityEventAsync(action, userId, reason);
}

/// <summary>Nền chung cho test Catalog: UoW thật có transaction, hỗ trợ đổi người dùng, truy vấn DB và dựng dữ liệu nhanh.</summary>
public abstract class CatalogTestBase : CoreServiceTestBase<CoreServiceCatalogTestModule>
{
    protected static readonly Guid ActorId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    protected AuditFaultSwitch FaultSwitch => GetRequiredService<AuditFaultSwitch>();

    protected Task<T> WithDbAsync<T>(Func<CoreServiceDbContext, Task<T>> action) =>
        WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await action(db);
        });

    /// <summary>Chạy <paramref name="action"/> như một request của Admin_User <see cref="ActorId"/> với Correlation_Id đã cho.</summary>
    protected async Task<T> AsAdminAsync<T>(Func<Task<T>> action, string correlationId = "test-cid")
    {
        var accessor = GetRequiredService<ICurrentPrincipalAccessor>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(AbpClaimTypes.UserId, ActorId.ToString()), new Claim(AbpClaimTypes.UserName, "admin") },
            "test"));

        using (accessor.Change(principal))
        using (GetRequiredService<ICorrelationIdProvider>().Change(correlationId))
        {
            // new AbpUnitOfWorkOptions() mặc định KHÔNG transaction; request không phải GET thì có, nên bật tường minh.
            return await WithUnitOfWorkAsync(new AbpUnitOfWorkOptions { IsTransactional = true }, action);
        }
    }

    protected Task AsAdminAsync(Func<Task> action, string correlationId = "test-cid") =>
        AsAdminAsync(async () =>
        {
            await action();
            return 0;
        }, correlationId);

    protected Task<List<AuditEntry>> AuditEntriesAsync(string? entityId = null) =>
        WithDbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(e => entityId == null || e.EntityId == entityId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync());

    // ---------------------------------------------------------------- dựng dữ liệu

    protected IBrandAppService Brands => GetRequiredService<IBrandAppService>();

    protected Task<BrandDto> CreateBrandAsync(string? name = null, string? slug = null, BrandStatus? status = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return AsAdminAsync(() => Brands.CreateAsync(new SaveBrandInput
        {
            Name = name ?? $"Brand {suffix}",
            Slug = slug ?? $"brand-{suffix}",
            Status = status,
        }));
    }

    protected static CarVariantData ValidVariant(string name = "1.5G", decimal? price = 458_000_000m, bool contact = false) =>
        new(name, "Sedan", "Xăng", "Số tự động", 5, "1.5L", "106 hp", contact ? null : price, contact, null);

    protected ICarModelAppService CarModels => GetRequiredService<ICarModelAppService>();

    protected ICarVariantAppService Variants => GetRequiredService<ICarVariantAppService>();

    protected const string LongDescription =
        "Mẫu sedan hạng B tiết kiệm nhiên liệu, rộng rãi, phù hợp gia đình và chạy dịch vụ trong đô thị.";

    protected static SaveCarVariantInput VariantInput(
        string name = "1.5G", decimal? price = 458_000_000m, bool contact = false,
        string bodyType = "Sedan", string fuel = "Xăng", string transmission = "Số tự động", int? seats = 5,
        string? engine = "1.5L", string? power = "106 hp") => new()
    {
        Name = name,
        BodyType = bodyType,
        Fuel = fuel,
        Transmission = transmission,
        Seats = seats,
        Engine = engine,
        Power = power,
        ReferencePrice = contact ? null : price,
        ContactForPrice = contact,
    };

    protected Task<CarModelDto> CreateModelAsync(Guid brandId, string name = "Vios", string? slug = null, string? description = LongDescription, bool featured = false) =>
        AsAdminAsync(() => CarModels.CreateAsync(new CreateCarModelInput
        {
            BrandId = brandId,
            Name = name,
            Slug = slug ?? Catalog.Slugs.SlugGenerator.FromName(name),
            Description = description,
            IsFeatured = featured,
        }));

    protected Task<CarVariantDto> AddPublishedVariantAsync(Guid modelId, SaveCarVariantInput? input = null) =>
        AsAdminAsync(async () =>
        {
            var variant = await Variants.CreateAsync(modelId, input ?? VariantInput());
            return await Variants.ChangeStatusAsync(modelId, variant.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Published });
        });

    /// <summary>Thêm ảnh (khóa ngẫu nhiên) qua domain service, tùy chọn đặt làm cover.</summary>
    protected Task<Guid> AddImageAsync(Guid modelId, string alt = "Ảnh mặt trước của xe", bool cover = false) =>
        AsAdminAsync(async () =>
        {
            var manager = GetRequiredService<Catalog.Images.VehicleImageManager>();
            var (_, image) = await manager.AddAsync(modelId, Guid.NewGuid().ToString("N"), alt);
            if (cover)
            {
                await manager.SetCoverAsync(modelId, image.Id);
            }

            return image.Id;
        });

    /// <summary>Brand Active + CarModel đủ điều kiện xuất bản (mô tả, cover, alt, 1 variant Published) rồi Published.</summary>
    protected async Task<(BrandDto Brand, CarModelDto Model)> CreatePublishedModelAsync(
        string modelName = "Vios", BrandDto? brand = null, SaveCarVariantInput? variant = null, bool featured = false)
    {
        brand ??= await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id, modelName, featured: featured);
        await AddPublishedVariantAsync(model.Id, variant);
        await AddImageAsync(model.Id, cover: true);
        var published = await AsAdminAsync(() => CarModels.ChangeStatusAsync(model.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Published }));
        return (brand, published);
    }
}
