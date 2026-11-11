using System;
using System.Collections.Generic;
using System.Linq;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Publishing;
using FsCheck;
using FsCheck.Fluent;
using Volo.Abp;
using Xunit;
using Check = FsCheck.Check;

namespace CarDealership.CoreService.Catalog.Properties;

/// <summary>
/// Property 1 (task 3.15, Req 10.1, 10.2): với MỌI cặp (s, t) kể cả s = t và Archived, chuyển thành công khi và chỉ khi
/// (s, t) thuộc bảng tham chiếu; bị từ chối thì trạng thái giữ nguyên và danh sách đích hợp lệ đúng bằng {t' | (s, t')}.
/// Bảng tham chiếu viết lại độc lập ở đây (không đọc từ <see cref="CatalogStatusMachine"/>).
/// </summary>
public class CatalogStatusMachineProperties
{
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

    private static readonly HashSet<(CatalogStatus, CatalogStatus)> Reference = new()
    {
        (CatalogStatus.Draft, CatalogStatus.Published),
        (CatalogStatus.Draft, CatalogStatus.Archived),
        (CatalogStatus.Published, CatalogStatus.Hidden),
        (CatalogStatus.Hidden, CatalogStatus.Published),
        (CatalogStatus.Hidden, CatalogStatus.Archived),
    };

    private static IEnumerable<CatalogStatus> ReferenceTargets(CatalogStatus from) =>
        Reference.Where(p => p.Item1 == from).Select(p => p.Item2).OrderBy(s => s);

    private static readonly Gen<CatalogStatus> AnyStatus = Gen.Elements(Enum.GetValues<CatalogStatus>());

    private static readonly Arbitrary<(CatalogStatus From, CatalogStatus To)> Pairs =
        (from s in AnyStatus from t in AnyStatus select (s, t)).ToArbitrary();

    private static Config Runs(int n) => Config.QuickThrowOnFailure.WithMaxTest(n);

    [Fact]
    public void State_machine_matches_the_reference_table_for_every_pair()
    {
        Check.One(Runs(500), Prop.ForAll(Pairs, p =>
        {
            var result = CatalogStatusMachine.TryTransition(p.From, p.To);
            return result.Allowed == Reference.Contains(p)
                   && result.From == p.From
                   && result.To == p.To
                   && result.AllowedTargets.SequenceEqual(ReferenceTargets(p.From));
        }));
    }

    /// <summary>Model đủ điều kiện xuất bản, đưa về trạng thái <paramref name="status"/> qua đường hợp lệ.</summary>
    private static CarModel ModelIn(CatalogStatus status)
    {
        var model = new CarModel(Guid.NewGuid(), Guid.NewGuid(), "Toyota", "Vios", "vios", new string('m', 60));
        var variant = model.AddVariant(Guid.NewGuid(), new CarVariantData(
            "1.5G", "Sedan", "Xăng", "Số tự động", 5, "1.5L", "106 hp", 458_000_000m, false, null));
        model.ChangeVariantStatus(variant.Id, CatalogStatus.Published);
        var image = model.AddImage(Guid.NewGuid(), "k1", "Ảnh mặt trước");
        model.SetCover(image.Id);

        switch (status)
        {
            case CatalogStatus.Published:
                model.ChangeStatus(CatalogStatus.Published, BrandStatus.Active, Now);
                break;
            case CatalogStatus.Hidden:
                model.ChangeStatus(CatalogStatus.Published, BrandStatus.Active, Now);
                model.ChangeStatus(CatalogStatus.Hidden, BrandStatus.Active, Now);
                break;
            case CatalogStatus.Archived:
                model.ChangeStatus(CatalogStatus.Archived, BrandStatus.Active, Now);
                break;
        }

        return model;
    }

    [Fact]
    public void CarModel_changes_status_iff_allowed_and_keeps_it_when_rejected()
    {
        Check.One(Runs(300), Prop.ForAll(Pairs, p =>
        {
            var model = ModelIn(p.From);
            try
            {
                model.ChangeStatus(p.To, BrandStatus.Active, Now);
                return Reference.Contains(p) && model.Status == p.To;
            }
            catch (BusinessException ex) when (ex.Code == CatalogErrorCodes.InvalidTransition)
            {
                return !Reference.Contains(p)
                       && model.Status == p.From
                       && (string)ex.Data["currentState"]! == p.From.ToString()
                       && ((string[])ex.Data["allowedTargets"]!).SequenceEqual(ReferenceTargets(p.From).Select(s => s.ToString()));
            }
        }));
    }

    [Fact]
    public void CarVariant_changes_status_iff_allowed_and_keeps_it_when_rejected()
    {
        Check.One(Runs(300), Prop.ForAll(Pairs, p =>
        {
            // Model Draft: quy tắc "variant Published cuối cùng của model Published" (Req 10.7) không can thiệp.
            var model = new CarModel(Guid.NewGuid(), Guid.NewGuid(), "Toyota", "Vios", "vios");
            var variant = model.AddVariant(Guid.NewGuid(), new CarVariantData(
                "1.5G", "Sedan", "Xăng", "Số tự động", 5, "1.5L", "106 hp", 458_000_000m, false, null));
            foreach (var step in PathTo(p.From))
            {
                model.ChangeVariantStatus(variant.Id, step);
            }

            try
            {
                model.ChangeVariantStatus(variant.Id, p.To);
                return Reference.Contains(p) && variant.Status == p.To;
            }
            catch (BusinessException ex) when (ex.Code == CatalogErrorCodes.InvalidTransition)
            {
                return !Reference.Contains(p) && variant.Status == p.From;
            }
        }));
    }

    private static IEnumerable<CatalogStatus> PathTo(CatalogStatus status) => status switch
    {
        CatalogStatus.Published => new[] { CatalogStatus.Published },
        CatalogStatus.Hidden => new[] { CatalogStatus.Published, CatalogStatus.Hidden },
        CatalogStatus.Archived => new[] { CatalogStatus.Archived },
        _ => Array.Empty<CatalogStatus>(),
    };
}
