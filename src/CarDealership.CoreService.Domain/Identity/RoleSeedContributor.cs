using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;
using AbpIdentityRole = Volo.Abp.Identity.IdentityRole;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Req 15.4, 25.1, 25.9: seed 4 Role cố định, grant theo ma trận, rồi SuperAdmin đầu tiên.
/// - Idempotent: chạy lại không tạo thêm bản ghi (khóa tự nhiên là tên Role / tên Permission / email).
/// - CHỈ THÊM grant còn thiếu, không bao giờ gỡ grant khác: không ghi đè chỉnh sửa tay.
/// - Kiểm cấu hình SuperAdmin TRƯỚC khi ghi gì (Req 25.7); cả lần chạy nằm trong một unit of work để
///   lỗi giữa chừng rollback toàn bộ (Req 25.9).
/// Gom vào một contributor để không phụ thuộc thứ tự chạy giữa các contributor.
/// </summary>
public class RoleSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionManager _permissionManager;
    private readonly SuperAdminSeeder _superAdminSeeder;

    public ILogger<RoleSeedContributor> Logger { get; set; } = NullLogger<RoleSeedContributor>.Instance;

    public RoleSeedContributor(
        IdentityRoleManager roleManager,
        IPermissionManager permissionManager,
        SuperAdminSeeder superAdminSeeder)
    {
        _roleManager = roleManager;
        _permissionManager = permissionManager;
        _superAdminSeeder = superAdminSeeder;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        _superAdminSeeder.ValidateOptions();

        foreach (var roleName in AppRoles.All)
        {
            await EnsureRoleAsync(roleName);

            foreach (var permission in RolePermissionMatrix.PermissionsOf(roleName))
            {
                await _permissionManager.SetAsync(
                    permission, RolePermissionValueProvider.ProviderName, roleName, isGranted: true);
            }
        }

        await _superAdminSeeder.SeedAsync();
    }

    private async Task EnsureRoleAsync(string roleName)
    {
        if (await _roleManager.FindByNameAsync(roleName) != null)
        {
            return;
        }

        var role = new AbpIdentityRole(System.Guid.NewGuid(), roleName)
        {
            IsStatic = true,   // Role cố định: ABP không cho đổi tên hay xóa.
            IsPublic = true
        };

        (await _roleManager.CreateAsync(role)).CheckErrors();
        Logger.LogInformation("Da tao role {Role}.", roleName);
    }
}
