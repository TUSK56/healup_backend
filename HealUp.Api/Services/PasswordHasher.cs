using System.Security.Cryptography;
using System.Text;

namespace HealUp.Api.Services;

public static class PasswordHasher
{
    public static string HashPassword(string password)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public static bool VerifyPassword(string password, string hash) =>
        HashPassword(password) == hash;
}
