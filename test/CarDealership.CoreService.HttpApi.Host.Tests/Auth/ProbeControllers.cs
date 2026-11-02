using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

// Namespace CỐ Ý không bắt đầu bằng CarDealership.CoreService: các controller này chỉ dùng trong host tối giản của
// ServiceCredentialTests, và nếu lỡ lọt vào host thật thì HideAbpModuleControllersConvention sẽ gỡ chúng.
namespace ProbeFixtures;

[Route("api/app/probe")]
public class AppProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("app");

    [HttpPost]
    public IActionResult Post() => Ok("app-post");
}

/// <summary>Không có [Authorize] nào: route dưới /api/jobs phải TỰ động dùng ServiceJobs nhờ convention.</summary>
[Route("api/jobs/probe")]
public class JobsProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("jobs");

    [HttpPost("run")]
    public IActionResult Run() => Ok("jobs-run");
}

/// <summary>Route nằm ở controller khác nhưng action ghi đè route tuyệt đối vào /api/jobs.</summary>
[Route("api/app/other")]
public class SneakyJobsController : ControllerBase
{
    [HttpGet("/api/jobs/sneaky")]
    public IActionResult Sneaky() => Ok("sneaky-jobs");

    [HttpGet("normal")]
    public IActionResult Normal() => Ok("other-normal");
}

/// <summary>Chữ hoa trong route vẫn là /api/jobs (ASP.NET so khớp route không phân biệt hoa thường).</summary>
[Route("API/Jobs/Mixed")]
public class MixedCaseJobsController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("mixed-jobs");
}

/// <summary>Tiền tố giống nhưng KHÁC đoạn: /api/jobsfoo không phải job.</summary>
[Route("api/jobsfoo")]
public class NotAJobController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("not-a-job");
}

/// <summary>Controller "quên" khai báo bảo mật: phải rơi vào fallback ServiceBff, không được mở công khai.</summary>
[Route("api/forgot")]
public class ForgotAttributesController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("forgot");
}

/// <summary>Mở công khai CÓ CHỦ ĐÍCH bằng [AllowAnonymous].</summary>
[Route("api/open")]
[AllowAnonymous]
public class OpenController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("open");
}

/// <summary>Gắn policy tường minh trên một endpoint không nằm dưới /api/jobs.</summary>
[Route("api/explicit-jobs")]
[Authorize(Policy = "ServiceJobs")]
public class ExplicitJobsPolicyController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("explicit-jobs");
}
