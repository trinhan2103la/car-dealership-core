using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore;

[CollectionDefinition(CoreServiceTestConsts.CollectionDefinitionName)]
public class CoreServiceEntityFrameworkCoreCollection : ICollectionFixture<CoreServiceEntityFrameworkCoreFixture>
{

}
