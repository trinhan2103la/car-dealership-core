using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CarDealership.CoreService.Auth;

/// <summary>
/// Ngày 37 (ADR-004, Req 15.3, 21, 28.12): Service_Credential = JWT ngắn hạn do BFF ký ES256; Core_API chỉ giữ
/// KHÓA CÔNG KHAI nên không thể giả mạo token. Hằng số dùng chung cho cấu hình, policy và test.
/// </summary>
public static class ServiceCredential
{
    public const string PublicKeyVariable = "SERVICE_JWT_PUBLIC_KEY";

    public const string Issuer = "bff";
    public const string Audience = "core-api";

    public const string ScopeClaim = "scope";
    public const string BffScope = "bff";
    public const string JobsScope = "jobs";

    public const string PolicyBff = "ServiceBff";
    public const string PolicyJobs = "ServiceJobs";

    public const string JobsRoutePrefix = "api/jobs";

    /// <summary>Token sống tối đa 60 giây: <c>exp − iat</c> lớn hơn là bị từ chối dù chữ ký đúng (Req 15.3).</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromSeconds(60);

    /// <summary>Lệch đồng hồ cho phép. Mặc định của JwtBearer là 5 phút, sẽ kéo token 60 giây sống tới 6 phút.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

    public const int MaxJtiLength = 128;
}

/// <summary>Nạp và kiểm khóa công khai ES256 từ biến môi trường (PEM hoặc base64 của SubjectPublicKeyInfo).</summary>
public static class ServiceCredentialKey
{
    /// <summary>
    /// Phân tích giá trị cấu hình. Chấp nhận PEM (xuống dòng thật hoặc ký tự "\n" literal, vì biến môi trường khó chứa
    /// nhiều dòng) hoặc base64 một dòng của SubjectPublicKeyInfo (DER).
    /// </summary>
    /// <returns>
    /// Khóa công khai P-256 hợp lệ, hoặc null kèm <paramref name="problem"/>. Thông báo lỗi KHÔNG bao giờ chứa nội dung khóa.
    /// </returns>
    public static ECDsa? TryParse(string? value, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            problem = "thiếu hoặc rỗng";
            return null;
        }

        ECDsa? ecdsa = null;
        try
        {
            ecdsa = ECDsa.Create();
            var text = value.Trim().Replace("\\n", "\n").Replace("\\r", string.Empty);

            if (text.Contains("-----BEGIN", StringComparison.Ordinal))
            {
                if (text.Contains("PRIVATE KEY", StringComparison.Ordinal))
                {
                    problem = "chứa khóa RIÊNG; Core_API chỉ được cấp khóa công khai";
                    ecdsa.Dispose();
                    return null;
                }

                ecdsa.ImportFromPem(text);
            }
            else
            {
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(text), out _);
            }

            var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
            if (parameters.Curve.Oid?.Value != ECCurve.NamedCurves.nistP256.Oid.Value
                && !string.Equals(parameters.Curve.Oid?.FriendlyName, "nistP256", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parameters.Curve.Oid?.FriendlyName, "ECDSA_P256", StringComparison.OrdinalIgnoreCase))
            {
                problem = "không phải khóa ECDSA P-256 (ES256)";
                ecdsa.Dispose();
                return null;
            }

            return ecdsa;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            problem = "không đọc được như khóa công khai ECDSA P-256 (PEM hoặc base64 SubjectPublicKeyInfo)";
            ecdsa?.Dispose();
            return null;
        }
    }
}

