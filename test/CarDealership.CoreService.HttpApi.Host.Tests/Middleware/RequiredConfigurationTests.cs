using System.Collections.Generic;
using System.Linq;
using CarDealership.CoreService.Startup;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Middleware.Tests;

/// <summary>Ngày 36 (Req 16.12): khởi động thiếu/sai biến bắt buộc thì dừng và chỉ nêu TÊN biến.</summary>
public class RequiredConfigurationTests
{
    private static IConfiguration Config(string? edgeKey)
    {
        var values = new Dictionary<string, string?>();
        if (edgeKey is not null)
        {
            values[EdgeKeyOptions.VariableName] = edgeKey;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Valid_key_has_no_problems()
    {
        RequiredConfiguration.Find(Config(new string('k', 32))).ShouldBeEmpty();
        Should.NotThrow(() => RequiredConfiguration.EnsureValid(Config(new string('k', 64))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_key_names_the_variable(string? value)
    {
        var problems = RequiredConfiguration.Find(Config(value));

        problems.ShouldHaveSingleItem().ShouldStartWith("EDGE_KEY");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(31)]
    public void Key_shorter_than_32_characters_is_rejected_without_echoing_it(int length)
    {
        // Chuỗi đặc trưng (không trùng chữ nào trong thông báo) để kiểm "không echo" cho chắc.
        var secret = "Qx7!vK2#pL9&wR4$mT6%jN8^cB1*dF3(gH5)"[..length];

        var ex = Should.Throw<System.InvalidOperationException>(() => RequiredConfiguration.EnsureValid(Config(secret)));

        ex.Message.ShouldContain("EDGE_KEY");
        ex.Message.ShouldNotContain(secret);
    }

    [Fact]
    public void Exactly_32_characters_is_the_minimum()
    {
        RequiredConfiguration.Find(Config(new string('k', 31))).ShouldNotBeEmpty();
        RequiredConfiguration.Find(Config(new string('k', 32))).ShouldBeEmpty();
    }

    [Fact]
    public void Error_message_never_contains_the_value_of_a_missing_variable_either()
    {
        var ex = Should.Throw<System.InvalidOperationException>(() => RequiredConfiguration.EnsureValid(Config(null)));

        ex.Message.ShouldContain("EDGE_KEY");
        ex.Message.Split(':').Last().ShouldNotContain("=");
    }
}
