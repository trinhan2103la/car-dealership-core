namespace CarDealership.CoreService.Identity;

/// <summary>
/// Req 25.6: email và mật khẩu SuperAdmin ban đầu lấy từ BIẾN MÔI TRƯỜNG, không có giá trị mặc định
/// trong mã nguồn hay file seed. Được gán từ IConfiguration ở <see cref="IdentityDomainModule"/>.
/// </summary>
public class SuperAdminSeedOptions
{
    public const string EmailVariable = "SEED_SUPERADMIN_EMAIL";
    public const string PasswordVariable = "SEED_SUPERADMIN_PASSWORD";

    public string? Email { get; set; }
    public string? Password { get; set; }
}
