using System;
using Microsoft.Extensions.Options;
using Volo.Abp.Timing;

namespace CarDealership.CoreService;

/// <summary>
/// Đồng hồ test: mặc định chạy như đồng hồ thật của ABP (UTC theo AbpClockOptions), nhưng cho dịch chuyển để
/// kiểm các mốc 15 phút / 30 phút mà không phải chờ. Singleton đăng ký thay IClock trong CoreServiceTestBaseModule (attribute Dependency/ExposeServices KHÔNG đủ: ABP vẫn trả Clock gốc); test PHẢI gọi <see cref="Reset"/> khi xong.
/// </summary>
public class TestClock : Clock
{
    public TestClock(
        IOptions<AbpClockOptions> options,
        ICurrentTimezoneProvider currentTimezoneProvider,
        ITimezoneProvider timezoneProvider)
        : base(options, currentTimezoneProvider, timezoneProvider)
    {
    }

    public TimeSpan Offset { get; set; } = TimeSpan.Zero;

    public override DateTime Now => base.Now + Offset;

    public void Advance(TimeSpan by) => Offset += by;

    public void Reset() => Offset = TimeSpan.Zero;
}

