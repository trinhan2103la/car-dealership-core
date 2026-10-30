using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Xunit;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.EntityFrameworkCore;

/// <summary>
/// Ngày 25: chứng minh test EF Core chạy trên PostgreSQL 18 thật (container) và dữ liệu được reset giữa các test.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class PostgresIntegrationTests : CoreServiceEntityFrameworkCoreTestBase
{
    private const string ProbeUserName = "reset-probe";

    [Fact]
    public async Task Runs_against_postgres_18_in_the_testcontainer()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();

            // Đúng container của fixture, không phải Postgres của docker compose hay biến môi trường.
            dbContext.Database.GetConnectionString().ShouldBe(PostgresFixture.ConnectionString);

            var versionNum = await dbContext.Database
                .SqlQueryRaw<int>("select current_setting('server_version_num')::int as \"Value\"")
                .SingleAsync();
            versionNum.ShouldBeGreaterThanOrEqualTo(180000);
            versionNum.ShouldBeLessThan(190000);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Each_test_starts_from_a_clean_database(int run)
    {
        // Ba lần chạy cùng ghi một user. Không reset thì lần thứ hai trở đi thấy user của lần trước,
        // bất kể xUnit chạy theo thứ tự nào.
        await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<IIdentityUserRepository>();
            (await repository.FindByNormalizedUserNameAsync(ProbeUserName.ToUpperInvariant()))
                .ShouldBeNull($"Lần {run}: còn dữ liệu của test trước, reset không chạy.");

            var userManager = GetRequiredService<IdentityUserManager>();
            var user = new AbpIdentityUser(System.Guid.NewGuid(), ProbeUserName, ProbeUserName + "@example.com");
            (await userManager.CreateAsync(user, "Aa1!reset-probe")).Succeeded.ShouldBeTrue();
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var users = await GetRequiredService<IIdentityUserRepository>().GetListAsync();
            users.Count(u => u.UserName == ProbeUserName).ShouldBe(1);
            // Dữ liệu seed vẫn có (reset chạy TRƯỚC seed).
            users.ShouldContain(u => u.UserName == "admin");
        });
    }
}
