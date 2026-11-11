using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>Chuyển trạng thái + Publish_Validation + Brand Inactive trên DB thật (Req 7.5, 10).</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class CatalogPublishingTests : CatalogTestBase
{
    [Fact]
    public async Task A_complete_model_is_published_and_audited_with_status_before_and_after()
    {
        var (_, model) = await CreatePublishedModelAsync();

        model.Status.ShouldBe(CatalogStatus.Published);
        model.PublishedAt.ShouldNotBeNull();
        model.MinPublishedPrice.ShouldBe(458_000_000);

        var transition = (await AuditEntriesAsync(model.Id.ToString())).Single(e => e.Action == AuditActions.ChangeStatus);
        using var changes = JsonDocument.Parse(transition.Changes);
        var status = changes.RootElement.EnumerateArray().Single(c => c.GetProperty("field").GetString() == "status");
        status.GetProperty("before").GetString().ShouldBe("Draft");
        status.GetProperty("after").GetString().ShouldBe("Published");
    }

    [Fact]
    public async Task Publish_check_lists_every_rule_without_changing_status()
    {
        var brand = await CreateBrandAsync(status: BrandStatus.Inactive);
        var model = await CreateModelAsync(brand.Id, description: null);

        var check = await AsAdminAsync(() => CarModels.GetPublishCheckAsync(model.Id));

        check.CanPublish.ShouldBeFalse();
        check.Rules.Select(r => r.Code).ShouldBe(Publishing.PublishValidator.CarModelRuleCodes);
        check.Rules.Where(r => !r.Passed).Select(r => r.Code).ShouldBe(new[]
        {
            PublishRuleCodes.BrandActive,
            PublishRuleCodes.DescriptionLength,
            PublishRuleCodes.SingleCoverImage,
            PublishRuleCodes.HasPublishedVariant,
        });
        (await AsAdminAsync(() => CarModels.GetAsync(model.Id))).Status.ShouldBe(CatalogStatus.Draft);
    }

    [Fact]
    public async Task Failed_publish_returns_all_violations_keeps_draft_and_writes_no_audit()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id, description: "ngắn");

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAdminAsync(
            () => CarModels.ChangeStatusAsync(model.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Published })));

        ex.Code.ShouldBe(CatalogErrorCodes.PublishValidationFailed);
        ((Dictionary<string, object>[])ex.Data["rules"]!).Length.ShouldBe(3);
        (await AsAdminAsync(() => CarModels.GetAsync(model.Id))).Status.ShouldBe(CatalogStatus.Draft);
        (await AuditEntriesAsync(model.Id.ToString())).ShouldAllBe(e => e.Action != AuditActions.ChangeStatus);
    }

    [Fact]
    public async Task Disallowed_transition_is_422_with_allowed_targets()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAdminAsync(
            () => CarModels.ChangeStatusAsync(model.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Hidden })));

        ex.Code.ShouldBe(CatalogErrorCodes.InvalidTransition);
        ((string[])ex.Data["allowedTargets"]!).ShouldBe(new[] { "Published", "Archived" });
    }

    [Fact]
    public async Task Hiding_the_last_published_variant_of_a_published_model_is_422()
    {
        var (_, model) = await CreatePublishedModelAsync();
        var variant = model.Variants.Single();

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAdminAsync(() => Variants.ChangeStatusAsync(
            model.Id, variant.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Hidden })));

        ex.Code.ShouldBe(CatalogErrorCodes.LastPublishedVariant);
        (await AsAdminAsync(() => Variants.GetAsync(model.Id, variant.Id))).Status.ShouldBe(CatalogStatus.Published);
    }

    [Fact]
    public async Task Editing_published_content_into_an_invalid_state_is_422_and_nothing_changes()
    {
        var (_, model) = await CreatePublishedModelAsync();

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAdminAsync(() => CarModels.UpdateAsync(model.Id, new UpdateCarModelInput
        {
            Name = model.Name, Slug = model.Slug, Description = "ngắn",
        })));

        ex.Code.ShouldBe(CatalogErrorCodes.PublishValidationFailed);
        (await AsAdminAsync(() => CarModels.GetAsync(model.Id))).Description.ShouldBe(LongDescription);
    }

    [Fact]
    public async Task Hidden_then_republished_keeps_working_and_archive_from_hidden_is_allowed()
    {
        var (_, model) = await CreatePublishedModelAsync();

        await AsAdminAsync(() => CarModels.ChangeStatusAsync(model.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Hidden }));
        await AsAdminAsync(() => CarModels.ChangeStatusAsync(model.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Published }));
        await AsAdminAsync(() => CarModels.ChangeStatusAsync(model.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Hidden }));
        var archived = await AsAdminAsync(() => CarModels.ChangeStatusAsync(model.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Archived }));

        archived.Status.ShouldBe(CatalogStatus.Archived);
    }

    // ---------------------------------------------------------------- Brand Inactive (Req 7.5)

    [Fact]
    public async Task Brand_cannot_go_inactive_while_it_has_published_models_and_the_422_lists_them_all()
    {
        var brand = await CreateBrandAsync();
        await CreatePublishedModelAsync("Vios", brand);
        await CreatePublishedModelAsync("Camry", brand);
        await CreateModelAsync(brand.Id, "Nháp");

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAdminAsync(() => Brands.ChangeStatusAsync(
            brand.Id, new ChangeBrandStatusInput { Status = BrandStatus.Inactive })));

        ex.Code.ShouldBe(CatalogErrorCodes.BrandHasPublishedCarModels);
        var listed = ((Dictionary<string, string>[])ex.Data["items"]!).Select(c => (c["name"], c["slug"])).ToList();
        listed.ShouldBe(new[] { ("Camry", "camry"), ("Vios", "vios") });
        (await AsAdminAsync(() => Brands.GetAsync(brand.Id))).Status.ShouldBe(BrandStatus.Active);
    }

    [Fact]
    public async Task Brand_without_published_models_goes_inactive_and_back_with_audit()
    {
        var brand = await CreateBrandAsync();
        await CreateModelAsync(brand.Id);

        await AsAdminAsync(() => Brands.ChangeStatusAsync(brand.Id, new ChangeBrandStatusInput { Status = BrandStatus.Inactive }));
        await AsAdminAsync(() => Brands.ChangeStatusAsync(brand.Id, new ChangeBrandStatusInput { Status = BrandStatus.Active }));

        (await AsAdminAsync(() => Brands.GetAsync(brand.Id))).Status.ShouldBe(BrandStatus.Active);
        (await AuditEntriesAsync(brand.Id.ToString())).Count(e => e.Action == AuditActions.ChangeStatus).ShouldBe(2);
    }
}
