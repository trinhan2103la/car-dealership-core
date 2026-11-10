using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Slugs;
using CarDealership.CoreService.EntityFrameworkCore.Catalog;
using CarDealership.CoreService.EntityFrameworkCore.Showrooms;
using CarDealership.CoreService.EntityFrameworkCore.Leads;
using CarDealership.CoreService.EntityFrameworkCore.Identity;
using CarDealership.CoreService.Identity.Login;
using CarDealership.CoreService.Identity.Sessions;
using CarDealership.CoreService.EntityFrameworkCore.Settings;
using CarDealership.CoreService.EntityFrameworkCore.Audit;
using CarDealership.CoreService.EntityFrameworkCore.Analytics;

namespace CarDealership.CoreService.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ConnectionStringName("Default")]
public class CoreServiceDbContext :
    AbpDbContext<CoreServiceDbContext>,
    IIdentityDbContext
{
    /* Add DbSet properties for your Aggregate Roots / Entities here. */

    #region Entities from the modules

    /* Notice: We only implemented IIdentityDbContext and replaced it for this
     * DbContext. This allows you to perform JOIN queries for the entities of
     * the Identity module over the repositories easily. If you need the same
     * for another module, implement that module's DbContext interface and add
     * a ReplaceDbContext attribute like IIdentityDbContext.
     */

    //Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    //Module Identity của dự án (idn_*)
    public DbSet<AdminSession> AdminSessions { get; set; }
    public DbSet<LoginAttempt> LoginAttempts { get; set; }

    //Module Audit của dự án (aud_*, append-only)
    public DbSet<AuditEntry> AuditEntries { get; set; }

    //Module Catalog (cat_*). CarVariant và VehicleImage cố ý KHÔNG có DbSet: chúng là entity con, chỉ truy cập qua CarModel.
    public DbSet<Brand> Brands { get; set; }
    public DbSet<CarModel> CarModels { get; set; }
    public DbSet<SlugHistory> SlugHistories { get; set; }

    #endregion

    public CoreServiceDbContext(DbContextOptions<CoreServiceDbContext> options)
        : base(options)
    {

    }

    /// <summary>
    /// Dịch vi phạm UNIQUE của Catalog (tên/Slug trùng, kể cả đồng thời) sang lỗi 409 theo trường.
    /// Lệch ConcurrencyStamp đã được ABP đổi thành <c>AbpDbConcurrencyException</c> trong <c>base</c> (cũng là 409).
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                                           && CatalogUniqueViolations.TryTranslate(pg) is not null)
        {
            throw CatalogUniqueViolations.TryTranslate(pg)!;
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureIdentity();

        /* Mỗi module của dự án có Configure riêng (hiện rỗng).
         * Giữ ở đây để khi thêm entity chỉ sửa đúng file module tương ứng. */
        builder.ConfigureCatalog();
        builder.ConfigureShowrooms();
        builder.ConfigureLeads();
        builder.ConfigureProjectIdentity();
        builder.ConfigureProjectSettings();
        builder.ConfigureProjectAudit();
        builder.ConfigureAnalytics();

        /* Configure your own tables/entities inside here */

        //builder.Entity<YourEntity>(b =>
        //{
        //    b.ToTable(CoreServiceConsts.DbTablePrefix + "YourEntities", CoreServiceConsts.DbSchema);
        //    b.ConfigureByConvention(); //auto configure for the base class props
        //    //...
        //});
    }
}
