using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;
using Volo.Abp.Identity;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Req 14.9: khi một user được lưu với IsActive = false (từ bất kỳ đường nào: màn hình quản trị sau này, script, seed),
/// thu hồi ngay mọi session của user đó và ghi security event. Đây là lớp phòng thủ thứ nhất; lớp thứ hai là
/// <see cref="SessionResolver"/> kiểm IsActive ở MỖI request, nên dù sự kiện này lỡ thì session cũ vẫn bị từ chối.
/// </summary>
public class UserDeactivatedSessionRevoker :
    ILocalEventHandler<EntityUpdatedEventData<IdentityUser>>,
    ITransientDependency
{
    private readonly SessionRevocation _revocation;
    private readonly ISecurityEventWriter _events;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;

    public UserDeactivatedSessionRevoker(
        SessionRevocation revocation,
        ISecurityEventWriter events,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock)
    {
        _revocation = revocation;
        _events = events;
        _unitOfWorkManager = unitOfWorkManager;
        _clock = clock;
    }

    public async Task HandleEventAsync(EntityUpdatedEventData<IdentityUser> eventData)
    {
        var user = eventData.Entity;
        if (user.IsActive)
        {
            return;
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);

        var revoked = await _revocation.RevokeAllAsync(user.Id, _clock.Now);
        if (revoked > 0)
        {
            await _events.WriteAsync(SecurityEventActions.SessionsRevoked, user.Email, user.Id, "UserInactive");
        }

        await uow.CompleteAsync();
    }
}
