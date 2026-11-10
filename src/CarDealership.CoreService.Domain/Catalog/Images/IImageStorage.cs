using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CarDealership.CoreService.Catalog.Images;

/// <summary>
/// Object_Storage cho ảnh xe (Req 9.3). Khóa object do Core_API sinh (UUID), KHÔNG BAO GIỜ chứa tên file gốc.
/// Triển khai: <c>S3ImageStorage</c> (API S3: Supabase Storage trên demo, S3Mock ở local).
/// </summary>
public interface IImageStorage
{
    /// <summary>Ghi đè nếu đã tồn tại. Lỗi kết nối/từ chối ném <see cref="ImageStorageUnavailableException"/>.</summary>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Không lỗi nếu object không tồn tại (xóa lại an toàn khi thử lại).</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Object_Storage không khả dụng hoặc từ chối thao tác: người gọi KHÔNG tạo bản ghi và trả lỗi cho phép thử lại (Req 9.13).</summary>
public class ImageStorageUnavailableException : Exception
{
    public ImageStorageUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>Sinh khóa object ngẫu nhiên: <c>{carModelId}/{uuid}-{size}.webp</c>. Không đoán được, không chứa tên gốc.</summary>
public static class ImageStorageKeys
{
    public static readonly string[] Sizes = { "sm", "md", "lg" };

    /// <summary>Khóa gốc lưu trong <c>cat_vehicle_images.storage_key</c> (UUID). Ba biến thể suy ra từ khóa này.</summary>
    public static string NewBaseKey() => Guid.NewGuid().ToString("N");

    public static string ObjectKey(Guid carModelId, string baseKey, string size) => $"{carModelId:N}/{baseKey}-{size}.webp";
}
