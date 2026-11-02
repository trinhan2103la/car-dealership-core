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
using CarDealership.CoreService.Startup;
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
            // Ngày 37: mọi action dưới /api/jobs tự động dùng policy ServiceJobs (JWT scope=jobs).
            options.Conventions.Add(new CarDealership.CoreService.Auth.JobsRouteAuthorizationConvention());
        });

        // Ngày 37 (ADR-004): xác thực JWT ES256 của BFF + policy ServiceBff (mặc định) / ServiceJobs.
        CarDealership.CoreService.Auth.ServiceCredentialSetup.AddServiceCredential(context.Services);

        // Ngày 36: giá trị Edge key chỉ từ biến môi trường EDGE_KEY (đọc lười để cấu hình thêm sau, ví dụ trong test, vẫn có hiệu lực).
        Configure<CarDealership.CoreService.Middleware.EdgeKeyOptions>(options =>
        {
            options.Key = configuration[CarDealership.CoreService.Middleware.EdgeKeyOptions.VariableName];
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
        // Ngày 36 (Req 16.12): thiếu biến môi trường bắt buộc thì dừng NGAY, trước khi nhận request. Ném exception ->
        // Program.cs log Fatal (chỉ có TÊN biến) và trả mã thoát 1; /health/ready không bao giờ ở trạng thái sẵn sàng.
        RequiredConfiguration.EnsureValid(context.GetConfiguration());

        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        // Ngày 13: Correlation_Id của ta (validate header, sinh mới nếu sai, đẩy vào LogContext).
        // Đặt ĐẦU pipeline để mọi dòng log (kể cả /health/live) đều có Correlation_Id.
        app.UseMiddleware<CarDealership.CoreService.Middleware.CorrelationIdMiddleware>();
        // Ngày 36 (Req 28.12): Edge key là middleware kế tiếp, trước log request/health/localization/routing/Swagger.
        // Sai/thiếu -> 404 rỗng; chỉ GET /health/live được miễn.
        app.UseMiddleware<CarDealership.CoreService.Middleware.EdgeKeyMiddleware>();
        // Ngày 11: một dòng log JSON cho mỗi request HTTP hoàn tất (Req 22.1).
        // CompactJsonFormatter cho @t (ISO 8601 có offset) và @l (level).
        // Serilog request logging tự thêm StatusCode, Elapsed (ms); ta thêm path.
        // service được enrich ở Program.cs; Correlation_Id thêm ở Ngày 13.
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                // Ngày 11: che query nhạy cảm (token/password...) trước khi log; giữ path.
                var pathAndQuery = httpContext.Request.Path.Value + httpContext.Request.QueryString.Value;
                diagnosticContext.Set("path", CarDealership.CoreService.Logging.LogRedaction.RedactPathAndQuery(pathAndQuery));
                diagnosticContext.Set("statusCode", httpContext.Response.StatusCode);
                // Lưu ý: KHÔNG set Authorization/Cookie/X-Edge-Key vào log (xem LogRedaction.IsSensitiveHeader).
            };
        });

        // Ngày 14 (sửa): /health/live phải ĐỘC LẬP DB. Đặt TRƯỚC UseAbpRequestLocalization()
        // vì localization đọc Setting từ Postgres; nếu đặt sau, DB sập sẽ khiến /health/live 500.
        // predicate loại hết check -> chỉ xác nhận process còn sống, trả 200 body rỗng.
        app.UseHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false,
            // Ngày 14: body rỗng (mặc định health checks ghi "Healthy"). Chỉ cần 200.
            ResponseWriter = (context, _) => System.Threading.Tasks.Task.CompletedTask
        });

        app.UseAbpRequestLocalization();
        app.UseRouting();
        app.UseCors();
        // Ngày 37: xác thực JWT dịch vụ (bật lại; trước đó để chờ JWT ES256). Đứng sau Edge key, trước authorization.
        app.UseAuthentication();

        if (MultiTenancyConsts.IsEnabled)
        {
            app.UseMultiTenancy();
        }
        app.UseUnitOfWork();
        app.UseAuthorization();

        // Ngày 22: Swagger UI và swagger.json CHỈ bật ở Development (design: "Swagger UI chỉ bật
        // ở dev", Req 29.5). Ở môi trường khác (compose mặc định là Production) không phục vụ
        // qua HTTP để không phơi bề mặt API; bản OpenAPI cho CI xuất bằng CLI ở build time.
        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseAbpSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "CoreService API");
            });
        }

        app.UseAuditing();
        app.UseAbpSerilogEnrichers();

        // Ngày 22 (Req 29.5): CHỈ map attribute routing ([Route] tường minh). Không dùng
        // app.UseConfiguredEndpoints() vì ABP tự thêm route quy ước {controller=Home}/{action=Index}/{id?}
        // (cả bản có {area}) và Razor Pages: controller nào quên [Route] sẽ tự lộ ra ở /Ten/Action.
        // Nếu sau này module cần endpoint do ABP đăng ký (SignalR, dashboard...) thì thêm có chủ đích.
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();

            // Ngày 14: /health/ready kiểm DB (check có tag "ready"). Lỗi -> 503.
            // Ngày 37: chuyển từ middleware sang ENDPOINT để chịu fallback policy ServiceBff (design: /health/ready cần
            // Edge key + JWT; chỉ /health/live được miễn). Vẫn nằm sau UoW nên DbContext sẵn sàng cho DatabaseHealthCheck.
            endpoints.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready")
            });
        });
    }
}
