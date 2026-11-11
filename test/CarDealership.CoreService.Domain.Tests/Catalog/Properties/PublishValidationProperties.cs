using System;
using System.Collections.Generic;
using System.Linq;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Publishing;
using FsCheck;
using FsCheck.Fluent;
using Volo.Abp;
using Volo.Abp.Validation;
using Xunit;
using Check = FsCheck.Check;

namespace CarDealership.CoreService.Catalog.Properties;

/// <summary>
/// Property 3 (task 3.18, Req 10.3–10.5, 10.7, 10.10): với CarModel/CarVariant sinh ngẫu nhiên, tập lỗi bằng đúng tập quy tắc
/// bị vi phạm khi đánh giá từng quy tắc ĐỘC LẬP; publish thành công khi và chỉ khi tập đó rỗng; sau mọi chuỗi thao tác được
/// chấp nhận, CarModel Published luôn thỏa toàn bộ quy tắc.
/// </summary>
public class PublishValidationProperties
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    private static Config Runs(int n) => Config.QuickThrowOnFailure.WithMaxTest(n);

    // ---------------------------------------------------------------- mô tả cấu hình sinh ngẫu nhiên

    public sealed record VariantSpec(bool HasEngine, bool HasPower, bool Contact, bool Publish);

    public sealed record ModelSpec(
        int DescriptionLength,
        BrandStatus Brand,
        IReadOnlyList<int> AltLengths,
        int? CoverIndex,
        IReadOnlyList<VariantSpec> Variants)
    {
        public override string ToString() =>
            $"desc={DescriptionLength}, brand={Brand}, alts=[{string.Join(",", AltLengths)}], cover={CoverIndex?.ToString() ?? "-"}, " +
            $"variants=[{string.Join(" ", Variants.Select(v => $"{(v.HasEngine ? "E" : "e")}{(v.HasPower ? "P" : "p")}{(v.Contact ? "C" : "$")}{(v.Publish ? "+" : "-")}"))}]";
    }

    private static readonly Gen<VariantSpec> Variant =
        from engine in Gen.Frequency((4, Gen.Constant(true)), (1, Gen.Constant(false)))
        from power in Gen.Frequency((4, Gen.Constant(true)), (1, Gen.Constant(false)))
        from contact in Gen.Elements(true, false)
        from publish in Gen.Elements(true, false)
        select new VariantSpec(engine, power, contact, publish);

    private static readonly Gen<ModelSpec> Model =
        from description in Gen.Elements(0, 1, 49, 50, 51, 300, 5000)
        from brand in Gen.Frequency((4, Gen.Constant(BrandStatus.Active)), (1, Gen.Constant(BrandStatus.Inactive)))
        from imageCount in Gen.Choose(0, 4)
        from alts in Gen.Elements(0, 3, 4, 5, 6, 150).ArrayOf(imageCount)
        from cover in imageCount == 0
            ? Gen.Constant<int?>(null)
            : Gen.Frequency((4, Gen.Choose(0, imageCount - 1).Select(i => (int?)i)), (1, Gen.Constant<int?>(null)))
        from variantCount in Gen.Choose(0, 3)
        from variants in Variant.ArrayOf(variantCount)
        select new ModelSpec(description, brand, alts, cover, variants);

    private static string Alt(int length) => length == 0 ? string.Empty : new string('a', length);

    /// <summary>Dựng aggregate theo cấu hình (variant chỉ được publish nếu nó qua Publish_Validation của variant).</summary>
    private static CarModel Build(ModelSpec spec)
    {
        var model = new CarModel(Guid.NewGuid(), Guid.NewGuid(), "Toyota", "Vios", "vios",
            spec.DescriptionLength == 0 ? null : new string('m', spec.DescriptionLength));
        var i = 0;
        foreach (var v in spec.Variants)
        {
            var variant = model.AddVariant(Guid.NewGuid(), Data($"V{i++}", v));
            if (v.Publish && v.HasEngine && v.HasPower)
            {
                model.ChangeVariantStatus(variant.Id, CatalogStatus.Published);
            }
        }

        var images = spec.AltLengths.Select((alt, n) => model.AddImage(Guid.NewGuid(), $"k{n}", Alt(alt))).ToList();
        if (spec.CoverIndex is { } c)
        {
            model.SetCover(images[c].Id);
        }

        return model;
    }

    private static CarVariantData Data(string name, VariantSpec v) => new(
        name, "Sedan", "Xăng", "Số tự động", 5,
        v.HasEngine ? "1.5L" : null, v.HasPower ? "106 hp" : null,
        v.Contact ? null : 458_000_000m, v.Contact, null);

    /// <summary>Đánh giá TỪNG quy tắc từ cấu hình, không qua <see cref="PublishValidator"/>.</summary>
    private static HashSet<string> ExpectedModelViolations(ModelSpec spec)
    {
        var expected = new HashSet<string>();
        if (spec.Brand != BrandStatus.Active)
        {
            expected.Add(PublishRuleCodes.BrandActive);
        }

        if (spec.DescriptionLength is < 50 or > 5000)
        {
            expected.Add(PublishRuleCodes.DescriptionLength);
        }

        if (spec.CoverIndex is null)
        {
            expected.Add(PublishRuleCodes.SingleCoverImage);
        }

        if (spec.AltLengths.Any(a => a is < 5 or > 150))
        {
            expected.Add(PublishRuleCodes.ImageAlt);
        }

        if (!spec.Variants.Any(v => v.Publish && v.HasEngine && v.HasPower))
        {
            expected.Add(PublishRuleCodes.HasPublishedVariant);
        }

        return expected;
    }

    [Fact]
    public void CarModel_violations_are_exactly_the_independently_evaluated_rules()
    {
        Check.One(Runs(1000), Prop.ForAll(Model.ToArbitrary(), spec =>
        {
            var model = Build(spec);
            var result = model.CheckPublishable(spec.Brand);
            var expected = ExpectedModelViolations(spec);
            var badAltIds = model.Images.Where(img => img.Alt.Trim().Length is < 5 or > 150).Select(img => img.Id).ToHashSet();

            return (result.Violations.ToHashSet().SetEquals(expected)
                    && result.Violations.Count == expected.Count
                    && result.InvalidImageIds.ToHashSet().SetEquals(badAltIds)
                    && result.IsValid == (expected.Count == 0))
                .Label($"spec: {spec}; got [{string.Join(",", result.Violations)}], expected [{string.Join(",", expected)}]");
        }));
    }

    [Fact]
    public void Publishing_succeeds_iff_no_rule_is_violated_and_lists_every_rule_otherwise()
    {
        Check.One(Runs(1000), Prop.ForAll(Model.ToArbitrary(), spec =>
        {
            var model = Build(spec);
            var expected = ExpectedModelViolations(spec);
            try
            {
                model.ChangeStatus(CatalogStatus.Published, spec.Brand, Now);
                return (expected.Count == 0 && model.Status == CatalogStatus.Published).Label($"spec: {spec}");
            }
            catch (BusinessException ex) when (ex.Code == CatalogErrorCodes.PublishValidationFailed)
            {
                var codes = ((Dictionary<string, object>[])ex.Data["rules"]!).Select(r => (string)r["code"]).ToHashSet();
                return (codes.SetEquals(expected) && model.Status == CatalogStatus.Draft && model.PublishedAt is null)
                    .Label($"spec: {spec}; rules [{string.Join(",", codes)}]");
            }
        }));
    }

    [Fact]
    public void CarVariant_violations_are_exactly_the_independently_evaluated_rules()
    {
        Check.One(Runs(500), Prop.ForAll(Variant.ToArbitrary(), v =>
        {
            var model = new CarModel(Guid.NewGuid(), Guid.NewGuid(), "Toyota", "Vios", "vios");
            var variant = model.AddVariant(Guid.NewGuid(), Data("V", v));
            var expected = v.HasEngine && v.HasPower ? Array.Empty<string>() : new[] { PublishRuleCodes.KeySpecComplete };
            var result = PublishValidator.ValidateVariant(variant);
            return result.Violations.SequenceEqual(expected);
        }));
    }

    // ---------------------------------------------------------------- bất biến qua chuỗi thao tác (Req 10.10)

    public enum OpKind
    {
        Publish,
        Hide,
        Republish,
        SetAlt,
        RemoveImage,
        AddImage,
        MoveCover,
        HideVariant,
        PublishVariant,
        AddVariant,
        UpdateDescription,
    }

    public sealed record Op(OpKind Kind, int Pick, int Value)
    {
        public override string ToString() => $"{Kind}({Pick},{Value})";
    }

    private static readonly Gen<Op> AnyOp =
        from kind in Gen.Elements(Enum.GetValues<OpKind>())
        from pick in Gen.Choose(0, 5)
        from value in Gen.Elements(0, 3, 5, 40, 60, 150)
        select new Op(kind, pick, value);

    private static readonly Arbitrary<(ModelSpec Start, Op[] Ops)> Scenarios =
        (from start in Model
         from count in Gen.Choose(1, 25)
         from ops in AnyOp.ArrayOf(count)
         select (start with { Brand = BrandStatus.Active }, ops)).ToArbitrary();

    private static void Apply(CarModel model, Op op)
    {
        T At<T>(IReadOnlyCollection<T> items) => items.ElementAt(op.Pick % items.Count);
        var images = model.Images.OrderBy(i => i.DisplayOrder).ToList();
        var variants = model.Variants.ToList();
        switch (op.Kind)
        {
            case OpKind.Publish:
            case OpKind.Republish:
                model.ChangeStatus(CatalogStatus.Published, BrandStatus.Active, Now);
                break;
            case OpKind.Hide:
                model.ChangeStatus(CatalogStatus.Hidden, BrandStatus.Active, Now);
                break;
            case OpKind.SetAlt when images.Count > 0:
                model.UpdateImageAlt(At(images).Id, Alt(op.Value), BrandStatus.Active);
                break;
            case OpKind.RemoveImage when images.Count > 0:
                model.RemoveImage(At(images).Id, BrandStatus.Active);
                break;
            // Hai thao tác dưới gọi ĐÚNG như app service/VehicleImageManager gọi (không tự revalidate thêm).
            case OpKind.AddImage:
                model.AddImage(Guid.NewGuid(), Guid.NewGuid().ToString("N"), Alt(op.Value));
                break;
            case OpKind.MoveCover when images.Count > 0:
                model.ClearCover();
                model.SetCover(At(images).Id);
                break;
            case OpKind.HideVariant when variants.Count > 0:
                model.ChangeVariantStatus(At(variants).Id, CatalogStatus.Hidden);
                break;
            case OpKind.PublishVariant when variants.Count > 0:
                model.ChangeVariantStatus(At(variants).Id, CatalogStatus.Published);
                break;
            case OpKind.AddVariant:
                model.AddVariant(Guid.NewGuid(), Data($"N{Guid.NewGuid():N}"[..20], new VariantSpec(true, true, op.Pick % 2 == 0, true)));
                break;
            case OpKind.UpdateDescription:
                model.Update("Toyota", BrandStatus.Active, model.Name, model.Slug, op.Value == 0 ? null : new string('d', op.Value), false, 0);
                break;
        }
    }

    [Fact]
    public void A_published_model_satisfies_every_rule_after_any_sequence_of_accepted_operations()
    {
        Check.One(Runs(500), Prop.ForAll(Scenarios, scenario =>
        {
            // Mỗi thao tác là một request: bị từ chối thì UoW rollback, thay đổi dở dang trên aggregate bị bỏ
            // (CarModel.Update/UpdateImageAlt/RemoveImage sửa rồi mới revalidate, dựa vào rollback đó).
            // Mô phỏng bằng cách dựng lại aggregate từ cấu hình đầu + các thao tác ĐÃ được chấp nhận.
            var accepted = new List<Op>();
            CarModel Replay()
            {
                var m = Build(scenario.Start);
                foreach (var done in accepted)
                {
                    Apply(m, done);
                }

                return m;
            }

            var model = Replay();
            var trace = new List<string>();
            foreach (var op in scenario.Ops)
            {
                try
                {
                    Apply(model, op);
                    accepted.Add(op);
                    trace.Add(op + " ok");
                }
                catch (Exception ex) when (ex is BusinessException or AbpValidationException)
                {
                    trace.Add(op + " rejected");
                    model = Replay();
                }

                if (model.Status == CatalogStatus.Published && !model.CheckPublishable(BrandStatus.Active).IsValid)
                {
                    return false.Label($"start: {scenario.Start}; ops: {string.Join(" > ", trace)}; violations: " +
                                       string.Join(",", model.CheckPublishable(BrandStatus.Active).Violations));
                }
            }

            return true.ToProperty();
        }));
    }
}
