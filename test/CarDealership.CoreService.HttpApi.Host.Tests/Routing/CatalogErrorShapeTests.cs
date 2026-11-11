using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CarDealership.CoreService.Catalog;
using CarDealership.CoreService.Catalog.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.Http;
using Xunit;

namespace CarDealership.CoreService.Routing;

/// <summary>
/// Data của <see cref="Volo.Abp.BusinessException"/> phải ra tới client trong <c>error.data</c> (lệch A): BFF đọc
/// <c>rules</c>, <c>currentState</c>, <c>allowedTargets</c>, <c>field</c>, <c>items</c> từ đó. Dùng converter THẬT của host
/// (cùng cấu hình AbpExceptionHandlingOptions), không cần DB.
/// </summary>
public class CatalogErrorShapeTests : IClassFixture<CoreServiceWebApplicationFactory>
{
    private readonly CoreServiceWebApplicationFactory _factory;

    public CatalogErrorShapeTests(CoreServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private JsonElement ErrorJson(System.Exception exception)
    {
        _ = _factory.Server;
        var converter = _factory.Services.GetRequiredService<IExceptionToErrorInfoConverter>();
        var info = converter.Convert(exception);
        var json = JsonSerializer.Serialize(new RemoteServiceErrorResponse(info), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return JsonDocument.Parse(json).RootElement.GetProperty("error");
    }

    private static JsonElement Data(JsonElement error)
    {
        error.TryGetProperty("data", out var data).ShouldBeTrue("ABP không đưa exception.Data ra error.data");
        return data;
    }

    [Fact]
    public void Publish_validation_failure_carries_rules_with_code_passed_and_message()
    {
        var error = ErrorJson(CatalogExceptions.PublishValidationFailed(new PublishValidationResult(
            new[] { PublishRuleCodes.SingleCoverImage, PublishRuleCodes.ImageAlt }, new[] { System.Guid.NewGuid() })));

        error.GetProperty("code").GetString().ShouldBe(CatalogErrorCodes.PublishValidationFailed);
        var rules = Data(error).GetProperty("rules").EnumerateArray().ToList();
        rules.Select(r => r.GetProperty("code").GetString()).ShouldBe(new[] { "SingleCoverImage", "ImageAlt" });
        rules.ShouldAllBe(r => r.GetProperty("passed").GetBoolean() == false);
        rules[0].GetProperty("message").GetString().ShouldBe("Cần đúng một ảnh cover");
        Data(error).GetProperty("invalidImageIds").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public void Invalid_transition_carries_current_state_and_allowed_targets()
    {
        var error = ErrorJson(CatalogExceptions.InvalidTransition(
            CatalogStatus.Draft, CatalogStatus.Hidden, new[] { CatalogStatus.Published, CatalogStatus.Archived }));

        Data(error).GetProperty("currentState").GetString().ShouldBe("Draft");
        Data(error).GetProperty("allowedTargets").EnumerateArray().Select(t => t.GetString())
            .ShouldBe(new[] { "Published", "Archived" });
    }

    [Fact]
    public void Duplicate_carries_the_field_and_blocked_brand_carries_items()
    {
        Data(ErrorJson(CatalogExceptions.Duplicate("slug"))).GetProperty("field").GetString().ShouldBe("slug");

        var items = Data(ErrorJson(CatalogExceptions.BrandHasPublishedCarModels(new[] { new BlockingCarModel("Vios", "vios") })))
            .GetProperty("items").EnumerateArray().ToList();
        items.Single().GetProperty("name").GetString().ShouldBe("Vios");
        items.Single().GetProperty("slug").GetString().ShouldBe("vios");
    }
}
