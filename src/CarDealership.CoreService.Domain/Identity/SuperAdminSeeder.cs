using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Req 25.6–25.8: tạo SuperAdmin đầu tiên từ biến môi trường.
/// - Thiếu/rỗng/ngắn hơn 12 ký tự: dừng, thông báo chỉ nêu TÊN BIẾN, không bao giờ in giá trị (25.7).
/// - Email đã tồn tại: giữ nguyên tài khoản, KHÔNG đổi mật khẩu (25.8).
/// Độ dài tối đa 128 và luật hoa/thường/số/ký tự đặc biệt do IdentityUserManager áp qua các validator (Ngày 24).
/// </summary>
public class SuperAdminSeeder : ITransientDependency
{
    private readonly SuperAdminSeedOptions _options;
    private readonly IdentityUserManager _userManager;

    public ILogger<SuperAdminSeeder> Logger { get; set; } = NullLogger<SuperAdminSeeder>.Instance;

    public SuperAdminSeeder(IOptions<SuperAdminSeedOptions> options, IdentityUserManager userManager)
    {
        _options = options.Value;
        _userManager = userManager;
    }

    /// <summary>Kiểm cấu hình mà KHÔNG chạm database. Ném InvalidOperationException nếu sai.</summary>
    public void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Email))
        {
            throw Stop(SuperAdminSeedOptions.EmailVariable, "bi thieu hoac rong");
        }

        if (!new EmailAddressAttribute().IsValid(_options.Email.Trim()))
        {
            throw Stop(SuperAdminSeedOptions.EmailVariable, "khong phai dia chi email hop le");
        }

        if (string.IsNullOrWhiteSpace(_options.Password))
        {
            throw Stop(SuperAdminSeedOptions.PasswordVariable, "bi thieu hoac rong");
        }

        if (_options.Password.Length < PasswordPolicy.MinLength)
        {
            throw Stop(SuperAdminSeedOptions.PasswordVariable, $"ngan hon {PasswordPolicy.MinLength} ky tu");
        }
    }

    public async Task SeedAsync()
    {
        ValidateOptions();

        var email = _options.Email!.Trim();

        if (await _userManager.FindByEmailAsync(email) != null)
        {
            Logger.LogInformation("SuperAdmin da ton tai, giu nguyen tai khoan va mat khau.");
            return;
        }

        var user = new AbpIdentityUser(Guid.NewGuid(), userName: email, email: email);
        user.SetEmailConfirmed(true);

        (await _userManager.CreateAsync(user, _options.Password!)).CheckErrors();
        (await _userManager.AddToRoleAsync(user, AppRoles.SuperAdmin)).CheckErrors();

        Logger.LogInformation("Da tao SuperAdmin ban dau.");
    }

    private static InvalidOperationException Stop(string variable, string reason) =>
        new($"Seed SuperAdmin dung lai: bien moi truong {variable} {reason}.");
}
