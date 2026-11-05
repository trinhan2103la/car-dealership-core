using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace CarDealership.CoreService;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            // "Request starting ... {Path}{QueryString}" của framework ghi NGUYÊN query string (không qua LogRedaction),
            // nên một khóa/token gửi nhầm vào URL sẽ lọt vào log. Một dòng log mỗi request đã có từ UseSerilogRequestLogging
            // (path đã che query nhạy cảm), nên tắt dòng thô này.
            .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            // Log JSON có cấu trúc (CompactJsonFormatter). Service name = core-api.
            // Correlation_Id do CorrelationIdMiddleware đẩy vào LogContext.
            .Enrich.WithProperty("service", "core-api")
            // environment: đọc từ ASPNETCORE_ENVIRONMENT
            // vì logger được tạo trước khi có WebApplicationBuilder.
            .Enrich.WithProperty(
                "environment",
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")
            .WriteTo.Async(c => c.Console(new CompactJsonFormatter()))
            .WriteTo.Async(c => c.File(new CompactJsonFormatter(), "Logs/logs.json"))
            .CreateLogger();

        try
        {
            Log.Information("Starting CarDealership.CoreService.HttpApi.Host.");
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog();
            await builder.AddApplicationAsync<CoreServiceHttpApiHostModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
