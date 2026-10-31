using CarDealership.CoreService.Samples;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore.Applications;

[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<CoreServiceEntityFrameworkCoreTestModule>
{

}
