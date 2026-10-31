using CarDealership.CoreService.Samples;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore.Domains;

[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class EfCoreSampleDomainTests : SampleDomainTests<CoreServiceEntityFrameworkCoreTestModule>
{

}
