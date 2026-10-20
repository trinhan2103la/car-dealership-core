using CarDealership.CoreService.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace CarDealership.CoreService.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class CoreServiceController : AbpControllerBase
{
    protected CoreServiceController()
    {
        LocalizationResource = typeof(CoreServiceResource);
    }
}
