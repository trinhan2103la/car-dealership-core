using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Public;
using CarDealership.CoreService.Text;
using FsCheck;
using FsCheck.Fluent;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace CarDealership.CoreService.Catalog.Properties;

/// <summary>
/// Property 8 (task 3.30, Req 2.1, 2.4, 2.10–2.13, 4.1, 4.8): với danh mục và bộ lọc sinh ngẫu nhiên, kết quả của truy vấn
/// công khai trên PostgreSQL THẬT bằng kết quả của hàm tham chiếu in-memory. Số vòng giảm (DB): 30.
/// FsCheck chỉ dùng để SINH dữ liệu (không shrink được qua async); khi lệch, thông báo in đủ kịch bản để tái hiện.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class PublicCatalogQueryProperties : CatalogTestBase
{
    private const int Rounds = 30;
    private static readonly DateTime BaseTime = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string Description = new('m', 60);

    // ---------------------------------------------------------------- mô hình sinh ngẫu nhiên

    public sealed record VariantSpec(string Name, CatalogStatus Status, string BodyType, string Fuel, string Transmission, int Seats, long? Price);

    public sealed record ModelSpec(string Name, string Slug, CatalogStatus Status, int PublishedMinute, VariantSpec[] Variants);

    public sealed record BrandSpec(string Name, string Slug, ModelSpec[] Models);

    public sealed record FilterSpec(
        string? Keyword, string[] Brands, string[] BodyTypes, string[] Fuels, string[] Transmissions, int[] Seats,
        long? Min, long? Max, string Sort);

    // Tên được dựng để thứ tự A–Z KHÔNG phụ thuộc collation của DB: mỗi Brand/Model bắt đầu bằng một token ASCII duy nhất.
    private static readonly (string Name, string Slug)[] BrandPool =
    {
        ("Aston Đông", "aston"), ("Bentley Hà Nội", "bentley"), ("Cadi Ôtô", "cadi"),
    };

    private static readonly string[] Words = { "Điện", "Thể Thao", "Gia Đình", "Đô Thị", "Việt" };
    private static readonly string[] VariantNames = { "Bản Đặc Biệt", "Tiêu Chuẩn", "Cao Cấp", "Hybrid" };
    private static readonly string[] BodyTypes = { "Sedan", "SUV" };
    private static readonly string[] Fuels = { "Xăng", "Dầu", "Hybrid" };
    private static readonly string[] Transmissions = { "Số sàn", "Số tự động" };
    private static readonly int[] SeatPool = { 5, 7 };
    private static readonly long[] Prices = { 400_000_000, 500_000_000, 600_000_000, 800_000_000 };
    private static readonly string[] Keywords = { "đông", "DONG", "ha noi", "bản đặc", "BIET", "dien", "the thao", "zzz", "hybrid", "  viet  " };
    private static readonly string[] Sorts = { "newest", "price_asc", "price_desc", "name_asc" };

    private static Gen<T[]> SubsetOf<T>(T[] items) =>
        Gen.Elements(true, false).ArrayOf(items.Length).Select(flags => items.Where((_, i) => flags[i]).ToArray());

    private static Gen<VariantSpec> Variant(int index) =>
        from name in Gen.Elements(VariantNames)
        from status in Gen.Frequency((3, Gen.Constant(CatalogStatus.Published)), (1, Gen.Constant(CatalogStatus.Draft)), (1, Gen.Constant(CatalogStatus.Hidden)))
        from body in Gen.Elements(BodyTypes)
        from fuel in Gen.Elements(Fuels)
        from transmission in Gen.Elements(Transmissions)
        from seats in Gen.Elements(SeatPool)
        from contact in Gen.Frequency((1, Gen.Constant(true)), (4, Gen.Constant(false)))
        from price in Gen.Elements(Prices)
        select new VariantSpec($"{name} {index}", status, body, fuel, transmission, seats, contact ? null : price);

    private static readonly Gen<BrandSpec[]> Catalog =
        from brandFlags in Gen.Elements(true, false).ArrayOf(BrandPool.Length)
        let chosen = BrandPool.Where((_, i) => brandFlags[i]).DefaultIfEmpty(BrandPool[0]).ToArray()
        from modelCounts in Gen.Choose(0, 3).ArrayOf(chosen.Length)
        from statuses in Gen.Frequency(
                (5, Gen.Constant(CatalogStatus.Published)), (1, Gen.Constant(CatalogStatus.Draft)),
                (1, Gen.Constant(CatalogStatus.Hidden)), (1, Gen.Constant(CatalogStatus.Archived)))
            .ArrayOf(9)
        from minutes in Gen.Shuffle(Enumerable.Range(1, 9).ToArray())
        from words in Gen.Elements(Words).ArrayOf(9)
        from variantCounts in Gen.Choose(0, 3).ArrayOf(9)
        from raw in Variant(0).ArrayOf(3).ArrayOf(9)
        // Tên variant duy nhất trong model: gắn chỉ số vị trí.
        let variants = raw.Select(list => list.Select((v, i) => v with { Name = $"{v.Name[..^2]} {i}" }).ToArray()).ToArray()
        select chosen.Select((b, bi) => new BrandSpec(b.Name, b.Slug,
                Enumerable.Range(0, modelCounts[bi]).Select(k =>
                {
                    var n = bi * 3 + k;
                    var token = $"{(char)('a' + n)}{n:00}";
                    return new ModelSpec($"{char.ToUpperInvariant(token[0])}{token[1..]} {words[n]}", token, statuses[n], minutes[n],
                        variants[n].Take(variantCounts[n]).ToArray());
                }).ToArray()))
            .ToArray();

    private static Gen<FilterSpec> Filter(BrandSpec[] catalog) =>
        from keyword in Gen.Frequency((2, Gen.Constant<string?>(null)), (3, Gen.Elements(Keywords).Select(k => (string?)k)))
        from brands in SubsetOf(catalog.Select(b => b.Slug).ToArray())
        from bodies in Gen.Frequency((3, Gen.Constant(Array.Empty<string>())), (1, SubsetOf(BodyTypes)))
        from fuels in Gen.Frequency((2, Gen.Constant(Array.Empty<string>())), (1, SubsetOf(Fuels)))
        from transmissions in Gen.Frequency((3, Gen.Constant(Array.Empty<string>())), (1, SubsetOf(Transmissions)))
        from seats in Gen.Frequency((3, Gen.Constant(Array.Empty<int>())), (1, SubsetOf(SeatPool)))
        from a in Gen.Frequency((2, Gen.Constant<long?>(null)), (1, Gen.Elements(Prices).Select(p => (long?)p)))
        from b in Gen.Frequency((2, Gen.Constant<long?>(null)), (1, Gen.Elements(Prices).Select(p => (long?)p)))
        from sort in Gen.Elements(Sorts)
        let min = a is { } x && b is { } y ? Math.Min(x, y) : a
        let max = a is { } x2 && b is { } y2 ? Math.Max(x2, y2) : b
        select new FilterSpec(keyword, brands, bodies, fuels, transmissions, seats, min, max, sort);

    private static readonly Gen<(BrandSpec[] Catalog, FilterSpec[] Filters)> Scenario =
        from catalog in Catalog
        from filters in Filter(catalog).ArrayOf(6)
        select (catalog, filters);

    // ---------------------------------------------------------------- dựng dữ liệu qua aggregate thật

    private sealed record Seeded(BrandSpec Brand, ModelSpec Spec, CatalogStatus Status, VariantSpec[] Variants);

    private async Task<List<Seeded>> SeedAsync(BrandSpec[] catalog)
    {
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "TRUNCATE cat_vehicle_images, cat_car_variants, cat_slug_history, cat_car_models, cat_brands CASCADE"));

        var seeded = new List<Seeded>();
        await WithUnitOfWorkAsync(async () =>
        {
            var brands = GetRequiredService<IRepository<Brand, Guid>>();
            var models = GetRequiredService<ICarModelRepository>();
            foreach (var b in catalog)
            {
                var brand = await brands.InsertAsync(new Brand(Guid.NewGuid(), b.Name, b.Slug), autoSave: true);
                foreach (var m in b.Models)
                {
                    var model = new CarModel(Guid.NewGuid(), brand.Id, brand.Name, m.Name, m.Slug, Description);
                    foreach (var v in m.Variants)
                    {
                        var variant = model.AddVariant(Guid.NewGuid(), new CarVariantData(
                            v.Name, v.BodyType, v.Fuel, v.Transmission, v.Seats, "1.5L", "106 hp", v.Price, v.Price is null, null));
                        if (v.Status != CatalogStatus.Draft)
                        {
                            model.ChangeVariantStatus(variant.Id, CatalogStatus.Published);
                        }
                    }

                    var image = model.AddImage(Guid.NewGuid(), Guid.NewGuid().ToString("N"), "Ảnh mặt trước của xe");
                    model.SetCover(image.Id);

                    // Trạng thái model: Published/Hidden cần ít nhất một variant Published, không có thì ở Draft.
                    var canPublish = m.Variants.Any(v => v.Status != CatalogStatus.Draft);
                    var status = m.Status switch
                    {
                        CatalogStatus.Published or CatalogStatus.Hidden when !canPublish => CatalogStatus.Draft,
                        _ => m.Status,
                    };
                    if (status is CatalogStatus.Published or CatalogStatus.Hidden)
                    {
                        model.ChangeStatus(CatalogStatus.Published, BrandStatus.Active, BaseTime.AddMinutes(m.PublishedMinute));
                    }

                    if (status == CatalogStatus.Hidden)
                    {
                        model.ChangeStatus(CatalogStatus.Hidden, BrandStatus.Active, BaseTime);
                    }
                    else if (status == CatalogStatus.Archived)
                    {
                        model.ChangeStatus(CatalogStatus.Archived, BrandStatus.Active, BaseTime);
                    }

                    // Hạ các variant cần Hidden SAU khi model đã đúng trạng thái (variant Published cuối của model Published thì giữ).
                    foreach (var (v, entity) in m.Variants.Zip(model.Variants.ToList()))
                    {
                        if (v.Status == CatalogStatus.Hidden
                            && !(model.Status == CatalogStatus.Published
                                 && model.Variants.Count(x => x.Status == CatalogStatus.Published) == 1
                                 && entity.Status == CatalogStatus.Published))
                        {
                            model.ChangeVariantStatus(entity.Id, CatalogStatus.Hidden);
                        }
                    }

                    model.RefreshDerived(brand.Name);
                    await models.InsertAsync(model, autoSave: true);
                    var actual = model.Variants.Select(e => m.Variants.Single(v => v.Name == e.Name) with { Status = e.Status }).ToArray();
                    seeded.Add(new Seeded(b, m, model.Status, actual));
                }
            }
        });
        return seeded;
    }

    // ---------------------------------------------------------------- hàm tham chiếu

    private static List<(string Slug, long? Min, long? Max, string BrandSlug)> Expected(List<Seeded> seeded, FilterSpec f)
    {
        var keyword = VietnameseText.Normalize(f.Keyword);
        bool VariantOk(VariantSpec v) =>
            v.Status == CatalogStatus.Published
            && (f.BodyTypes.Length == 0 || f.BodyTypes.Contains(v.BodyType))
            && (f.Fuels.Length == 0 || f.Fuels.Contains(v.Fuel))
            && (f.Transmissions.Length == 0 || f.Transmissions.Contains(v.Transmission))
            && (f.Seats.Length == 0 || f.Seats.Contains(v.Seats))
            && (f.Min is null || (v.Price is { } p1 && p1 >= f.Min))
            && (f.Max is null || (v.Price is { } p2 && p2 <= f.Max));

        var variantCriteria = f.BodyTypes.Length + f.Fuels.Length + f.Transmissions.Length + f.Seats.Length > 0
                              || f.Min is not null || f.Max is not null;

        var rows = seeded
            .Where(s => s.Status == CatalogStatus.Published && s.Variants.Any(v => v.Status == CatalogStatus.Published))
            .Where(s => f.Brands.Length == 0 || f.Brands.Contains(s.Brand.Slug))
            .Where(s =>
            {
                if (keyword.Length == 0)
                {
                    return true;
                }

                var published = s.Variants.Where(v => v.Status == CatalogStatus.Published).Select(v => v.Name).OrderBy(n => n, StringComparer.Ordinal);
                var text = VietnameseText.Normalize(string.Join(' ', new[] { s.Brand.Name, s.Spec.Name }.Concat(published)));
                return text.Contains(keyword, StringComparison.Ordinal);
            })
            .Where(s => !variantCriteria || s.Variants.Any(VariantOk))
            .Select(s =>
            {
                var prices = s.Variants.Where(v => v.Status == CatalogStatus.Published && v.Price is not null).Select(v => v.Price!.Value).ToList();
                return (s, Min: prices.Count == 0 ? (long?)null : prices.Min(), Max: prices.Count == 0 ? (long?)null : prices.Max());
            })
            .ToList();

        IEnumerable<(Seeded s, long? Min, long? Max)> ordered = f.Sort switch
        {
            "price_asc" => rows.OrderBy(r => r.Min is null).ThenBy(r => r.Min).ThenBy(r => r.s.Spec.Slug, StringComparer.Ordinal),
            "price_desc" => rows.OrderBy(r => r.Min is null).ThenByDescending(r => r.Min).ThenBy(r => r.s.Spec.Slug, StringComparer.Ordinal),
            "name_asc" => rows.OrderBy(r => r.s.Brand.Name.ToLowerInvariant() + " " + r.s.Spec.Name.ToLowerInvariant(), StringComparer.Ordinal)
                .ThenBy(r => r.s.Spec.Slug, StringComparer.Ordinal),
            _ => rows.OrderByDescending(r => r.s.Spec.PublishedMinute).ThenBy(r => r.s.Spec.Slug, StringComparer.Ordinal),
        };
        return ordered.Select(r => (r.s.Spec.Slug, r.Min, r.Max, r.s.Brand.Slug)).ToList();
    }

    private static string Describe(BrandSpec[] catalog, FilterSpec f, List<Seeded> seeded) =>
        $"filter={f with { }} brands=[{string.Join(",", f.Brands)}] bodies=[{string.Join(",", f.BodyTypes)}] fuels=[{string.Join(",", f.Fuels)}] " +
        $"trans=[{string.Join(",", f.Transmissions)}] seats=[{string.Join(",", f.Seats)}]\ncatalog:\n" +
        string.Join("\n", seeded.Select(s =>
            $"  {s.Brand.Slug}/{s.Spec.Slug} '{s.Spec.Name}' {s.Status} t={s.Spec.PublishedMinute}: " +
            string.Join("; ", s.Variants.Select(v => $"{v.Name}|{v.Status}|{v.BodyType}|{v.Fuel}|{v.Transmission}|{v.Seats}|{v.Price?.ToString() ?? "contact"}"))));

    [Fact]
    public async Task Public_query_matches_the_in_memory_reference_model()
    {
        // FsCheck 3: Sample(số mẫu, kích thước).
        var scenarios = Scenario.Sample(Rounds, 10);
        var @public = GetRequiredService<IPublicCatalogAppService>();
        var checkedFilters = 0;

        foreach (var (catalog, filters) in scenarios)
        {
            var seeded = await SeedAsync(catalog);
            foreach (var f in filters)
            {
                var input = new PublicCarModelListInput
                {
                    Q = f.Keyword,
                    Brand = f.Brands.ToList(),
                    BodyType = f.BodyTypes.ToList(),
                    Fuel = f.Fuels.ToList(),
                    Transmission = f.Transmissions.ToList(),
                    Seats = f.Seats.ToList(),
                    MinPrice = f.Min,
                    MaxPrice = f.Max,
                    Sort = f.Sort,
                    PageSize = 48,
                };
                var page = await AsAdminAsync(() => @public.GetCarModelsAsync(input));
                var expected = Expected(seeded, f);
                var actual = page.Items.Select(i => (i.Slug, i.MinPrice, i.MaxPrice, i.BrandSlug)).ToList();

                actual.ShouldBe(expected, Describe(catalog, f, seeded));
                page.TotalItems.ShouldBe(expected.Count, Describe(catalog, f, seeded));
                checkedFilters++;
            }
        }

        checkedFilters.ShouldBe(Rounds * 6);
    }
}
