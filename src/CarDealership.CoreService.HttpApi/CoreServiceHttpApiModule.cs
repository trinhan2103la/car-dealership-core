using System.Net;
using Localization.Resources.AbpUi;
using CarDealership.CoreService.Catalog;
using CarDealership.CoreService.Localization;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.HttpApi;
using Volo.Abp.SettingManagement;

namespace CarDealership.CoreService;

[DependsOn(
    typeof(CoreServiceApplicationContractsModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpPermissionManagementHttpApiModule),
    typeof(AbpSettingManagementHttpApiModule)
    )]
public class CoreServiceHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureLocalization();
        ConfigureCatalogErrorStatusCodes();
    }

    /// <summary>
    /// BusinessException mặc định của ABP trả 403, không đúng với Req 7, 8, 10: trùng là 409, vi phạm quy tắc nghiệp vụ là 422.
    /// 400 (validation), 404 (EntityNotFoundException) và 409 (AbpDbConcurrencyException) đã là mặc định của ABP.
    /// </summary>
    private void ConfigureCatalogErrorStatusCodes()
    {
        Configure<AbpExceptionHttpStatusCodeOptions>(options =>
        {
            options.Map(CatalogErrorCodes.Duplicate, HttpStatusCode.Conflict);
            options.Map(CatalogErrorCodes.InvalidTransition, HttpStatusCode.UnprocessableEntity);
            options.Map(CatalogErrorCodes.PublishValidationFailed, HttpStatusCode.UnprocessableEntity);
            options.Map(CatalogErrorCodes.LastPublishedVariant, HttpStatusCode.UnprocessableEntity);
            options.Map(CatalogErrorCodes.BrandHasPublishedCarModels, HttpStatusCode.UnprocessableEntity);
            options.Map(CatalogErrorCodes.CarModelArchived, HttpStatusCode.UnprocessableEntity);
            options.Map(CatalogErrorCodes.ImageLimitReached, HttpStatusCode.UnprocessableEntity);
        });
    }

    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<CoreServiceResource>()
                .AddBaseTypes(
                    typeof(AbpUiResource)
                );
        });
    }
}
