using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Catalog.Publishing;

/// <summary>Đủ 16 cặp (4 × 4) của bảng chuyển trạng thái, so với bảng tham chiếu viết TAY ở đây (Req 10.1, 10.2).</summary>
public class CatalogStatusMachineTests
{
    private static readonly HashSet<(CatalogStatus, CatalogStatus)> Expected = new()
    {
        (CatalogStatus.Draft, CatalogStatus.Published),
        (CatalogStatus.Published, CatalogStatus.Hidden),
        (CatalogStatus.Hidden, CatalogStatus.Published),
        (CatalogStatus.Draft, CatalogStatus.Archived),
        (CatalogStatus.Hidden, CatalogStatus.Archived),
    };

    public static IEnumerable<object[]> AllPairs() =>
        from source in Enum.GetValues<CatalogStatus>()
        from target in Enum.GetValues<CatalogStatus>()
        select new object[] { source, target };

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Every_pair_matches_the_reference_table(CatalogStatus from, CatalogStatus to)
    {
        var result = CatalogStatusMachine.TryTransition(from, to);

        result.Allowed.ShouldBe(Expected.Contains((from, to)), $"{from} -> {to}");
        result.From.ShouldBe(from);
        result.To.ShouldBe(to);
        result.AllowedTargets.ShouldBe(Expected.Where(p => p.Item1 == from).Select(p => p.Item2).OrderBy(s => s));
    }

    [Fact]
    public void Exactly_five_transitions_are_allowed()
    {
        CatalogStatusMachine.AllowedTransitionCount.ShouldBe(5);
        AllPairs().Count(p => CatalogStatusMachine.IsAllowed((CatalogStatus)p[0], (CatalogStatus)p[1])).ShouldBe(5);
    }

    [Fact]
    public void Archived_is_terminal_and_self_transitions_are_rejected()
    {
        CatalogStatusMachine.AllowedTargets(CatalogStatus.Archived).ShouldBeEmpty();
        foreach (var status in Enum.GetValues<CatalogStatus>())
        {
            CatalogStatusMachine.IsAllowed(status, status).ShouldBeFalse();
        }

        CatalogStatusMachine.IsAllowed(CatalogStatus.Published, CatalogStatus.Archived).ShouldBeFalse();
        CatalogStatusMachine.IsAllowed(CatalogStatus.Published, CatalogStatus.Draft).ShouldBeFalse();
        CatalogStatusMachine.IsAllowed(CatalogStatus.Hidden, CatalogStatus.Draft).ShouldBeFalse();
    }
}
