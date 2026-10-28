using Volo.Abp.Modularity;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Ngày 16 (ADR-003, Req 28.1): module Identity của dự án — phần mở rộng trên ABP Identity
/// (AdminSession, LoginAttempt, Showroom_Scope...). Skeleton rỗng; entity thêm ở P1 (Ngày 30+).
/// Bảng dùng tiền tố "idn_". Lưu ý: KHÁC với module ABP Identity có sẵn.
/// </summary>
public class IdentityDomainModule : AbpModule
{
}
