using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace CarDealership.CoreService.Catalog.Slugs;

/// <summary>
/// Quản lý lịch sử Slug khi đổi Slug (Req 8.13, 3.9). Ghi trong cùng UoW với thay đổi Slug.
/// Quy tắc phạm vi: Slug Brand duy nhất toàn hệ thống; Slug CarModel duy nhất trong Brand, nên hai Brand khác nhau
/// vẫn cùng có CarModel <c>vios</c>.
/// </summary>
public class SlugChangeManager : DomainService
{
    private readonly IRepository<SlugHistory, Guid> _history;

    public SlugChangeManager(IRepository<SlugHistory, Guid> history)
    {
        _history = history;
    }

    /// <summary>
    /// Slug MỚI không được trùng Slug CŨ của một entity KHÁC trong cùng phạm vi, nếu không URL cũ của entity đó sẽ bị
    /// "chiếm" và chuyển hướng không còn xác định (409). Entity lấy lại Slug cũ của chính mình thì được phép.
    /// </summary>
    public async Task EnsureAvailableAsync(string entityType, Guid entityId, Guid scopeId, string newSlug)
    {
        var taken = await _history.AnyAsync(h =>
            h.EntityType == entityType
            && h.ScopeId == scopeId
            && h.OldSlug == newSlug
            && h.EntityId != entityId);

        if (taken)
        {
            throw CatalogExceptions.Duplicate("slug");
        }
    }

    /// <summary>
    /// Ghi nhận <paramref name="oldSlug"/> sau khi đổi sang <paramref name="newSlug"/>. Nếu entity lấy lại một Slug cũ của
    /// mình thì xóa dòng lịch sử đó (Slug hiện tại không được vừa là Slug cũ).
    /// </summary>
    public async Task RecordChangeAsync(
        string entityType, Guid entityId, Guid scopeId, string oldSlug, string newSlug, DateTime nowUtc)
    {
        var reclaimed = await _history.GetListAsync(h =>
            h.EntityType == entityType
            && h.ScopeId == scopeId
            && h.OldSlug == newSlug
            && h.EntityId == entityId);

        if (reclaimed.Count > 0)
        {
            await _history.DeleteManyAsync(reclaimed, autoSave: true);
        }

        var exists = await _history.AnyAsync(h =>
            h.EntityType == entityType && h.ScopeId == scopeId && h.OldSlug == oldSlug);

        if (!exists)
        {
            await _history.InsertAsync(
                new SlugHistory(GuidGenerator.Create(), entityType, entityId, scopeId, oldSlug, nowUtc),
                autoSave: true);
        }
    }
}
