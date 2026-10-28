using CarDealership.CoreService.Samples;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore.Domains;

[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<CoreServiceEntityFrameworkCoreTestModule>
{

}
