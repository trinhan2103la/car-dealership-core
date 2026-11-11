using System;
using System.Linq;
using CarDealership.CoreService.Catalog.Publishing;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Validation;
using Xunit;

namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>Bất biến của aggregate CarModel (không DB): biên dữ liệu, Publish_Validation, khoảng giá, search_text, ảnh.</summary>
public class CarModelTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string Description50 = new('m', 50);

    private static CarModel NewModel(string? description = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Toyota", "Vios", "vios", description ?? Description50);

    private static CarVariantData Variant(
        string name = "1.5G", decimal? price = 458_000_000m, bool contact = false,
        string? engine = "1.5L", string? power = "106 hp", int? seats = 5) =>
        new(name, "Sedan", "Xăng", "Số tự động", seats, engine, power, contact ? null : price, contact, null);

    private static System.Collections.Generic.Dictionary<string, object>[] Rules(BusinessException ex) =>
        (System.Collections.Generic.Dictionary<string, object>[])ex.Data["rules"]!;

    private static string[] RuleCodes(BusinessException ex) => Rules(ex).Select(r => (string)r["code"]).ToArray();

    private static CarModel PublishableModel()
    {
        var model = NewModel();
        var variant = model.AddVariant(Guid.NewGuid(), Variant());
        model.ChangeVariantStatus(variant.Id, CatalogStatus.Published);
        var image = model.AddImage(Guid.NewGuid(), "key-1", "Ảnh mặt trước");
        model.SetCover(image.Id);
        return model;
    }

    // ---------------------------------------------------------------- dữ liệu

    [Theory]
    [InlineData(0, true)]
    [InlineData(9999, true)]
    [InlineData(-1, false)]
    [InlineData(10_000, false)]
    public void Display_order_is_0_to_9999(int order, bool valid)
    {
        CarModel.Validate("Vios", "vios", null, order).Any.ShouldBe(!valid);
    }

    [Fact]
    public void Name_is_trimmed_and_limited_to_100()
    {
        CarModel.Validate(new string('a', 100), "a", null, 0).Any.ShouldBeFalse();
        CarModel.Validate(new string('a', 101), "a", null, 0).Members.ShouldBe(new[] { "name" });
        new CarModel(Guid.NewGuid(), Guid.NewGuid(), "B", "  Vios  ", "vios").Name.ShouldBe("Vios");
    }

    [Fact]
    public void New_model_is_draft()
    {
        NewModel().Status.ShouldBe(CatalogStatus.Draft);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(16, true)]
    [InlineData(17, false)]
    public void Seats_are_2_to_16(int seats, bool valid)
    {
        var model = NewModel();
        if (seats == 1)
        {
            Should.Throw<AbpValidationException>(() => model.AddVariant(Guid.NewGuid(), Variant(seats: 1)));
            return;
        }

        var act = () => model.AddVariant(Guid.NewGuid(), Variant(seats: seats));
        if (valid)
        {
            act().Seats.ShouldBe(seats);
        }
        else
        {
            Should.Throw<AbpValidationException>(act);
        }
    }

    [Fact]
    public void Fractional_price_is_rejected()
    {
        var ex = Should.Throw<AbpValidationException>(() => NewModel().AddVariant(Guid.NewGuid(), Variant(price: 458_000_000.5m)));
        ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "referencePrice" });
    }

    // ---------------------------------------------------------------- Publish_Validation

    [Fact]
    public void Publishable_model_publishes_and_records_published_at()
    {
        var model = PublishableModel();

        model.ChangeStatus(CatalogStatus.Published, BrandStatus.Active, Now);

        model.Status.ShouldBe(CatalogStatus.Published);
        model.PublishedAt.ShouldBe(Now);
        model.HasEverBeenPublished.ShouldBeTrue();
    }

    [Fact]
    public void Publish_validation_returns_every_violated_rule_at_once()
    {
        var model = NewModel(description: "ngắn");
        model.AddImage(Guid.NewGuid(), "k", "abc");   // alt 3 ký tự, không cover

        var ex = Should.Throw<BusinessException>(() => model.ChangeStatus(CatalogStatus.Published, BrandStatus.Inactive, Now));

        ex.Code.ShouldBe(CatalogErrorCodes.PublishValidationFailed);
        RuleCodes(ex).ShouldBe(PublishValidator.CarModelRuleCodes, ignoreOrder: true);
        Rules(ex).ShouldAllBe(r => (bool)r["passed"] == false && ((string)r["message"]).Length > 0);
        model.Status.ShouldBe(CatalogStatus.Draft);
        model.PublishedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(5000, true)]
    public void Description_length_rule_boundaries(int length, bool passes)
    {
        var model = PublishableModel();
        model.Update("Toyota", BrandStatus.Active, "Vios", "vios", new string('d', length), false, 0);

        model.CheckPublishable(BrandStatus.Active).Violations.Contains(PublishRuleCodes.DescriptionLength).ShouldBe(!passes);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(150, true)]
    public void Image_alt_rule_boundaries(int length, bool passes)
    {
        var model = PublishableModel();
        model.AddImage(Guid.NewGuid(), "k2", new string('a', length));

        var result = model.CheckPublishable(BrandStatus.Active);
        result.Violations.Contains(PublishRuleCodes.ImageAlt).ShouldBe(!passes);
        result.InvalidImageIds.Count.ShouldBe(passes ? 0 : 1);
    }

    [Fact]
    public void Alt_longer_than_150_is_rejected_when_saving()
    {
        Should.Throw<AbpValidationException>(() => NewModel().AddImage(Guid.NewGuid(), "k", new string('a', 151)));
    }

    [Fact]
    public void Zero_covers_fails_the_single_cover_rule()
    {
        var model = PublishableModel();
        model.ClearCover();

        model.CheckPublishable(BrandStatus.Active).Violations.ShouldBe(new[] { PublishRuleCodes.SingleCoverImage });
    }

    [Fact]
    public void Variant_publish_requires_all_six_key_specs()
    {
        var model = NewModel();
        var variant = model.AddVariant(Guid.NewGuid(), Variant(engine: null, power: null));

        var ex = Should.Throw<BusinessException>(() => model.ChangeVariantStatus(variant.Id, CatalogStatus.Published));

        RuleCodes(ex).ShouldBe(new[] { PublishRuleCodes.KeySpecComplete });
        variant.Status.ShouldBe(CatalogStatus.Draft);
    }

    [Fact]
    public void Invalid_transition_returns_current_status_and_allowed_targets()
    {
        var model = NewModel();

        var ex = Should.Throw<BusinessException>(() => model.ChangeStatus(CatalogStatus.Hidden, BrandStatus.Active, Now));

        ex.Code.ShouldBe(CatalogErrorCodes.InvalidTransition);
        ex.Data["currentState"].ShouldBe("Draft");
        ((string[])ex.Data["allowedTargets"]!).ShouldBe(new[] { "Published", "Archived" });
    }

    [Fact]
    public void Hiding_the_only_published_variant_of_a_published_model_is_rejected()
    {
        var model = PublishableModel();
        model.ChangeStatus(CatalogStatus.Published, BrandStatus.Active, Now);
        var only = model.Variants.Single();

        var ex = Should.Throw<BusinessException>(() => model.ChangeVariantStatus(only.Id, CatalogStatus.Hidden));

        ex.Code.ShouldBe(CatalogErrorCodes.LastPublishedVariant);
        only.Status.ShouldBe(CatalogStatus.Published);

        // Có variant Published thứ hai thì được ẩn variant đầu.
        var second = model.AddVariant(Guid.NewGuid(), Variant("2.0"));
        model.ChangeVariantStatus(second.Id, CatalogStatus.Published);
        model.ChangeVariantStatus(only.Id, CatalogStatus.Hidden);
        only.Status.ShouldBe(CatalogStatus.Hidden);
    }

    [Fact]
    public void Editing_a_published_model_into_an_invalid_state_is_rejected()
    {
        var model = PublishableModel();
        model.ChangeStatus(CatalogStatus.Published, BrandStatus.Active, Now);

        var ex = Should.Throw<BusinessException>(() => model.Update("Toyota", BrandStatus.Active, "Vios", "vios", "ngắn", false, 0));

        ex.Code.ShouldBe(CatalogErrorCodes.PublishValidationFailed);
    }

    [Fact]
    public void Editing_a_published_variant_into_an_invalid_state_is_rejected()
    {
        var model = PublishableModel();
        var variant = model.Variants.Single();

        var ex = Should.Throw<BusinessException>(() => model.UpdateVariant(variant.Id, Variant(power: null)));

        ex.Code.ShouldBe(CatalogErrorCodes.PublishValidationFailed);
    }

    // ---------------------------------------------------------------- dẫn xuất

    [Fact]
    public void Price_range_uses_published_variants_and_ignores_contact_for_price()
    {
        var model = NewModel();
        var a = model.AddVariant(Guid.NewGuid(), Variant("A", 500_000_000m));
        var b = model.AddVariant(Guid.NewGuid(), Variant("B", 700_000_000m));
        var c = model.AddVariant(Guid.NewGuid(), Variant("C", contact: true));
        model.AddVariant(Guid.NewGuid(), Variant("D", 100_000_000m));   // Draft: bỏ qua

        model.RecomputePriceRange();
        model.MinPublishedPrice.ShouldBeNull();

        model.ChangeVariantStatus(c.Id, CatalogStatus.Published);
        model.RecomputePriceRange();
        model.MinPublishedPrice.ShouldBeNull();   // chỉ có giá liên hệ
        model.MaxPublishedPrice.ShouldBeNull();

        model.ChangeVariantStatus(a.Id, CatalogStatus.Published);
        model.ChangeVariantStatus(b.Id, CatalogStatus.Published);
        model.RecomputePriceRange();
        model.MinPublishedPrice.ShouldBe(500_000_000);
        model.MaxPublishedPrice.ShouldBe(700_000_000);
    }

    [Fact]
    public void Search_text_is_brand_model_and_published_variant_names_without_accents()
    {
        var model = new CarModel(Guid.NewGuid(), Guid.NewGuid(), "VinFast", "VF Điện", "vf-dien", Description50);
        var v = model.AddVariant(Guid.NewGuid(), Variant("Bản Cao Cấp"));
        model.AddVariant(Guid.NewGuid(), Variant("Nháp"));

        model.SearchText.ShouldBe("vinfast vf dien");

        model.ChangeVariantStatus(v.Id, CatalogStatus.Published);
        model.RefreshDerived("VinFast");
        model.SearchText.ShouldBe("vinfast vf dien ban cao cap");

        model.RecomputeSearchText("Vin Fast Mới");
        model.SearchText.ShouldBe("vin fast moi vf dien ban cao cap");
    }

    // ---------------------------------------------------------------- ảnh

    [Fact]
    public void Thirty_images_is_the_limit_and_archived_models_take_no_images()
    {
        var model = NewModel();
        for (var i = 0; i < CatalogConsts.ImageMaxCount; i++)
        {
            model.AddImage(Guid.NewGuid(), $"k{i}", "Ảnh xe đẹp");
        }

        Should.Throw<BusinessException>(() => model.AddImage(Guid.NewGuid(), "k31", "Ảnh xe đẹp"))
            .Code.ShouldBe(CatalogErrorCodes.ImageLimitReached);
        model.Images.Select(i => i.DisplayOrder).ShouldBe(Enumerable.Range(0, 30));

        var archived = NewModel();
        archived.ChangeStatus(CatalogStatus.Archived, BrandStatus.Active, Now);
        Should.Throw<BusinessException>(() => archived.AddImage(Guid.NewGuid(), "k", "Ảnh xe đẹp"))
            .Code.ShouldBe(CatalogErrorCodes.CarModelArchived);
    }

    [Fact]
    public void Setting_a_second_cover_requires_clearing_the_first()
    {
        var model = NewModel();
        var a = model.AddImage(Guid.NewGuid(), "a", "Ảnh xe A");
        var b = model.AddImage(Guid.NewGuid(), "b", "Ảnh xe B");
        model.SetCover(a.Id);

        Should.Throw<InvalidOperationException>(() => model.SetCover(b.Id));

        model.ClearCover().ShouldBeTrue();
        model.SetCover(b.Id);
        model.Images.Count(i => i.IsCover).ShouldBe(1);
        b.IsCover.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- sắp xếp, alt, xóa ảnh (Ngày 90)

    private static (CarModel Model, VehicleImage A, VehicleImage B, VehicleImage C) ModelWithThreeImages()
    {
        var model = NewModel();
        var a = model.AddImage(Guid.NewGuid(), "a", "Ảnh xe A");
        var b = model.AddImage(Guid.NewGuid(), "b", "Ảnh xe B");
        var c = model.AddImage(Guid.NewGuid(), "c", "Ảnh xe C");
        return (model, a, b, c);
    }

    [Fact]
    public void Reorder_sets_contiguous_order_in_the_given_sequence()
    {
        var (model, a, b, c) = ModelWithThreeImages();

        model.ReorderImages(new[] { c.Id, a.Id, b.Id });

        c.DisplayOrder.ShouldBe(0);
        a.DisplayOrder.ShouldBe(1);
        b.DisplayOrder.ShouldBe(2);
    }

    public static TheoryData<string> BadOrders => new() { "missing", "duplicate", "unknown" };

    [Theory]
    [MemberData(nameof(BadOrders))]
    public void Reorder_rejects_missing_duplicate_or_unknown_ids(string kind)
    {
        var (model, a, b, c) = ModelWithThreeImages();
        Guid[] ids = kind switch
        {
            "missing" => new[] { a.Id, b.Id },
            "duplicate" => new[] { a.Id, b.Id, b.Id },
            _ => new[] { a.Id, b.Id, Guid.NewGuid() },
        };

        var ex = Should.Throw<AbpValidationException>(() => model.ReorderImages(ids));

        ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "imageIds" });
        new[] { a.DisplayOrder, b.DisplayOrder, c.DisplayOrder }.ShouldBe(new[] { 0, 1, 2 });
    }

    [Fact]
    public void Removing_an_image_keeps_order_contiguous()
    {
        var model = NewModel();
        var images = Enumerable.Range(0, 4).Select(i => model.AddImage(Guid.NewGuid(), $"k{i}", "Ảnh xe đẹp")).ToList();

        model.RemoveImage(images[1].Id, BrandStatus.Active);

        model.Images.Count.ShouldBe(3);
        model.Images.OrderBy(i => i.DisplayOrder).Select(i => i.Id)
            .ShouldBe(new[] { images[0].Id, images[2].Id, images[3].Id });
        model.Images.Select(i => i.DisplayOrder).OrderBy(o => o).ShouldBe(new[] { 0, 1, 2 });
    }

    [Fact]
    public void Removing_the_cover_of_a_draft_model_leaves_no_cover()
    {
        var model = NewModel();
        var cover = model.AddImage(Guid.NewGuid(), "a", "Ảnh xe A");
        model.AddImage(Guid.NewGuid(), "b", "Ảnh xe B");
        model.SetCover(cover.Id);

        model.RemoveImage(cover.Id, BrandStatus.Active);

        model.Images.Any(i => i.IsCover).ShouldBeFalse();
    }

    [Fact]
    public void Removing_the_cover_of_a_published_model_is_rejected()
    {
        var model = PublishableModel();
        model.ChangeStatus(CatalogStatus.Published, BrandStatus.Active, Now);
        var cover = model.Images.Single(i => i.IsCover);

        var ex = Should.Throw<BusinessException>(() => model.RemoveImage(cover.Id, BrandStatus.Active));

        ex.Code.ShouldBe(CatalogErrorCodes.PublishValidationFailed);
    }

    [Fact]
    public void Removing_an_unknown_image_is_not_found()
    {
        Should.Throw<Volo.Abp.Domain.Entities.EntityNotFoundException>(() =>
            NewModel().RemoveImage(Guid.NewGuid(), BrandStatus.Active));
    }

    [Fact]
    public void Alt_is_trimmed_and_limited_to_150()
    {
        var (model, a, _, _) = ModelWithThreeImages();

        model.UpdateImageAlt(a.Id, "  Ảnh nội thất  ", BrandStatus.Active);
        a.Alt.ShouldBe("Ảnh nội thất");

        var ex = Should.Throw<AbpValidationException>(() =>
            model.UpdateImageAlt(a.Id, new string('a', 151), BrandStatus.Active));
        ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "alt" });
    }

    [Fact]
    public void Short_alt_on_a_published_model_is_rejected()
    {
        var model = PublishableModel();
        model.ChangeStatus(CatalogStatus.Published, BrandStatus.Active, Now);
        var cover = model.Images.Single(i => i.IsCover);

        var ex = Should.Throw<BusinessException>(() => model.UpdateImageAlt(cover.Id, "abc", BrandStatus.Active));

        ex.Code.ShouldBe(CatalogErrorCodes.PublishValidationFailed);
    }
}
