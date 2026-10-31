using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.PermissionManagement;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Ngày 26 (Req 15.4, 15.14, 25.1): đúng 4 Role cố định, grant đúng ma trận, seed lại không đổi gì.
/// Ma trận mong đợi viết TAY ở đây, độc lập với RolePermissionMatrix của code, để test không tự khẳng định mình.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class RoleSeedTests : CoreServiceEntityFrameworkCoreTestBase
{
    public static IEnumerable<object[]> Matrix => new[]
    {
        new object[] { "SuperAdmin", new[]
            { "Catalog.Edit", "Catalog.Publish", "Leads.View", "Leads.Manage", "Showrooms.Manage",
              "Users.Manage", "Settings.Manage", "AuditLog.View", "Analytics.View" } },
        new object[] { "ContentEditor", new[] { "Catalog.Edit", "Catalog.Publish", "Settings.Manage" } },
        new object[] { "SalesStaff", new[] { "Leads.View", "Leads.Manage" } },
        new object[] { "Viewer", new[] { "Analytics.View" } },
    };

    private const int ExpectedGrantTotal = 9 + 3 + 2 + 1;

    [Fact]
    public async Task Seeds_exactly_four_static_roles_and_no_abp_admin_role()
    {
        var roles = await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await db.Roles.Select(r => new { r.Name, r.IsStatic }).ToListAsync();
        });

        roles.Select(r => r.Name).ShouldBe(
            new[] { "ContentEditor", "SalesStaff", "SuperAdmin", "Viewer" }, ignoreOrder: true);
        roles.ShouldAllBe(r => r.IsStatic);
        roles.ShouldNotContain(r => r.Name == "admin");
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Role_has_exactly_the_permissions_of_requirement_15_4(string role, string[] expected)
    {
        var granted = await GrantedPermissionsAsync(role);

        granted.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public async Task No_grants_exist_outside_the_matrix()
    {
        // Seeder mặc định của ABP để lại grant cho role "admin". Tổng phải đúng 15 và chỉ thuộc 4 Role của ta.
        var total = await CountAsync("AbpPermissionGrants");
        total.ShouldBe(ExpectedGrantTotal);

        var keys = await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await db.Database
                .SqlQueryRaw<string>("select distinct \"ProviderKey\" || '/' || \"ProviderName\" as \"Value\" from \"AbpPermissionGrants\"")
                .ToListAsync();
        });

        keys.Select(k => k.Split('/')[0]).ShouldBe(
            new[] { "ContentEditor", "SalesStaff", "SuperAdmin", "Viewer" }, ignoreOrder: true);
        keys.ShouldAllBe(k => k.EndsWith("/R"));
    }

    [Fact]
    public async Task Reseeding_twice_does_not_add_rows()
    {
        var tables = new[] { "AbpRoles", "AbpUsers", "AbpUserRoles", "AbpPermissionGrants" };
        var before = await CountsAsync(tables);

        await SeedAsync();
        await SeedAsync();

        (await CountsAsync(tables)).ShouldBe(before);
    }

    [Fact]
    public async Task Reseeding_keeps_hand_added_grants_and_restores_deleted_matrix_grants()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var permissions = GetRequiredService<IPermissionManager>();
            // chỉnh tay: thêm một grant ngoài ma trận, và gỡ một grant trong ma trận
            await permissions.SetAsync("Analytics.View", RolePermissionValueProvider.ProviderName, "ContentEditor", true);
            await permissions.SetAsync("Catalog.Publish", RolePermissionValueProvider.ProviderName, "ContentEditor", false);
        });

        await SeedAsync();

        var granted = await GrantedPermissionsAsync("ContentEditor");
        granted.ShouldContain("Analytics.View");     // không bị gỡ: seed không ghi đè chỉnh sửa tay
        granted.ShouldContain("Catalog.Publish");    // grant thuộc ma trận được bù lại
    }

    [Fact]
    public async Task Invalid_superadmin_configuration_stops_the_whole_seed_before_writing_anything()
    {
        // Req 25.7: cấu hình SuperAdmin sai thì dừng NGAY, không tạo gì. Dựng tình huống: thiếu role Viewer.
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            await db.Database.ExecuteSqlRawAsync("delete from \"AbpPermissionGrants\" where \"ProviderKey\" = 'Viewer'");
            await db.Database.ExecuteSqlRawAsync("delete from \"AbpRoles\" where \"Name\" = 'Viewer'");
        });

        await Should.ThrowAsync<InvalidOperationException>(() => WithUnitOfWorkAsync(async () =>
        {
            var brokenSuperAdmin = ActivatorUtilities.CreateInstance<SuperAdminSeeder>(
                ServiceProvider, Options.Create(new SuperAdminSeedOptions { Email = null, Password = null }));
            var contributor = ActivatorUtilities.CreateInstance<RoleSeedContributor>(ServiceProvider, brokenSuperAdmin);

            await contributor.SeedAsync(new DataSeedContext());
        }));

        // Role Viewer vẫn vắng mặt: contributor đã dừng trước khi tạo bất cứ Role nào.
        var roles = await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await db.Roles.Select(r => r.Name).ToListAsync();
        });
        roles.ShouldNotContain("Viewer");
    }

    private Task SeedAsync() =>
        WithUnitOfWorkAsync(() => GetRequiredService<IDataSeeder>().SeedAsync());

    private Task<List<string>> GrantedPermissionsAsync(string role) =>
        WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await db.Database
                .SqlQuery<string>($"select \"Name\" as \"Value\" from \"AbpPermissionGrants\" where \"ProviderName\" = 'R' and \"ProviderKey\" = {role}")
                .ToListAsync();
        });

    private async Task<Dictionary<string, int>> CountsAsync(IEnumerable<string> tables)
    {
        var result = new Dictionary<string, int>();
        foreach (var table in tables)
        {
            result[table] = await CountAsync(table);
        }

        return result;
    }

    // Tên bảng là hằng số trong test, không đến từ người dùng.
    private Task<int> CountAsync(string table) =>
        WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await db.Database
                .SqlQueryRaw<int>($"select count(*)::int as \"Value\" from \"{table}\"")
                .SingleAsync();
        });
}
