using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Identity.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>CarModel + CarVariant qua app service trên PostgreSQL thật (Req 8, 17.1).</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class CarModelAppServiceTests : CatalogTestBase
{
    [Fact]
    public async Task Create_puts_the_model_in_draft_even_under_an_inactive_brand()
    {
        var brand = await CreateBrandAsync(status: BrandStatus.Inactive);

        var model = await CreateModelAsync(brand.Id, "Vios");

        model.Status.ShouldBe(CatalogStatus.Draft);
        model.Slug.ShouldBe("vios");
        model.IsFeatured.ShouldBeFalse();
        model.DisplayOrder.ShouldBe(0);
    }

    [Fact]
    public async Task Create_reports_every_field_error_including_unknown_brand()
    {
        var ex = await Should.ThrowAsync<AbpValidationException>(() => AsAdminAsync(() => CarModels.CreateAsync(new CreateCarModelInput
        {
            BrandId = Guid.NewGuid(),
            Name = "",
            Slug = "Bad Slug",
            Description = new string('x', 5001),
            DisplayOrder = -1,
        })));

        ex.ValidationErrors.SelectMany(e => e.MemberNames)
            .ShouldBe(new[] { "name", "slug", "description", "displayOrder", "brandId" }, ignoreOrder: true);
    }

    [Fact]
    public async Task Same_slug_is_allowed_under_two_brands_but_not_twice_in_one_brand()
    {
        var toyota = await CreateBrandAsync("Toyota", "toyota");
        var honda = await CreateBrandAsync("Honda", "honda");

        await CreateModelAsync(toyota.Id, "Vios", "vios");
        await CreateModelAsync(honda.Id, "Vios", "vios");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateModelAsync(toyota.Id, "Vios Mới", "vios"));
        ex.Code.ShouldBe(CatalogErrorCodes.Duplicate);
        ex.Data["field"].ShouldBe("slug");

        var byName = await Should.ThrowAsync<BusinessException>(() => CreateModelAsync(toyota.Id, "  VIOS ", "vios-2"));
        byName.Data["field"].ShouldBe("name");
    }

    [Fact]
    public async Task Two_puts_with_the_same_stamp_second_gets_409()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);

        await AsAdminAsync(() => CarModels.UpdateAsync(model.Id, new UpdateCarModelInput
        {
            Name = "Vios 2026", Slug = model.Slug, Description = LongDescription, ConcurrencyStamp = model.ConcurrencyStamp,
        }));

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => AsAdminAsync(() => CarModels.UpdateAsync(model.Id, new UpdateCarModelInput
        {
            Name = "Vios STALE", Slug = model.Slug, Description = LongDescription, ConcurrencyStamp = model.ConcurrencyStamp,
        })));

        (await AsAdminAsync(() => CarModels.GetAsync(model.Id))).Name.ShouldBe("Vios 2026");
    }

    [Fact]
    public async Task Unknown_model_or_variant_is_404()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);

        await Should.ThrowAsync<EntityNotFoundException>(() => AsAdminAsync(() => CarModels.GetAsync(Guid.NewGuid())));
        await Should.ThrowAsync<EntityNotFoundException>(() => AsAdminAsync(() => Variants.GetAsync(model.Id, Guid.NewGuid())));
        await Should.ThrowAsync<EntityNotFoundException>(() => AsAdminAsync(() => Variants.CreateAsync(Guid.NewGuid(), VariantInput())));
    }

    [Fact]
    public async Task Each_change_writes_one_audit_entry_and_a_no_op_update_writes_none()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);

        await AsAdminAsync(() => CarModels.UpdateAsync(model.Id, new UpdateCarModelInput
        {
            Name = model.Name, Slug = model.Slug, Description = LongDescription,
        }));
        await AsAdminAsync(() => CarModels.UpdateAsync(model.Id, new UpdateCarModelInput
        {
            Name = model.Name, Slug = model.Slug, Description = LongDescription, IsFeatured = true,
        }));

        var entries = await AuditEntriesAsync(model.Id.ToString());
        entries.Select(e => e.Action).ShouldBe(new[] { AuditActions.Create, AuditActions.Update });
        using var changes = JsonDocument.Parse(entries[1].Changes);
        changes.RootElement.EnumerateArray().Select(c => c.GetProperty("field").GetString()).ShouldBe(new[] { "isFeatured" });
    }

    // ---------------------------------------------------------------- variant

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100_000_000_000, true)]
    [InlineData(100_000_000_001, false)]
    public async Task Price_boundaries(long price, bool valid)
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);

        var act = () => AsAdminAsync(() => Variants.CreateAsync(model.Id, VariantInput(price: price)));

        if (valid)
        {
            (await act()).ReferencePrice.ShouldBe(price);
        }
        else
        {
            var ex = await Should.ThrowAsync<AbpValidationException>(act);
            ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "referencePrice" });
        }
    }

    [Fact]
    public async Task One_request_with_many_errors_reports_them_all()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var input = VariantInput(price: 1.5m, seats: 17, bodyType: "");
        input.ExtraSpecs = Enumerable.Range(0, 201).Select(i => new ExtraSpecDto { Group = "G", Name = $"n{i}", Value = "v" }).ToList();

        var ex = await Should.ThrowAsync<AbpValidationException>(() => AsAdminAsync(() => Variants.CreateAsync(model.Id, input)));

        ex.ValidationErrors.SelectMany(e => e.MemberNames).Distinct()
            .ShouldBe(new[] { "bodyType", "seats", "referencePrice", "extraSpecs" }, ignoreOrder: true);
        (await AsAdminAsync(() => Variants.GetListAsync(model.Id))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Price_and_contact_flag_both_or_neither_are_rejected()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var both = VariantInput();
        both.ContactForPrice = true;
        var neither = VariantInput(price: null);

        (await Should.ThrowAsync<AbpValidationException>(() => AsAdminAsync(() => Variants.CreateAsync(model.Id, both))))
            .ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "referencePrice" });
        (await Should.ThrowAsync<AbpValidationException>(() => AsAdminAsync(() => Variants.CreateAsync(model.Id, neither))))
            .ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "referencePrice" });
        (await AsAdminAsync(() => Variants.CreateAsync(model.Id, VariantInput(contact: true)))).ContactForPrice.ShouldBeTrue();
    }

    [Fact]
    public async Task Variant_name_is_unique_per_model_case_insensitively()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        await AsAdminAsync(() => Variants.CreateAsync(model.Id, VariantInput("1.5G")));

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAdminAsync(() => Variants.CreateAsync(model.Id, VariantInput(" 1.5g "))));

        ex.Code.ShouldBe(CatalogErrorCodes.Duplicate);
        ex.Data["field"].ShouldBe("name");
    }

    [Fact]
    public async Task Extra_specs_are_stored_as_jsonb_and_edits_are_persisted()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var input = VariantInput();
        input.ExtraSpecs = new() { new ExtraSpecDto { Group = "Kích thước", Name = "Dài", Value = "4425", Unit = "mm" } };
        var variant = await AsAdminAsync(() => Variants.CreateAsync(model.Id, input));

        input.ExtraSpecs = new() { new ExtraSpecDto { Group = "Kích thước", Name = "Dài", Value = "4430", Unit = "mm" } };
        input.ConcurrencyStamp = variant.ConcurrencyStamp;
        await AsAdminAsync(() => Variants.UpdateAsync(model.Id, variant.Id, input));

        var json = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select extra_specs::text as \"Value\" from cat_car_variants where id = {variant.Id}")
            .SingleAsync());
        using var doc = JsonDocument.Parse(json);
        doc.RootElement[0].GetProperty("group").GetString().ShouldBe("Kích thước");
        doc.RootElement[0].GetProperty("value").GetString().ShouldBe("4430");
        doc.RootElement[0].GetProperty("unit").GetString().ShouldBe("mm");
    }

    [Fact]
    public async Task Extra_spec_names_are_unique_within_a_group_and_200_is_the_limit()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var ok = VariantInput("A");
        ok.ExtraSpecs = Enumerable.Range(0, 200).Select(i => new ExtraSpecDto { Group = "G", Name = $"n{i}", Value = "v" }).ToList();
        (await AsAdminAsync(() => Variants.CreateAsync(model.Id, ok))).ExtraSpecs.Count.ShouldBe(200);

        var dup = VariantInput("B");
        dup.ExtraSpecs = new()
        {
            new ExtraSpecDto { Group = "G", Name = "Dài", Value = "1" },
            new ExtraSpecDto { Group = "G", Name = "dài", Value = "2" },
            new ExtraSpecDto { Group = "H", Name = "Dài", Value = "3" },   // nhóm khác: được
        };

        var ex = await Should.ThrowAsync<AbpValidationException>(() => AsAdminAsync(() => Variants.CreateAsync(model.Id, dup)));
        ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "extraSpecs[1].name" });
    }

    [Fact]
    public async Task Variant_put_with_a_stale_variant_stamp_gets_409()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var variant = await AsAdminAsync(() => Variants.CreateAsync(model.Id, VariantInput()));

        var first = VariantInput(price: 460_000_000m);
        first.ConcurrencyStamp = variant.ConcurrencyStamp;
        await AsAdminAsync(() => Variants.UpdateAsync(model.Id, variant.Id, first));

        var stale = VariantInput(price: 470_000_000m);
        stale.ConcurrencyStamp = variant.ConcurrencyStamp;
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => AsAdminAsync(() => Variants.UpdateAsync(model.Id, variant.Id, stale)));

        (await AsAdminAsync(() => Variants.GetAsync(model.Id, variant.Id))).ReferencePrice.ShouldBe(460_000_000);
    }

    [Fact]
    public async Task Variant_changes_are_audited_as_car_variant()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var variant = await AsAdminAsync(() => Variants.CreateAsync(model.Id, VariantInput()));

        var entries = await AuditEntriesAsync(variant.Id.ToString());
        entries.Single().EntityType.ShouldBe(CatalogEntityTypes.CarVariant);
        entries.Single().Action.ShouldBe(AuditActions.Create);
    }

    // ---------------------------------------------------------------- quyền

    [Fact]
    public void Content_methods_require_catalog_edit_and_transitions_require_catalog_publish()
    {
        static string? Policy(Type type, string method) =>
            type.GetMethod(method)!.GetCustomAttribute<AuthorizeAttribute>()?.Policy;

        foreach (var method in new[] { "GetListAsync", "GetAsync", "CreateAsync", "UpdateAsync", "GetPublishCheckAsync" })
        {
            Policy(typeof(CarModelAppService), method).ShouldBe(AppPermissions.CatalogEdit, method);
        }

        foreach (var method in new[] { "GetListAsync", "GetAsync", "CreateAsync", "UpdateAsync" })
        {
            Policy(typeof(CarVariantAppService), method).ShouldBe(AppPermissions.CatalogEdit, method);
        }

        Policy(typeof(CarModelAppService), "ChangeStatusAsync").ShouldBe(AppPermissions.CatalogPublish);
        Policy(typeof(CarVariantAppService), "ChangeStatusAsync").ShouldBe(AppPermissions.CatalogPublish);
        typeof(CarModelAppService).GetCustomAttribute<AuthorizeAttribute>().ShouldBeNull();   // không có Edit ở cấp class chồng lên Publish
    }
}
