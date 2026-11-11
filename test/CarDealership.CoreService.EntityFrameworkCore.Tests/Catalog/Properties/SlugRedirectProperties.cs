using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Public;
using FsCheck;
using FsCheck.Fluent;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace CarDealership.CoreService.Catalog.Properties;

/// <summary>
/// Property 13, phần chuyển hướng (task 3.23, Req 3.8, 3.9): với chuỗi đổi Slug ngẫu nhiên của Brand và hai CarModel
/// (gồm lấy lại Slug cũ của chính mình), MỌI tổ hợp Slug cũ/hiện tại của Brand × CarModel trả đúng URL hiện tại trong MỘT
/// bước; Slug chưa từng tồn tại luôn "không tìm thấy". Chạy qua app service + resolver thật trên PostgreSQL, 15 vòng.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class SlugRedirectProperties : CatalogTestBase
{
    private const int Rounds = 15;

    private enum Target
    {
        Brand,
        Model0,
        Model1,
    }

    // Ba bể Slug rời nhau: không thao tác nào chiếm Slug (cũ hay hiện tại) của entity khác, nên mọi thao tác hợp lệ.
    private static readonly string[] Prefix = { "br", "ma", "mb" };

    private static readonly Gen<(Target Target, int Index)[]> Renames =
        from count in Gen.Choose(1, 8)
        from ops in (from t in Gen.Elements(Enum.GetValues<Target>()) from i in Gen.Choose(0, 3) select (t, i)).ArrayOf(count)
        select ops;

    private IPublicCatalogAppService Public => GetRequiredService<IPublicCatalogAppService>();

    [Fact]
    public async Task Every_old_slug_combination_redirects_to_the_current_url_in_one_step()
    {
        var rounds = 0;
        // FsCheck 3: Sample(số mẫu, kích thước).
        foreach (var renames in Renames.Sample(Rounds, 8))
        {
            rounds++;
            await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
                "TRUNCATE cat_vehicle_images, cat_car_variants, cat_slug_history, cat_car_models, cat_brands CASCADE"));

            var brand = await CreateBrandAsync("Br 0", "br-0");
            var (_, m0) = await CreatePublishedModelAsync("Ma 0", brand);
            var (_, m1) = await CreatePublishedModelAsync("Mb 0", brand);
            m0.Slug.ShouldBe("ma-0");
            m1.Slug.ShouldBe("mb-0");

            var current = new[] { "br-0", "ma-0", "mb-0" };
            var ever = new[] { new HashSet<string> { "br-0" }, new HashSet<string> { "ma-0" }, new HashSet<string> { "mb-0" } };
            var ids = new[] { brand.Id, m0.Id, m1.Id };
            var trace = new List<string>();

            foreach (var (target, index) in renames)
            {
                var t = (int)target;
                var slug = $"{Prefix[t]}-{index}";
                trace.Add($"{target}->{slug}");
                if (target == Target.Brand)
                {
                    await AsAdminAsync(() => Brands.UpdateAsync(brand.Id, new SaveBrandInput { Name = brand.Name, Slug = slug }));
                }
                else
                {
                    var model = await AsAdminAsync(() => CarModels.GetAsync(ids[t]));
                    await AsAdminAsync(() => CarModels.UpdateAsync(model.Id, new UpdateCarModelInput
                    {
                        Name = model.Name, Slug = slug, Description = model.Description, ConcurrencyStamp = model.ConcurrencyStamp,
                    }));
                }

                current[t] = slug;
                ever[t].Add(slug);
            }

            var context = $"renames: {string.Join(", ", trace)}";
            foreach (var model in new[] { 1, 2 })
            {
                var url = $"/xe/{current[0]}/{current[model]}";
                foreach (var b in ever[0])
                {
                    foreach (var m in ever[model])
                    {
                        var result = await AsAdminAsync(() => Public.GetCarModelBySlugsAsync(b, m));
                        if (b == current[0] && m == current[model])
                        {
                            result.Redirect.ShouldBeNull($"{context}; lookup {b}/{m}");
                            result.CarModel!.Id.ShouldBe(ids[model], $"{context}; lookup {b}/{m}");
                        }
                        else
                        {
                            result.Redirect.ShouldBe(url, $"{context}; lookup {b}/{m}");
                        }
                    }
                }
            }

            // Slug chưa từng tồn tại; Slug của model này đặt dưới Brand khác không tồn tại.
            await Should.ThrowAsync<EntityNotFoundException>(() => AsAdminAsync(() => Public.GetCarModelBySlugsAsync("br-9", current[1])));
            await Should.ThrowAsync<EntityNotFoundException>(() => AsAdminAsync(() => Public.GetCarModelBySlugsAsync(current[0], "ma-9")));
            await Should.ThrowAsync<EntityNotFoundException>(() => AsAdminAsync(() => Public.GetCarModelBySlugsAsync(current[0], "mb-9")));
        }

        rounds.ShouldBe(Rounds);
    }
}
