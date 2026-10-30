using System;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Identity;
using Volo.Abp.Identity.Settings;
using Volo.Abp.Options;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Xunit;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Ngày 24 (Req 14.3, 28.9): mật khẩu phải dài 12–128 ký tự, kể cả khi mặc định của ABP khác.
/// Chạy qua IdentityUserManager thật + SQLite in-memory, tức là đúng đường mà đăng nhập/seed sẽ dùng.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class PasswordPolicyTests : CoreServiceEntityFrameworkCoreTestBase
{
    /// <summary>Mật khẩu dài đúng <paramref name="length"/> ký tự, có đủ hoa/thường/số/ký tự đặc biệt.</summary>
    private static string PasswordOfLength(int length) => ("Aa1!" + new string('x', Math.Max(0, length - 4)))[..length];

    private Task<IdentityResult> TryCreateUserAsync(string password)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var userManager = GetRequiredService<IdentityUserManager>();
            var name = "u" + Guid.NewGuid().ToString("N")[..12];
            var user = new AbpIdentityUser(Guid.NewGuid(), name, name + "@example.com");
            return await userManager.CreateAsync(user, password);
        });
    }

    private static string Codes(IdentityResult result) => string.Join(", ", result.Errors.Select(e => e.Code));

    [Theory]
    [InlineData(1, false)]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(64, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    [InlineData(300, false)]
    public async Task Password_length_must_be_between_12_and_128(int length, bool accepted)
    {
        var result = await TryCreateUserAsync(PasswordOfLength(length));

        result.Succeeded.ShouldBe(accepted,
            $"Độ dài {length}: mong đợi {(accepted ? "chấp nhận" : "từ chối")}. Lỗi: {Codes(result)}");

        if (!accepted)
        {
            result.Errors.Select(e => e.Code).ShouldContain(PasswordLengthValidator.ErrorCode);
        }
    }

    [Fact]
    public async Task Abp_composition_rules_are_kept()
    {
        // Quyết định Ngày 24: GIỮ luật hoa/thường/số/ký tự đặc biệt của ABP. 16 ký tự toàn chữ thường bị từ chối.
        var result = await TryCreateUserAsync("passwordpassword");

        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public void Identity_options_required_length_is_12()
    {
        // Đường thường (UserManager dùng trực tiếp) lấy IdentityOptions từ Configure<IdentityOptions> của module.
        GetRequiredService<IOptions<IdentityOptions>>().Value.Password.RequiredLength.ShouldBe(PasswordPolicy.MinLength);
    }

    [Fact]
    public async Task Setting_default_for_required_length_is_12()
    {
        // Nếu provider ghi đè mặc định không chạy (sai thứ tự module), giá trị này sẽ là "6".
        var value = await WithUnitOfWorkAsync(() =>
            GetRequiredService<ISettingProvider>().GetOrNullAsync(IdentitySettingNames.Password.RequiredLength));

        value.ShouldBe(PasswordPolicy.MinLength.ToString());
    }

    [Fact]
    public async Task Policy_holds_when_abp_dynamic_options_are_overridden_by_a_lower_stored_setting()
    {
        // Mô phỏng "lệch DB": ai đó đặt RequiredLength = 8 (khác mặc định để ABP không xóa hàng) trong AbpSettings,
        // rồi một đường nào đó gọi AbpDynamicOptionsManager.SetAsync() (như app service Identity của ABP).
        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<ISettingManager>()
                .SetGlobalAsync(IdentitySettingNames.Password.RequiredLength, "8");
        });

        var result = await WithUnitOfWorkAsync(async () =>
        {
            var options = (AbpDynamicOptionsManager<IdentityOptions>)GetRequiredService<IOptions<IdentityOptions>>();
            await options.SetAsync();

            // Điều kiện tiên quyết: ABP đã thật sự hạ ngưỡng xuống 8, nếu không test này không chứng minh gì.
            options.Value.Password.RequiredLength.ShouldBe(8);

            var userManager = GetRequiredService<IdentityUserManager>();
            var name = "u" + Guid.NewGuid().ToString("N")[..12];
            var user = new AbpIdentityUser(Guid.NewGuid(), name, name + "@example.com");
            return await userManager.CreateAsync(user, PasswordOfLength(11));
        });

        result.Succeeded.ShouldBeFalse("Validator phải giữ ngưỡng 12 dù ABP đã hạ RequiredLength xuống 8.");
        result.Errors.Select(e => e.Code).ShouldContain(PasswordLengthValidator.ErrorCode);
    }
}
