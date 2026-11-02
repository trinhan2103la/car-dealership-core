using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CarDealership.CoreService.Identity;

public interface ISessionAppService : IApplicationService
{
    /// <summary>Đăng nhập. Trả null khi thất bại vì BẤT KỲ lý do nào (controller đổi thành 401 chung).</summary>
    Task<LoginResultDto?> LoginAsync(LoginInput input);
}
