using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore;

[CollectionDefinition(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class CoreServiceEntityFrameworkCoreCollection : ICollectionFixture<PostgresFixture>
{

}
