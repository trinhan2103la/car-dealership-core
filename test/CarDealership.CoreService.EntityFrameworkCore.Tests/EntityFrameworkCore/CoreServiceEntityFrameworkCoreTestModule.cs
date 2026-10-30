using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;

namespace CarDealership.CoreService.EntityFrameworkCore;

/// <summary>
/// Ngày 25: test EF Core chạy trên PostgreSQL 18 thật (Testcontainers) thay cho SQLite in-memory.
/// Container do <see cref="PostgresFixture"/> quản lý (một container cho cả collection test).
/// </summary>
[DependsOn(
    typeof(CoreServiceApplicationTestModule),
    typeof(CoreServiceEntityFrameworkCoreModule)
)]
public class CoreServiceEntityFrameworkCoreTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });
        Configure<SettingManagementOptions>(options =>
        {
            options.SaveStaticSettingsToDatabase = false;
            options.IsDynamicSettingStoreEnabled = false;
        });
        context.Services.AddAlwaysDisableUnitOfWorkTransaction();

        // Trỏ vào container Testcontainers. Ghi đè cả biến môi trường ConnectionStrings__Default
        // (CI đặt biến này cho Host.Tests), vì Configure của module này chạy sau ABP.
        // UseNpgsql() đã có sẵn ở CoreServiceEntityFrameworkCoreModule.
        Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = PostgresFixture.ConnectionString;
        });
    }

    public override void OnPreApplicationInitialization(ApplicationInitializationContext context)
    {
        // Mỗi test là một ABP application mới. Xóa sạch dữ liệu TRƯỚC khi ABP seed (seed chạy ở
        // OnApplicationInitialization), để test không thấy dữ liệu do test trước để lại.
        PostgresFixture.ResetDatabase();
    }
}