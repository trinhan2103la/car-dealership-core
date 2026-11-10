using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace CarDealership.CoreService.Catalog.Images;

/// <summary>
/// Cấu hình Object_Storage (mục <c>ObjectStorage</c>; biến môi trường <c>ObjectStorage__ServiceUrl</c>, ...).
/// Khóa truy cập CHỈ từ biến môi trường, không có giá trị mặc định trong mã nguồn.
/// </summary>
public class ImageStorageOptions
{
    public const string Section = "ObjectStorage";

    /// <summary>Endpoint S3, ví dụ <c>http://localhost:9090</c> (S3Mock) hoặc <c>https://&lt;project&gt;.supabase.co/storage/v1/s3</c>.</summary>
    public string? ServiceUrl { get; set; }

    public string Bucket { get; set; } = "vehicle-images";

    public string Region { get; set; } = "us-east-1";

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>Thời gian chờ mỗi thao tác. Hết giờ coi như storage không khả dụng.</summary>
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// <see cref="IImageStorage"/> qua API S3 (AWSSDK.S3, endpoint tùy chỉnh, <c>ForcePathStyle</c> cho các dịch vụ S3-compatible).
/// Mọi lỗi mạng/S3 được đổi thành <see cref="ImageStorageUnavailableException"/> và KHÔNG mang theo khóa truy cập.
/// </summary>
public class S3ImageStorage : IImageStorage, ISingletonDependency, IDisposable
{
    private readonly IOptions<ImageStorageOptions> _options;
    private readonly Lazy<IAmazonS3> _client;

    public S3ImageStorage(IOptions<ImageStorageOptions> options)
    {
        _options = options;
        _client = new Lazy<IAmazonS3>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private string Bucket => _options.Value.Bucket;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await RunAsync(() => _client.Value.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            // S3-compatible (Supabase, S3Mock) không hỗ trợ đủ checksum trailer mặc định của SDK v4.
            UseChunkEncoding = false,
        }, cancellationToken), "ghi");
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        // S3 trả 204 cả khi object không tồn tại: xóa lại an toàn.
        await RunAsync(() => _client.Value.DeleteObjectAsync(Bucket, key, cancellationToken), "xóa");
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await RunAsync(() => _client.Value.GetObjectMetadataAsync(Bucket, key, cancellationToken), "đọc");
            return true;
        }
        catch (ImageStorageUnavailableException ex) when (ex.InnerException is AmazonS3Exception { StatusCode: HttpStatusCode.NotFound })
        {
            return false;
        }
    }

    private static async Task RunAsync(Func<Task> action, string operation)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException
                                       or IOException or TaskCanceledException)
        {
            // Không đưa thông điệp gốc vào message (có thể chứa URL ký sẵn); giữ InnerException để log có kiểm soát.
            throw new ImageStorageUnavailableException($"Object_Storage không khả dụng khi {operation} ảnh.", ex);
        }
    }

    private IAmazonS3 CreateClient()
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.ServiceUrl)
            || string.IsNullOrWhiteSpace(options.AccessKey)
            || string.IsNullOrWhiteSpace(options.SecretKey))
        {
            throw new ImageStorageUnavailableException(
                "Thiếu cấu hình Object_Storage (ObjectStorage__ServiceUrl, ObjectStorage__AccessKey, ObjectStorage__SecretKey).");
        }

        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = options.Region,
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            MaxErrorRetry = 2,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
