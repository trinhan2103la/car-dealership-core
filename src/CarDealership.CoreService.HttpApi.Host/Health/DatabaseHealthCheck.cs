using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using CarDealership.CoreService.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace CarDealership.CoreService.Health;

/// <summary>
/// Ngày 14 (Req 22): kiểm kết nối PostgreSQL cho /health/ready.
/// Không dùng package health-check bên thứ ba; gọi thẳng DbContext.CanConnectAsync.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck, ITransientDependency
{
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IDbContextProvider<CoreServiceDbContext> _dbContextProvider;

    public DatabaseHealthCheck(
        IUnitOfWorkManager uowManager,
        IDbContextProvider<CoreServiceDbContext> dbContextProvider)
    {
        _uowManager = uowManager;
        _dbContextProvider = dbContextProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var uow = _uowManager.Begin(requiresNew: true, isTransactional: false);
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            await uow.CompleteAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("PostgreSQL reachable")
                : HealthCheckResult.Unhealthy("PostgreSQL not reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL check failed", ex);
        }
    }
}