public static class ServiceCredentialSetup
{
    /// <summary>
    /// Đăng ký xác thực JWT cho request từ BFF và hai policy theo scope. Khóa công khai được nạp LƯỜI (lúc dùng lần đầu) từ
    /// <see cref="IConfiguration"/> cuối cùng; thiếu/sai khóa đã bị chặn từ lúc khởi động bởi <c>RequiredConfiguration</c>.
    /// </summary>
    public static IServiceCollection AddServiceCredential(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration, ILoggerFactory>(Configure);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ServiceCredential.PolicyBff, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ServiceCredential.ScopeClaim, ServiceCredential.BffScope));

            options.AddPolicy(ServiceCredential.PolicyJobs, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ServiceCredential.ScopeClaim, ServiceCredential.JobsScope));

            // Mặc định đóng: endpoint nào KHÔNG khai báo [Authorize]/[AllowAnonymous] đều cần JWT scope=bff
            // (gồm cả /health/ready, controller quên gắn attribute...). Muốn mở công khai phải ghi [AllowAnonymous] có chủ đích.
            options.FallbackPolicy = options.GetPolicy(ServiceCredential.PolicyBff);
        });

        return services;
    }

    private static void Configure(JwtBearerOptions options, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("CarDealership.CoreService.Auth.ServiceCredential");

        var key = ServiceCredentialKey.TryParse(configuration[ServiceCredential.PublicKeyVariable], out _);

        options.MapInboundClaims = false;            // giữ nguyên tên claim (scope, jti, cid), không đổi sang tên .NET
        options.RequireHttpsMetadata = false;        // không dùng metadata/discovery
        options.IncludeErrorDetails = false;         // 401 không kèm lý do chi tiết trong WWW-Authenticate
        options.SaveToken = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = ServiceCredential.Issuer,
            ValidateAudience = true,
            ValidAudience = ServiceCredential.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            // Chỉ ES256: chặn "none", HS256 (nhầm khóa công khai làm khóa HMAC), RS256... dù chữ ký có hợp lệ về mặt kỹ thuật.
            ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },
            IssuerSigningKey = key is null ? null : new ECDsaSecurityKey(key),
            ClockSkew = ServiceCredential.ClockSkew,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var failure = ValidateClaims(context.SecurityToken, context.Principal, DateTime.UtcNow);
                if (failure is not null)
                {
                    logger.LogWarning("Service JWT bị từ chối: {Reason}", failure);
                    context.Fail(failure);
                }

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                // Chỉ ghi LOẠI lỗi, không ghi token hay thông điệp (có thể chứa giá trị claim).
                logger.LogWarning("Service JWT không hợp lệ: {ErrorType}", context.Exception.GetType().Name);
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>Các ràng buộc ngoài những gì TokenValidationParameters kiểm. Trả null nếu hợp lệ, ngược lại mã lý do.</summary>
    internal static string? ValidateClaims(SecurityToken? token, System.Security.Claims.ClaimsPrincipal? principal, DateTime utcNow)
    {
        if (token is not JsonWebToken jwt || principal is null)
        {
            return "NotAJwt";
        }

        if (!jwt.TryGetPayloadValue<long>("iat", out var iat))
        {
            return "MissingIat";
        }

        if (!jwt.TryGetPayloadValue<long>("exp", out var exp))
        {
            return "MissingExp";
        }

        var lifetime = exp - iat;
        if (lifetime <= 0)
        {
            return "ExpNotAfterIat";
        }

        if (lifetime > (long)ServiceCredential.MaxLifetime.TotalSeconds)
        {
            return "LifetimeTooLong";
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(iat).UtcDateTime;
        if (issuedAt > utcNow + ServiceCredential.ClockSkew)
        {
            return "IssuedInTheFuture";
        }

        var jti = principal.FindFirst("jti")?.Value;
        if (string.IsNullOrWhiteSpace(jti) || jti.Length > ServiceCredential.MaxJtiLength)
        {
            return "MissingOrInvalidJti";
        }

        // Nhiều giá trị scope trong một token sẽ qua được CẢ HAI policy (ServiceBff lẫn ServiceJobs) nên bị loại ngay ở bước
        // xác thực (401). Thiếu scope hoặc scope lạ thì vẫn là token hợp lệ nhưng không đủ quyền: policy trả 403 (ADR-004).
        if (principal.FindAll(ServiceCredential.ScopeClaim).Count() > 1)
        {
            return "MultipleScopes";
        }

        return null;
    }
}

/// <summary>
/// Mọi action có route bắt đầu bằng <c>api/jobs</c> phải dùng policy <see cref="ServiceCredential.PolicyJobs"/>
/// (JWT scope=jobs). Nhờ vậy JWT scope=bff không gọi được job và JWT scope=jobs không gọi được endpoint nghiệp vụ.
/// </summary>
public sealed class JobsRouteAuthorizationConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var action in controller.Actions)
            {
                if (!action.Selectors.Any(selector => IsJobsRoute(controller, selector)))
                {
                    continue;
                }

                // Phải là METADATA của endpoint: một AuthorizeFilter của MVC không được UseAuthorization (middleware) nhìn thấy, nên
                // fallback ServiceBff vẫn áp dụng và chặn nhầm scope=jobs. Một action có MỘT selector thuộc /api/jobs thì cả action bị ràng buộc.
                foreach (var selector in action.Selectors)
                {
                    selector.EndpointMetadata.Add(new AuthorizeAttribute(ServiceCredential.PolicyJobs));
                }
            }
        }
    }

    private static bool IsJobsRoute(ControllerModel controller, SelectorModel selector)
    {
        var template = selector.AttributeRouteModel?.Template;
        var controllerTemplate = controller.Selectors
            .Select(s => s.AttributeRouteModel)
            .FirstOrDefault(a => a is not null)?.Template;

        var combined = AttributeRouteModel.CombineTemplates(controllerTemplate, template) ?? string.Empty;
        var normalized = combined.TrimStart('/');

        return normalized.Equals(ServiceCredential.JobsRoutePrefix, StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(ServiceCredential.JobsRoutePrefix + "/", StringComparison.OrdinalIgnoreCase);
    }
}
