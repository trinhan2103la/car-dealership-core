using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using CarDealership.CoreService.Catalog.Images;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>Một container S3Mock (API S3) cho các test của <see cref="S3ImageStorage"/>.</summary>
public sealed class S3MockFixture : IAsyncLifetime
{
    public const string Image = "adobe/s3mock:5.2.3";
    public const string Bucket = "vehicle-images";

    // S3Mock không kiểm chữ ký; khóa ở đây chỉ để SDK ký request, không phải bí mật.
    public const string AccessKey = "test-access";
    public const string SecretKey = "test-secret-not-real";

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage(Image)
        .WithPortBinding(9090, assignRandomHostPort: true)
        .WithEnvironment("COM_ADOBE_TESTING_S3MOCK_STORE_INITIAL_BUCKETS", Bucket)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9090).ForPath("/")))
        .Build();

    public string ServiceUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(9090)}";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Không dựa vào biến môi trường tạo bucket (tên biến đổi giữa các phiên bản S3Mock): tạo tường minh.
        using var client = new AmazonS3Client(new BasicAWSCredentials(AccessKey, SecretKey), new AmazonS3Config
        {
            ServiceURL = ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1",
        });
        try
        {
            await client.PutBucketAsync(Bucket);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>Ghi/đọc/xóa object thật qua API S3, và storage không khả dụng → <see cref="ImageStorageUnavailableException"/>.</summary>
[Trait("Category", "Integration")]
public class S3ImageStorageTests : IClassFixture<S3MockFixture>
{
    private readonly S3MockFixture _fixture;

    public S3ImageStorageTests(S3MockFixture fixture)
    {
        _fixture = fixture;
    }

    private static S3ImageStorage Storage(string? serviceUrl) => new(Options.Create(new ImageStorageOptions
    {
        ServiceUrl = serviceUrl,
        Bucket = S3MockFixture.Bucket,
        AccessKey = S3MockFixture.AccessKey,
        SecretKey = S3MockFixture.SecretKey,
        TimeoutSeconds = 3,
    }));

    [Fact]
    public async Task Put_exists_delete_round_trip()
    {
        using var storage = Storage(_fixture.ServiceUrl);
        var key = ImageStorageKeys.ObjectKey(Guid.NewGuid(), ImageStorageKeys.NewBaseKey(), "md");
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("RIFF....WEBPVP8 fake"));

        await storage.PutAsync(key, content, "image/webp");
        (await storage.ExistsAsync(key)).ShouldBeTrue();

        await storage.DeleteAsync(key);
        (await storage.ExistsAsync(key)).ShouldBeFalse();

        await storage.DeleteAsync(key);   // xóa lại: không lỗi
    }

    [Fact]
    public async Task Unreachable_storage_raises_a_retryable_error_without_leaking_credentials()
    {
        using var storage = Storage("http://127.0.0.1:1");
        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var ex = await Should.ThrowAsync<ImageStorageUnavailableException>(() => storage.PutAsync("k", content, "image/webp"));

        ex.Message.ShouldNotContain(S3MockFixture.SecretKey);
    }

    [Fact]
    public async Task Missing_configuration_is_reported_by_variable_name_only()
    {
        using var storage = Storage(null);
        await using var content = new MemoryStream(new byte[] { 1 });

        var ex = await Should.ThrowAsync<ImageStorageUnavailableException>(() => storage.PutAsync("k", content, "image/webp"));

        ex.Message.ShouldContain("ObjectStorage__ServiceUrl");
    }

    [Fact]
    public void Object_keys_are_random_and_never_contain_a_file_name()
    {
        var model = Guid.NewGuid();
        var a = ImageStorageKeys.ObjectKey(model, ImageStorageKeys.NewBaseKey(), "lg");
        var b = ImageStorageKeys.ObjectKey(model, ImageStorageKeys.NewBaseKey(), "lg");

        a.ShouldNotBe(b);
        a.ShouldMatch($"^{model:N}/[0-9a-f]{{32}}-lg\\.webp$");
    }
}
