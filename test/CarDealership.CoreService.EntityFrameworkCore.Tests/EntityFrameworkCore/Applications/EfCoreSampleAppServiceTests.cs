using CarDealership.CoreService.Samples;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore.Applications;

[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<CoreServiceEntityFrameworkCoreTestModule>
{

}
