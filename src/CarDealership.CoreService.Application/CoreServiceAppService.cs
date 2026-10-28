using System;
using System.Collections.Generic;
using System.Text;
using CarDealership.CoreService.Localization;
using Volo.Abp.Application.Services;

namespace CarDealership.CoreService;

/* Inherit your application services from this class.
 */
public abstract class CoreServiceAppService : ApplicationService
{
    protected CoreServiceAppService()
    {
        LocalizationResource = typeof(CoreServiceResource);
    }
}
