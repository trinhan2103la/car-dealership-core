using System.Collections.Generic;
using System.Linq;
using CarDealership.CoreService.Startup;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Middleware.Tests;

/// <summary>Khởi động thiếu/sai biến bắt buộc thì dừng và chỉ nêu TÊN biến.</summary>
public class RequiredConfigurationTests
{
    private const string ValidPassphrase = "passphrase-16byte";

    private static IConfiguration Config(
        string? edgeKey,
        string? publicKey = null,
        bool useValidPublicKey = true,
        string? passphrase = ValidPassphrase)
    {
        var values = new Dictionary<string, string?>();
        if (edgeKey is not null)
        {
            values[EdgeKeyOptions.VariableName] = edgeKey;
        }

        // Khóa công khai ES256 cũng là biến bắt buộc. Mặc định cấp khóa hợp lệ để các ca Edge key không bị lẫn.
        if (publicKey is not null)
        {
            values[Auth.ServiceCredential.PublicKeyVariable] = publicKey;
        }
        else if (useValidPublicKey)
        {
            values[Auth.ServiceCredential.PublicKeyVariable] = TestServiceJwt.PublicKeyPem;
        }

        // Passphrase mã hóa cũng bắt buộc; mặc định cấp giá trị hợp lệ để không lẫn với các ca khác.
        if (passphrase is not null)
        {
            values[StringEncryptionOptions.VariableName] = passphrase;
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

    // ---------- SERVICE_JWT_PUBLIC_KEY ----------

    [Fact]
    public void Missing_public_key_names_the_variable()
    {
        var problems = RequiredConfiguration.Find(Config(new string('k', 32), useValidPublicKey: false));

        problems.ShouldHaveSingleItem().ShouldStartWith("SERVICE_JWT_PUBLIC_KEY");
    }

    [Fact]
    public void Both_variables_missing_are_both_reported_in_one_message()
    {
        var ex = Should.Throw<System.InvalidOperationException>(() =>
            RequiredConfiguration.EnsureValid(Config(null, useValidPublicKey: false)));

        ex.Message.ShouldContain("EDGE_KEY");
        ex.Message.ShouldContain("SERVICE_JWT_PUBLIC_KEY");
    }

    [Fact]
    public void A_private_key_is_refused_without_echoing_any_key_material()
    {
        var privatePem = TestServiceJwt.Key.ExportPkcs8PrivateKeyPem();

        var ex = Should.Throw<System.InvalidOperationException>(() =>
            RequiredConfiguration.EnsureValid(Config(new string('k', 32), privatePem)));

        ex.Message.ShouldContain("SERVICE_JWT_PUBLIC_KEY");
        ex.Message.ShouldContain("RIÊNG");
        ex.Message.ShouldNotContain("BEGIN");
        ex.Message.ShouldNotContain(privatePem.Split('\n')[1].Trim());
    }

    [Fact]
    public void Garbage_p384_and_rsa_keys_are_refused()
    {
        using var p384 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        using var rsa = System.Security.Cryptography.RSA.Create(2048);

        foreach (var bad in new[] { "khong-phai-khoa", "!!!", p384.ExportSubjectPublicKeyInfoPem(), rsa.ExportSubjectPublicKeyInfoPem() })
        {
            var problems = RequiredConfiguration.Find(Config(new string('k', 32), bad));

            problems.ShouldHaveSingleItem().ShouldStartWith("SERVICE_JWT_PUBLIC_KEY");
        }
    }

    // ---------- STRING_ENCRYPTION_PASSPHRASE ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void Missing_blank_or_short_passphrase_names_the_variable_without_echoing_it(string? value)
    {
        var problems = RequiredConfiguration.Find(Config(new string('k', 32), passphrase: value));

        problems.ShouldHaveSingleItem().ShouldStartWith("STRING_ENCRYPTION_PASSPHRASE");
        if (!string.IsNullOrEmpty(value))
        {
            Should.Throw<System.InvalidOperationException>(() =>
                RequiredConfiguration.EnsureValid(Config(new string('k', 32), passphrase: value)))
                .Message.ShouldNotContain(value);
        }
    }

    [Fact]
    public void Passphrase_from_the_config_path_is_also_accepted()
    {
        // ABP đọc StringEncryption:DefaultPassPhrase; chấp nhận cả đường này cho tương thích ngược.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [EdgeKeyOptions.VariableName] = new string('k', 32),
            [Auth.ServiceCredential.PublicKeyVariable] = TestServiceJwt.PublicKeyPem,
            [StringEncryptionOptions.ConfigPath] = ValidPassphrase,
        }).Build();

        RequiredConfiguration.Find(config).ShouldBeEmpty();
    }

    [Fact]
    public void Public_key_is_accepted_as_pem_pem_with_literal_backslash_n_or_base64_der()
    {
        var pem = TestServiceJwt.PublicKeyPem;
        var oneLinePem = pem.Trim().Replace("\r", string.Empty).Replace("\n", "\\n");
        var base64Der = System.Convert.ToBase64String(TestServiceJwt.Key.ExportSubjectPublicKeyInfo());

        foreach (var good in new[] { pem, oneLinePem, base64Der, "  " + base64Der + "  " })
        {
            RequiredConfiguration.Find(Config(new string('k', 32), good)).ShouldBeEmpty();
        }
    }
}