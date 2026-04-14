using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HealUp.Api.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HealUp.Api.Services;

public class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateForPatient(Patient patient)
    {
        return GenerateToken(patient.Id.ToString(), patient.Email, "patient", "user");
    }

    public string GenerateForAdmin(Admin admin)
    {
        return GenerateToken(admin.Id.ToString(), admin.Email, "admin", "user");
    }

    public string GenerateForPharmacy(Pharmacy pharmacy)
    {
        return GenerateToken(pharmacy.Id.ToString(), pharmacy.Email, "pharmacy", "pharmacy");
    }

    private string GenerateToken(string id, string email, string role, string guard)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresMinutes = int.TryParse(jwtSection["ExpiresMinutes"], out var m) ? m : 60;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, id),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Role, role),
            new("guard", guard)
        };

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiresMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

