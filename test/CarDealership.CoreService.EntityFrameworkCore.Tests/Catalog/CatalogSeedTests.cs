using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Images;
using CarDealership.CoreService.Catalog.Public;
using CarDealership.CoreService.Catalog.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>Seed Demo_Data catalog (Ngày 92) chạy trên dữ liệu THẬT trong <c>seed/demo</c>, PostgreSQL và S3Mock.</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class CatalogSeedTests : CatalogTestBase, IClassFixture<S3MockFixture>
{
    private readonly S3MockFixture _s3;

    public CatalogSeedTests(S3MockFixture s3)
    {
        _s3 = s3;
    }

    /// <summary>Thư mục <c>seed/demo</c> của repo (đi ngược từ thư mục chạy test).</summary>
    private static string RepoSeedPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "seed", "demo");
            if (File.Exists(Path.Combine(candidate, "catalog.json")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Không tìm thấy seed/demo/catalog.json.");
    }

    private RecordingImageStorage UseS3Mock()
    {
        var storage = new RecordingImageStorage(new S3ImageStorage(Options.Create(new ImageStorageOptions
        {
            ServiceUrl = _s3.ServiceUrl,
            Bucket = S3MockFixture.Bucket,
            AccessKey = S3MockFixture.AccessKey,
            SecretKey = S3MockFixture.SecretKey,
            TimeoutSeconds = 5,
        })));
        GetRequiredService<ImageStorageSwitch>().Use(storage);
        return storage;
    }

    private Task SeedAsync(string? environment, string? dataPath = null) =>
        GetRequiredService<CatalogDemoSeedContributor>().SeedAsync(new CatalogDemoSeedOptions
        {
            Environment = environment,
            DataPath = dataPath ?? RepoSeedPath(),
        });

    private Task<(int Brands, int Models, int Variants, int Images)> CountsAsync() =>
        WithDbAsync(async db => (
            await db.Set<Brand>().CountAsync(),
            await db.Set<CarModel>().CountAsync(),
            await db.Set<CarVariant>().CountAsync(),
            await db.Set<VehicleImage>().CountAsync()));

    [Fact]
    public async Task Seeding_twice_creates_the_demo_catalog_once()
    {
        var storage = UseS3Mock();

        await SeedAsync("dev");
        var first = await CountsAsync();
        var firstKeys = storage.Puts.ToList();
        await SeedAsync("demo");

        first.ShouldBe((3, 6, 11, 12));
        (await CountsAsync()).ShouldBe(first);
        // Lần 2 không upload lại (model đã có); khóa object tất định nên kể cả upload lại cũng chỉ ghi đè.
        storage.Puts.ShouldBe(firstKeys);
        firstKeys.Count.ShouldBe(12 * 3);

        var models = await WithDbAsync(db => db.Set<CarModel>().AsNoTracking().ToListAsync());
        models.ShouldAllBe(m => m.IsDemo && m.Status == CatalogStatus.Published);
        (await WithDbAsync(db => db.Set<Brand>().AsNoTracking().ToListAsync())).ShouldAllBe(b => b.IsDemo);
    }

    [Fact]
    public async Task Seeded_cars_are_visible_through_the_public_catalog_with_objects_in_storage()
    {
        var storage = UseS3Mock();
        await SeedAsync("dev");

        var page = await GetRequiredService<IPublicCatalogAppService>()
            .GetCarModelsAsync(new PublicCarModelListInput { PageSize = 48 });

        page.TotalItems.ShouldBe(6);
        page.Items.Select(i => $"{i.BrandSlug}/{i.Slug}").OrderBy(s => s, StringComparer.Ordinal).ShouldBe(new[]
        {
            "honda/city", "honda/cr-v", "hyundai/accent", "hyundai/tucson", "toyota/corolla-cross", "toyota/vios",
        });
        foreach (var key in storage.Puts)
        {
            (await storage.ExistsAsync(key)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Missing_manifest_entry_fails_with_the_file_name_before_anything_is_written()
    {
        var storage = UseS3Mock();
        var copy = CopySeed();
        var manifestPath = Path.Combine(copy, "images", "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
        manifest.Remove(manifest.First(n => n!["file"]!.GetValue<string>() == "honda-city-side"));
        File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        try
        {
            var ex = await Should.ThrowAsync<InvalidOperationException>(() => SeedAsync("dev", copy));

            ex.Message.ShouldContain("honda-city-side");
            storage.Puts.ShouldBeEmpty();
            (await CountsAsync()).ShouldBe((0, 0, 0, 0));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Fact]
    public async Task Manifest_entry_without_license_is_rejected()
    {
        UseS3Mock();
        var copy = CopySeed();
        var manifestPath = Path.Combine(copy, "images", "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsArray();
        manifest.First(n => n!["file"]!.GetValue<string>() == "toyota-vios-front")!.AsObject().Remove("license");
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        try
        {
            var ex = await Should.ThrowAsync<InvalidOperationException>(() => SeedAsync("dev", copy));
            ex.Message.ShouldContain("toyota-vios-front");
            ex.Message.ShouldContain("license");
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    [Theory]
    [InlineData("prod")]
    [InlineData(null)]
    [InlineData("")]
    public async Task Prod_or_unset_environment_creates_nothing(string? environment)
    {
        var storage = UseS3Mock();

        await SeedAsync(environment);

        storage.Puts.ShouldBeEmpty();
        (await CountsAsync()).ShouldBe((0, 0, 0, 0));
    }

    [Fact]
    public async Task Unknown_environment_is_an_error()
    {
        UseS3Mock();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => SeedAsync("staging"));

        ex.Message.ShouldContain(CatalogDemoSeedOptions.EnvironmentVariable);
    }

    private static string CopySeed()
    {
        var source = RepoSeedPath();
        var target = Path.Combine(Path.GetTempPath(), "cdp-seed-" + Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }

        return target;
    }
}
