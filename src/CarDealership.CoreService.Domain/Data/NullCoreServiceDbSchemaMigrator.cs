using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace CarDealership.CoreService.Data;

/* This is used if database provider does't define
 * ICoreServiceDbSchemaMigrator implementation.
 */
public class NullCoreServiceDbSchemaMigrator : ICoreServiceDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
