using System.Threading.Tasks;
using CarDealership.CoreService.Identity;
using Microsoft.AspNetCore.Http;

namespace CarDealership.CoreService.Middleware;

/// <summary>
/// Sau <c>UseAuthentication</c>: Service JWT có <c>sub</c>/<c>sid</c> thì tra phiên và gắn danh tính ABP (user id, Role) vào
/// <see cref="HttpContext.User"/> qua <see cref="AdminPrincipalEnricher"/>. Phiên không dùng được thì 401 ngay với cùng một
/// body chung (<see cref="SessionFailureDto"/>), không chạm controller. Không có sub/sid thì đi tiếp nguyên trạng.
/// </summary>
public class AdminPrincipalMiddleware
{
    private readonly RequestDelegate _next;

    public AdminPrincipalMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AdminPrincipalEnricher enricher)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var result = await enricher.EnrichAsync(context.User);
            if (result == AdminPrincipalResult.SessionInvalid)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new SessionFailureDto());
                return;
            }
        }

        await _next(context);
    }
}
