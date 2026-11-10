using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>
/// Claim sub/sid của Service_Credential → danh tính ABP (user id + Role) để <c>[Authorize(Catalog.Edit)]</c> và actor của
/// Audit_Log chạy đúng trên request thật. Dùng PermissionChecker THẬT (module test mặc định cho phép mọi quyền).
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class AdminPrincipalEnricherTests : CatalogTestBase
{
    private AdminPrincipalEnricher Enricher => GetRequiredService<AdminPrincipalEnricher>();

    private static ClaimsPrincipal ServicePrincipal(Guid? sub, Guid? sid)
    {
        var claims = new System.Collections.Generic.List<Claim> { new("scope", "bff") };
        if (sub is not null)
        {
            claims.Add(new Claim(AdminIdentityClaims.UserId, sub.ToString()!));
        }

        if (sid is not null)
        {
            claims.Add(new Claim(AdminIdentityClaims.SessionId, sid.ToString()!));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private async Task<LoginResultDto> LoginAsync(string role)
    {
        var email = $"u{Guid.NewGuid():N}"[..16] + "@example.com";
        const string password = "Good-Passw0rd!2026";
        await WithUnitOfWorkAsync(async () =>
        {
            var users = GetRequiredService<IdentityUserManager>();
            var user = new Volo.Abp.Identity.IdentityUser(Guid.NewGuid(), email, email);
            (await users.CreateAsync(user, password)).Succeeded.ShouldBeTrue();
            (await users.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
        });

        return (await WithUnitOfWorkAsync(() => GetRequiredService<ISessionAppService>().LoginAsync(
            new LoginInput { Email = email, Password = password })))!;
    }

    private async Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permission)
    {
        var checker = ActivatorUtilities.CreateInstance<PermissionChecker>(ServiceProvider);
        return await WithUnitOfWorkAsync(() => checker.IsGrantedAsync(principal, permission));
    }

    [Fact]
    public async Task Service_call_without_admin_identity_is_left_untouched()
    {
        var principal = ServicePrincipal(null, null);

        (await Enricher.EnrichAsync(principal)).ShouldBe(AdminPrincipalResult.NoAdminIdentity);
        principal.Identities.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_session_is_rejected()
    {
        (await Enricher.EnrichAsync(ServicePrincipal(Guid.NewGuid(), Guid.NewGuid()))).ShouldBe(AdminPrincipalResult.SessionInvalid);
    }

    [Fact]
    public async Task Valid_session_adds_user_id_and_roles_so_real_permission_checks_pass()
    {
        var login = await LoginAsync(AppRoles.ContentEditor);
        var principal = ServicePrincipal(login.UserId, login.SessionId);

        (await Enricher.EnrichAsync(principal)).ShouldBe(AdminPrincipalResult.Enriched);

        principal.FindFirst(AbpClaimTypes.UserId)!.Value.ShouldBe(login.UserId.ToString());
        principal.FindAll(AbpClaimTypes.Role).Select(c => c.Value).ShouldBe(new[] { AppRoles.ContentEditor });
        (await IsGrantedAsync(principal, Identity.Permissions.AppPermissions.CatalogEdit)).ShouldBeTrue();
        (await IsGrantedAsync(principal, Identity.Permissions.AppPermissions.CatalogPublish)).ShouldBeTrue();
        (await IsGrantedAsync(principal, Identity.Permissions.AppPermissions.UsersManage)).ShouldBeFalse();
    }

    [Fact]
    public async Task Sales_staff_session_does_not_get_catalog_permissions()
    {
        var login = await LoginAsync(AppRoles.SalesStaff);
        var principal = ServicePrincipal(login.UserId, login.SessionId);

        await Enricher.EnrichAsync(principal);

        (await IsGrantedAsync(principal, Identity.Permissions.AppPermissions.CatalogEdit)).ShouldBeFalse();
    }

    [Fact]
    public async Task Revoked_session_is_rejected_immediately()
    {
        var login = await LoginAsync(AppRoles.ContentEditor);
        await WithUnitOfWorkAsync(() => GetRequiredService<ISessionAppService>().RevokeAsync(login.RefreshToken));

        (await Enricher.EnrichAsync(ServicePrincipal(login.UserId, login.SessionId))).ShouldBe(AdminPrincipalResult.SessionInvalid);
    }

    [Fact]
    public async Task Session_of_another_user_is_rejected()
    {
        var login = await LoginAsync(AppRoles.ContentEditor);

        (await Enricher.EnrichAsync(ServicePrincipal(Guid.NewGuid(), login.SessionId))).ShouldBe(AdminPrincipalResult.SessionInvalid);
    }
}
