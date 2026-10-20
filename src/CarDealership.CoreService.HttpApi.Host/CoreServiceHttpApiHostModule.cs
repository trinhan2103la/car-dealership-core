using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Serilog;
using CarDealership.CoreService.EntityFrameworkCore;
using CarDealership.CoreService.MultiTenancy;
using Volo.Abp;
using Volo.Abp.AspNetCore.MultiTenancy;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;
using Volo.Abp.Swashbuckle;

namespace CarDealership.CoreService;

[DependsOn(
    typeof(CoreServiceHttpApiModule),
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMultiTenancyModule),
    typeof(CoreServiceApplicationModule),
    typeof(CoreServiceEntityFrameworkCoreModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpSwashbuckleModule)
)]
public class CoreServiceHttpApiHostModule : AbpModule
{
    // Lưu ý (Ngày 7): đã cắt Account + OpenIddict + LeptonX theme + AuthServer OAuth.
    // Xác thực thật (JWT ES256 service credential + session) sẽ được lắp ở Ngày 37.
    // Hiện chưa có authentication scheme nào; endpoint tạm thời chưa yêu cầu auth.

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        // Ngày 14: health checks. /health/live không check dependency; /health/ready check DB.
        context.Services.AddHealthChecks()
            .AddCheck<CarDealership.CoreService.Health.DatabaseHealthCheck>(
                "postgresql", tags: new[] { "ready" });

        // Ngày 10: KHÔNG bật auto/conventional API controller (Req 29.5).
        // Controller được viết tường minh dưới prefix riêng (/api/catalog/*, /api/app/*...).
        // Và ẩn mọi controller do module ABP tự đăng ký (Identity/Permission/Setting/Abp core)
        // để /api/identity/*, /api/abp/*... trả 404.
        Configure<MvcOptions>(options =>
        {
            options.Conventions.Add(new HideAbpModuleControllersConvention());
        });

        ConfigureCors(context, configuration);
        ConfigureSwaggerServices(context, configuration);
    }

    private static void ConfigureSwaggerServices(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddAbpSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "CoreService API", Version = "v1" });
            options.DocInclusionPredicate((docName, description) => true);
            options.CustomSchemaIds(type => type.FullName);
        });
    }

    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .WithOrigins(configuration["App:CorsOrigins"]?
                        .Split(",", StringSplitOptions.RemoveEmptyEntries)
                        .Select(o => o.RemovePostFix("/"))
                        .ToArray() ?? Array.Empty<string>())
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();

        // Ngày 13: Correlation_Id của ta (validate header, sinh mới nếu sai, đẩy vào LogContext).
        // Đặt TRƯỚC Serilog request logging để trường Correlation_Id có trong dòng log.
        app.UseMiddleware<CarDealership.CoreService.Middleware.CorrelationIdMiddleware>();
        // Ngày 11: một dòng log JSON cho mỗi request HTTP hoàn tất (Req 22.1).
        // CompactJsonFormatter cho @t (ISO 8601 có offset) và @l (level).
        // Serilog request logging tự thêm StatusCode, Elapsed (ms); ta thêm path.
        // service được enrich ở Program.cs; Correlation_Id thêm ở Ngày 13.
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("path", httpContext.Request.Path.Value ?? string.Empty);
                diagnosticContext.Set("statusCode", httpContext.Response.StatusCode);
            };
        });
        app.UseRouting();
        app.UseCors();
        // app.UseAuthentication();  // sẽ bật ở Ngày 37 khi có JWT ES256

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }
        app.UseUnitOfWork();
        app.UseAuthorization();

        // Ngày 14: health endpoints.
        // /health/live: process còn sống, không kiểm dependency (predicate loại hết check).
        app.UseHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false
        });
        // /health/ready: chạy check có tag "ready" (DB). Lỗi -> 503.
        app.UseHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        app.UseSwagger();
        app.UseAbpSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "CoreService API");
        });

        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }
}
