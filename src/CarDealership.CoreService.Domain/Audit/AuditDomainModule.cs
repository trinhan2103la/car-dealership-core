using Volo.Abp.Modularity;

namespace CarDealership.CoreService.Audit;

/// <summary>
/// Ngày 16 (ADR-003, Req 28.1): module Audit (security event + audit trail của dự án).
/// Skeleton rỗng; entity thêm ở P2 (Ngày 56+). Bảng dùng tiền tố "aud_".
/// Lưu ý: KHÁC với module ABP AuditLogging có sẵn.
/// </summary>
public class AuditDomainModule : AbpModule
{
}
