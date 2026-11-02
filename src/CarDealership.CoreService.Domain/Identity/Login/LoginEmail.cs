using System;

namespace CarDealership.CoreService.Identity.Login;

/// <summary>Req 14.1: email so khớp không phân biệt hoa thường sau khi trim. Cùng cách chuẩn hóa với ASP.NET Identity (chữ hoa).</summary>
public static class LoginEmail
{
    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToUpperInvariant();
    }
}
