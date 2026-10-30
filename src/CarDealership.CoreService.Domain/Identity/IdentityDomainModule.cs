using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Module Identity của dự án (ADR-003): phần mở rộng trên ABP Identity. Ngày 24: chính sách mật khẩu 12–128.
/// DependsOn ABP Identity để provider/option của ta được nạp SAU của ABP và ghi đè được mặc định.
/// </summary>
[DependsOn(typeof(AbpIdentityDomainModule))]
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
    }
}
