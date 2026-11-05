using System.Globalization;
using Volo.Abp.Identity.Settings;
using Volo.Abp.Settings;

namespace CarDealership.CoreService.Identity;


/// <summary>
/// Đổi GIÁ TRỊ MẶC ĐỊNH của setting RequiredLength từ 6 (ABP) thành 12.
/// Cần vì khi ABP áp cấu hình động (AbpDynamicOptionsManager.SetAsync) nó ghi đè IdentityOptions bằng giá trị
/// setting; nếu mặc định vẫn là 6 thì ngưỡng của ta tụt về 6 trên các đường đó.
/// </summary>
public class PasswordPolicySettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        var requiredLength = context.GetOrNull(IdentitySettingNames.Password.RequiredLength);

        if (requiredLength != null)
        {
            requiredLength.DefaultValue = PasswordPolicy.MinLength.ToString(CultureInfo.InvariantCulture);
        }

        // Khóa đăng nhập do LoginAttemptPolicy quyết định (một nguồn sự thật). Tắt lockout có sẵn của
        // ASP.NET Identity (5 lần/5 phút) để hai cơ chế không chạy song song với số liệu khác nhau.
        var lockoutAllowed = context.GetOrNull(IdentitySettingNames.Lockout.AllowedForNewUsers);

        if (lockoutAllowed != null)
        {
            lockoutAllowed.DefaultValue = "false";
        }
    }
}