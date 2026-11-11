using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;

namespace CarDealership.CoreService.Catalog.Images;

/// <summary>Các object của một ảnh đã xóa khỏi DB nhưng chưa xóa được khỏi Object_Storage (Req 9.9).</summary>
[BackgroundJobName("Catalog.ImageCleanup")]
public class ImageCleanupArgs
{
    public List<string> Keys { get; set; } = new();
}

/// <summary>
/// Dọn object mồ côi bằng job ABP (bảng <c>AbpBackgroundJobs</c>). Ném lỗi = ABP thử lại theo lịch giãn dần.
/// <see cref="IImageStorage.DeleteAsync"/> idempotent nên chạy lại an toàn. Render free ngủ thì job chỉ chạy khi service
/// thức, nên "trong 24 giờ" không được bảo đảm trên demo ít traffic (ghi ở ADR).
/// </summary>
public class ImageCleanupJob : AsyncBackgroundJob<ImageCleanupArgs>, ITransientDependency
{
    private readonly IImageStorage _storage;

    public ImageCleanupJob(IImageStorage storage)
    {
        _storage = storage;
    }

    public override async Task ExecuteAsync(ImageCleanupArgs args)
    {
        foreach (var key in args.Keys)
        {
            await _storage.DeleteAsync(key);
        }
    }
}
