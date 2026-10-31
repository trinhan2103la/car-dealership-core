using System;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Identity;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Ngày 26 (Req 25.6–25.8): SuperAdmin ban đầu từ biến môi trường.
/// Seeder thật được dựng với options riêng cho từng ca; tài khoản do host test seed sẵn (SuperAdminEmail).
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class SuperAdminSeedTests : CoreServiceEntityFrameworkCoreTestBase
{
    private const string OtherEmail = "second-admin@example.com";
    private const string StrongPassword = "Another-Passw0rd!2026";

    private Task<TResult> InScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action) =>
        WithUnitOfWorkAsync(() => action(ServiceProvider));

    private static SuperAdminSeeder NewSeeder(IServiceProvider sp, string? email, string? password) =>
        ActivatorUtilities.CreateInstance<SuperAdminSeeder>(
            sp, Options.Create(new SuperAdminSeedOptions { Email = email, Password = password }));

    [Fact]
    public async Task Initial_superadmin_exists_with_role_and_working_password()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var userManager = GetRequiredService<IdentityUserManager>();
            var user = await userManager.FindByEmailAsync(CoreServiceTestConsts.SuperAdminEmail);

            user.ShouldNotBeNull();
            user.IsActive.ShouldBeTrue();
            (await userManager.GetRolesAsync(user)).ShouldBe(new[] { "SuperAdmin" });
            (await userManager.CheckPasswordAsync(user, CoreServiceTestConsts.SuperAdminPassword)).ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Existing_email_keeps_its_password_when_seeded_again_with_another_password()
    {
        // Req 25.8
        await WithUnitOfWorkAsync(async () =>
        {
            await NewSeeder(ServiceProvider, CoreServiceTestConsts.SuperAdminEmail, StrongPassword).SeedAsync();
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var userManager = GetRequiredService<IdentityUserManager>();
            var user = (await userManager.FindByEmailAsync(CoreServiceTestConsts.SuperAdminEmail))!;

            (await userManager.CheckPasswordAsync(user, CoreServiceTestConsts.SuperAdminPassword)).ShouldBeTrue();
            (await userManager.CheckPasswordAsync(user, StrongPassword)).ShouldBeFalse();
        });
    }

    [Theory]
    [InlineData(null, "Aa1!Aa1!Aa1!Aa1!", SuperAdminSeedOptions.EmailVariable)]
    [InlineData("", "Aa1!Aa1!Aa1!Aa1!", SuperAdminSeedOptions.EmailVariable)]
    [InlineData("khong-phai-email", "Aa1!Aa1!Aa1!Aa1!", SuperAdminSeedOptions.EmailVariable)]
    [InlineData(OtherEmail, null, SuperAdminSeedOptions.PasswordVariable)]
    [InlineData(OtherEmail, "", SuperAdminSeedOptions.PasswordVariable)]
    [InlineData(OtherEmail, "            ", SuperAdminSeedOptions.PasswordVariable)]
    [InlineData(OtherEmail, "Aa1!Aa1!Aa1", SuperAdminSeedOptions.PasswordVariable)]   // 11 ký tự, Req 25.7
    public async Task Invalid_configuration_stops_naming_the_variable_and_creates_nobody(
        string? email, string? password, string variable)
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            InScopeAsync(async sp => { await NewSeeder(sp, email, password).SeedAsync(); return 0; }));

        ex.Message.ShouldContain(variable);
        if (!string.IsNullOrWhiteSpace(password))
        {
            ex.Message.ShouldNotContain(password);   // không bao giờ in giá trị
        }

        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(OtherEmail)).ShouldBeNull();
        });
    }

    [Fact]
    public async Task Password_that_fails_complexity_is_rejected_without_leaking_it_and_creates_nobody()
    {
        // 16 ký tự nhưng toàn chữ thường: qua kiểm độ dài, rớt luật hoa/số/ký tự đặc biệt (quyết định Ngày 24).
        const string weak = "abcdefghijklmnop";

        var ex = await Should.ThrowAsync<Exception>(() =>
            InScopeAsync(async sp => { await NewSeeder(sp, OtherEmail, weak).SeedAsync(); return 0; }));

        ex.ToString().ShouldNotContain(weak);

        await WithUnitOfWorkAsync(async () =>
        {
            (await GetRequiredService<IdentityUserManager>().FindByEmailAsync(OtherEmail)).ShouldBeNull();
        });
    }

    [Fact]
    public async Task Strong_configuration_creates_a_second_superadmin_account()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await NewSeeder(ServiceProvider, OtherEmail, StrongPassword).SeedAsync();
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var userManager = GetRequiredService<IdentityUserManager>();
            var user = await userManager.FindByEmailAsync(OtherEmail);

            user.ShouldNotBeNull();
            (await userManager.IsInRoleAsync(user, "SuperAdmin")).ShouldBeTrue();
            (await userManager.CheckPasswordAsync(user, StrongPassword)).ShouldBeTrue();
        });
    }
}
