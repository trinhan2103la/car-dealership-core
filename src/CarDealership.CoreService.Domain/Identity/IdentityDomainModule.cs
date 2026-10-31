using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.Identity;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Module Identity của dự án (ADR-003): phần mở rộng trên ABP Identity.
/// Ngày 24: chính sách mật khẩu 12–128. Ngày 26: 9 Permission, 4 Role, SuperAdmin đầu tiên.
/// DependsOn ABP Identity/PermissionManagement để cấu hình của ta được nạp SAU của ABP và ghi đè được mặc định.
/// </summary>
[DependsOn(
    typeof(AbpIdentityDomainModule),
    typeof(AbpPermissionManagementDomainIdentityModule))]
public class IdentityDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Đường thường (UserManager dùng trực tiếp): IdentityOptions lấy từ đây.
        // Độ phức tạp (hoa/thường/số/ký tự đặc biệt) GIỮ mặc định của ABP.
        Configure<IdentityOptions>(options =>
        {
            options.Password.RequiredLength = PasswordPolicy.MinLength;
        });

        // Giữ cả hai biên 12–128 bất kể cấu hình/setting (ASP.NET Identity không có độ dài tối đa).
        context.Services.AddTransient<IPasswordValidator<AbpIdentityUser>, PasswordLengthValidator>();

        // Req 25.6: email/mật khẩu SuperAdmin chỉ lấy từ biến môi trường, không có mặc định trong mã nguồn.
        var configuration = context.Services.GetConfiguration();
        Configure<SuperAdminSeedOptions>(options =>
        {
            options.Email = configuration[SuperAdminSeedOptions.EmailVariable];
            options.Password = configuration[SuperAdminSeedOptions.PasswordVariable];
        });

        // ABP mặc định tự seed role "admin", user "admin@abp.io" (mật khẩu mặc định) và grant cho role "admin".
        // Trái Req 15.4 (đúng 4 Role) và Req 25.6 (không mật khẩu mặc định). DataSeeder của ABP đọc danh sách
        // AbpDataSeedOptions.Contributors (KHÔNG phải DI), nên gỡ ở đây; RoleSeedContributor của ta thay thế.
        Configure<AbpDataSeedOptions>(options =>
        {
            options.Contributors.Remove(typeof(IdentityDataSeedContributor));
            options.Contributors.Remove(typeof(PermissionDataSeedContributor));
        });
    }
}
