using System.Threading.Tasks;

namespace CarDealership.CoreService.Data;

public interface ICoreServiceDbSchemaMigrator
{
    Task MigrateAsync();
}
