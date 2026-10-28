using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CarDealership.CoreService.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Middleware.Tests;

/// <summary>
/// Ngày 13 (Req 22.2–22.5): chuẩn hóa Correlation_Id, chống log injection.
/// Dùng TestServer (pipeline ASP.NET thật, không DB/ABP) để OnStarting chạy đúng như production.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    private static readonly Regex Valid = new(@"^[A-Za-z0-9_-]{1,128}$");

    /// <summary>Dựng 1 host tối giản chỉ gắn CorrelationIdMiddleware; trả giá trị header response.</summary>
    private static async Task<string> RunAsync(string? incomingHeader)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseMiddleware<CorrelationIdMiddleware>();
                        app.Run(async ctx => await ctx.Response.WriteAsync("ok"));
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        if (incomingHeader != null)
        {
            // TryAddWithoutValidation để gửi được cả giá trị "bẩn" (space, newline...).
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, incomingHeader);
        }

        var res = await client.SendAsync(request);
        return res.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values)
            ? string.Join("", values)
            : string.Empty;
    }

    [Fact]
    public async Task Valid_header_is_preserved()
    {
        var result = await RunAsync("abc-123_XYZ");
        result.ShouldBe("abc-123_XYZ");
    }

    [Fact]
    public async Task Missing_header_generates_new_valid_id()
    {
        var result = await RunAsync(null);
        result.ShouldNotBeNullOrEmpty();
        Valid.IsMatch(result).ShouldBeTrue();
    }

    [Fact]
    public async Task Too_long_header_is_rejected_and_regenerated()
    {
        var longId = new string('a', 129);
        var result = await RunAsync(longId);
        result.ShouldNotBe(longId);
        Valid.IsMatch(result).ShouldBeTrue();
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("slash/here")]
    public async Task Invalid_chars_are_rejected_and_regenerated(string bad)
    {
        var result = await RunAsync(bad);
        result.ShouldNotBe(bad);
        Valid.IsMatch(result).ShouldBeTrue();
    }
}
