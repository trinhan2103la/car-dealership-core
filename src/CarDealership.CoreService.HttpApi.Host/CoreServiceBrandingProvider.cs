using Microsoft.Extensions.Localization;
using CarDealership.CoreService.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace CarDealership.CoreService;

[Dependency(ReplaceServices = true)]
public class CoreServiceBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<CoreServiceResource> _localizer;

    public CoreServiceBrandingProvider(IStringLocalizer<CoreServiceResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
