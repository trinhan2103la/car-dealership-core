using System.Collections.Generic;
using System.Linq;

namespace CarDealership.CoreService.Catalog.Publishing;

/// <summary>Kết quả hỏi máy trạng thái: được phép hay không, kèm các đích hợp lệ từ trạng thái hiện tại.</summary>
public sealed record TransitionResult(
    bool Allowed,
    CatalogStatus From,
    CatalogStatus To,
    IReadOnlyList<CatalogStatus> AllowedTargets);

/// <summary>
/// Máy trạng thái dùng chung cho CarModel và CarVariant (Req 10.1, 10.2). Đúng 5 chuyển, khai báo dạng DỮ LIỆU
/// (một bảng), không có <c>if</c> rải rác. Mọi chuyển khác bị từ chối, kể cả chuyển sang chính trạng thái hiện tại
/// và mọi chuyển từ Archived.
/// </summary>
public static class CatalogStatusMachine
{
    private static readonly IReadOnlySet<(CatalogStatus From, CatalogStatus To)> Allowed = new HashSet<(CatalogStatus, CatalogStatus)>
    {
        (CatalogStatus.Draft, CatalogStatus.Published),
        (CatalogStatus.Published, CatalogStatus.Hidden),
        (CatalogStatus.Hidden, CatalogStatus.Published),
        (CatalogStatus.Draft, CatalogStatus.Archived),
        (CatalogStatus.Hidden, CatalogStatus.Archived),
    };

    /// <summary>Số chuyển được phép (để test khóa cứng bằng 5).</summary>
    public static int AllowedTransitionCount => Allowed.Count;

    public static bool IsAllowed(CatalogStatus from, CatalogStatus to) => Allowed.Contains((from, to));

    /// <summary>Các đích hợp lệ từ <paramref name="from"/>, theo thứ tự enum. Rỗng với Archived.</summary>
    public static IReadOnlyList<CatalogStatus> AllowedTargets(CatalogStatus from) =>
        Allowed.Where(t => t.From == from).Select(t => t.To).OrderBy(s => s).ToList();

    /// <summary>
    /// Hỏi một chuyển. Không đổi gì: người gọi giữ nguyên trạng thái khi bị từ chối và trả
    /// <see cref="TransitionResult.AllowedTargets"/> cho client.
    /// </summary>
    public static TransitionResult TryTransition(CatalogStatus from, CatalogStatus to) =>
        new(IsAllowed(from, to), from, to, AllowedTargets(from));
}
