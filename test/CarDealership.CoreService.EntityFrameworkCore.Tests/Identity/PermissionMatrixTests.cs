using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Security.Claims;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Ngày 27 (Req 15.4, 15.14): kiểm MỌI ô của ma trận 4 Role × 9 Permission qua IPermissionChecker thật
/// trên PostgreSQL thật (grant do DbMigrator/seeder ghi), không gọi RolePermissionMatrix của code.
/// Ma trận mong đợi viết TAY ở đây, tách khỏi code sản xuất, để test không tự khẳng định mình.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class PermissionMatrixTests : CoreServiceEntityFrameworkCoreTestBase
{
    private static readonly string[] AllPermissions =
    {
        "Catalog.Edit", "Catalog.Publish", "Leads.View", "Leads.Manage", "Showrooms.Manage",
        "Users.Manage", "Settings.Manage", "AuditLog.View", "Analytics.View"
    };

    // Req 15.4, viết tay từng Role.
    private static readonly Dictionary<string, string[]> Expected = new()
    {
        ["SuperAdmin"] = AllPermissions,
        ["ContentEditor"] = new[] { "Catalog.Edit", "Catalog.Publish", "Settings.Manage" },
        ["SalesStaff"] = new[] { "Leads.View", "Leads.Manage" },
        ["Viewer"] = new[] { "Analytics.View" },
    };

    /// <summary>36 ô: (Role, Permission, được phép?).</summary>
    public static IEnumerable<object[]> Cells =>
        Expected.SelectMany(role => AllPermissions.Select(permission =>
            new object[] { role.Key, permission, role.Value.Contains(permission) }));

    /// <summary>Mọi cặp hai Role khác nhau (6 cặp).</summary>
    public static IEnumerable<object[]> RolePairs =>
        new[]
        {
            new object[] { "SuperAdmin", "ContentEditor" },
            new object[] { "SuperAdmin", "SalesStaff" },
            new object[] { "SuperAdmin", "Viewer" },
            new object[] { "ContentEditor", "SalesStaff" },
            new object[] { "ContentEditor", "Viewer" },
            new object[] { "SalesStaff", "Viewer" },
        };

    [Fact]
    public void Matrix_data_has_exactly_36_cells()
    {
        Cells.Count().ShouldBe(4 * 9);
        Cells.Count(c => (bool)c[2]).ShouldBe(9 + 3 + 2 + 1);
    }

    [Fact]
    public async Task All_nine_permissions_are_defined()
    {
        var definitions = GetRequiredService<IPermissionDefinitionManager>();

        foreach (var name in AllPermissions)
        {
            (await definitions.GetOrNullAsync(name)).ShouldNotBeNull($"Permission {name} chưa được khai báo");
        }
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public async Task Role_permission_cell_matches_requirement_15_4(string role, string permission, bool expected)
    {
        var granted = await IsGrantedAsync(new[] { role }, permission);

        granted.ShouldBe(expected, $"{role} × {permission}");
    }

    [Theory]
    [MemberData(nameof(RolePairs))]
    public async Task User_with_two_roles_gets_the_union_of_both_grants(string first, string second)
    {
        var union = Expected[first].Union(Expected[second]).ToHashSet();

        foreach (var permission in AllPermissions)
        {
            var granted = await IsGrantedAsync(new[] { first, second }, permission);

            granted.ShouldBe(union.Contains(permission), $"{first}+{second} × {permission}");
        }
    }

    [Fact]
    public async Task User_with_no_role_is_denied_every_permission()
    {
        foreach (var permission in AllPermissions)
        {
            (await IsGrantedAsync(Array.Empty<string>(), permission)).ShouldBeFalse(permission);
        }
    }

    [Fact]
    public async Task Role_name_that_is_not_seeded_is_denied_every_permission()
    {
        // Req 15.4: mọi Permission không được liệt kê bị từ chối, kể cả với Role lạ (vd. "admin" của ABP).
        foreach (var permission in AllPermissions)
        {
            (await IsGrantedAsync(new[] { "admin" }, permission)).ShouldBeFalse(permission);
        }
    }

    // CoreServiceTestBaseModule gọi AddAlwaysAllowAuthorization() nên IPermissionChecker trong test luôn trả true
    // (của template, để các test khác khỏi lo quyền). Test này phải dùng PermissionChecker THẬT của ABP.
    private IPermissionChecker RealChecker() =>
        ActivatorUtilities.CreateInstance<PermissionChecker>(ServiceProvider);

    private async Task<bool> IsGrantedAsync(IEnumerable<string> roles, string permission)
    {
        var accessor = GetRequiredService<ICurrentPrincipalAccessor>();
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, Guid.NewGuid().ToString()),
            new(AbpClaimTypes.UserName, "matrix-user"),
        };
        claims.AddRange(roles.Select(r => new Claim(AbpClaimTypes.Role, r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test", AbpClaimTypes.UserName, AbpClaimTypes.Role));

        using (accessor.Change(principal))
        {
            return await WithUnitOfWorkAsync(() =>
                RealChecker().IsGrantedAsync(permission));
        }
    }
}
