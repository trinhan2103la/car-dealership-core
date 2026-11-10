using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Tracing;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace CarDealership.CoreService.Audit;

/// <summary>
/// Ghi <see cref="AuditEntry"/>. Thay đổi nghiệp vụ dùng UoW hiện tại (insert không autoSave, nên cùng
/// SaveChanges và cùng transaction với thay đổi nghiệp vụ). Security event dùng UoW riêng.
/// </summary>
public class AuditTrail : IAuditTrail, ITransientDependency
{
    public const string SecurityEntityType = "AdminUser";
    public const string SecurityEntityIdWhenUnknown = "unknown";
    public const string NoCorrelationPrefix = "gen-";

    private readonly IRepository<AuditEntry, Guid> _entries;
    private readonly ICurrentUser _currentUser;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly IClock _clock;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public AuditTrail(
        IRepository<AuditEntry, Guid> entries,
        ICurrentUser currentUser,
        ICorrelationIdProvider correlationIdProvider,
        IClock clock,
        IGuidGenerator guidGenerator,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _entries = entries;
        _currentUser = currentUser;
        _correlationIdProvider = correlationIdProvider;
        _clock = clock;
        _guidGenerator = guidGenerator;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<bool> WriteAsync(
        string action,
        string entityType,
        string entityId,
        IReadOnlyDictionary<string, object?>? before,
        IReadOnlyDictionary<string, object?>? after)
    {
        var changes = AuditDiff.Compute(before, after);
        if (changes.Count == 0)
        {
            return false;
        }

        var entry = NewEntry(action, entityType, entityId, _currentUser.Id, AuditDiff.ToJson(changes));

        // Không autoSave và không requiresNew: insert nằm trong UoW hiện tại, cùng SaveChanges + transaction với
        // thay đổi nghiệp vụ. Audit lỗi thì cả hai cùng rollback (Req 17.4).
        await _entries.InsertAsync(entry);
        return true;
    }

    public async Task WriteSecurityEventAsync(string action, Guid? userId, string? reason)
    {
        var changes = string.IsNullOrEmpty(reason)
            ? AuditEntry.EmptyChanges
            : AuditDiff.ToJson(new[]
            {
                new AuditChange("reason", null, System.Text.Json.Nodes.JsonValue.Create(reason)),
            });

        var entry = NewEntry(
            action,
            SecurityEntityType,
            userId?.ToString() ?? SecurityEntityIdWhenUnknown,
            userId,
            changes);

        // UoW riêng, commit ngay: đăng nhập sai không có thay đổi nghiệp vụ nào để cùng rollback.
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        await _entries.InsertAsync(entry, autoSave: true);
        await uow.CompleteAsync();
    }

    private AuditEntry NewEntry(string action, string entityType, string entityId, Guid? actorId, string changesJson)
    {
        var correlationId = _correlationIdProvider.Get();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            // Ngoài HTTP (DbMigrator, job) không có header: sinh mã mới, vẫn khớp ^[A-Za-z0-9_-]{1,128}$.
            correlationId = NoCorrelationPrefix + Guid.NewGuid().ToString("N");
        }

        return new AuditEntry(
            _guidGenerator.Create(),
            _clock.Now.Kind == DateTimeKind.Utc ? _clock.Now : _clock.Now.ToUniversalTime(),
            actorId,
            action,
            entityType,
            entityId,
            correlationId,
            changesJson);
    }
}
