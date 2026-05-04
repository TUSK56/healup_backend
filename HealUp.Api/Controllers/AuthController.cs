using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HealUp.Api.Data;
using HealUp.Api.Models;
using HealUp.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api")]
public class AuthController : ControllerBase
{
    private readonly HealUpDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly IConfiguration _configuration;
    private readonly NotificationService _notifications;

    public AuthController(HealUpDbContext db, JwtTokenService jwt, IConfiguration configuration, NotificationService notifications)
    {
        _db = db;
        _jwt = jwt;
        _configuration = configuration;
        _notifications = notifications;
    }

    public class PatientRegisterDto
    {
        [Required, MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(120)]
        public string? City { get; set; }

        [MaxLength(120)]
        public string? District { get; set; }

        [MaxLength(500)]
        public string? AddressDetails { get; set; }

        [Required, MinLength(12), MaxLength(15)]
        public string Password { get; set; } = string.Empty;

        [Required, MinLength(12), MaxLength(15)]
        public string PasswordConfirmation { get; set; } = string.Empty;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class PharmacyRegisterDto
    {
        [Required, MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(100)]
        public string? LicenseNumber { get; set; }

        [MaxLength(255)]
        public string? ResponsiblePharmacistName { get; set; }

        [MaxLength(120)]
        public string? City { get; set; }

        [MaxLength(120)]
        public string? District { get; set; }

        [MaxLength(500)]
        public string? AddressDetails { get; set; }

        [Required, MinLength(12), MaxLength(15)]
        public string Password { get; set; } = string.Empty;

        [Required, MinLength(12), MaxLength(15)]
        public string PasswordConfirmation { get; set; } = string.Empty;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class LoginDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Guard { get; set; } = string.Empty; // user | pharmacy | admin
    }

    public class OtpSendDto
    {
        [Required]
        public string Identifier { get; set; } = string.Empty;

        /// <summary>Optional: patient | pharmacy | admin — narrows account lookup for OTP.</summary>
        [JsonPropertyName("guard")]
        public string? Guard { get; set; }
    }

    public class OtpVerifyDto
    {
        [Required]
        public string Identifier { get; set; } = string.Empty;

        [Required]
        public string Otp { get; set; } = string.Empty;
    }

    private string GetTestingOtp() =>
        _configuration["Otp:TestingCode"] ?? "0000";

    private static bool MeetsStrictPasswordRules(string password) =>
        password.Length is >= 12 and <= 15 && Regex.IsMatch(password, "[!@#$%^]");

    private static string DigitsOnly(string value) =>
        new string((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private async Task<bool> PatientMatchesIdentifierAsync(string identifier, CancellationToken ct)
    {
        var id = identifier.Trim();
        if (id.Length == 0) return false;
        if (await _db.Patients.AnyAsync(u => u.Email == id, ct))
            return true;
        var digits = DigitsOnly(id);
        if (digits.Length < 6) return false;
        return await _db.Patients.AnyAsync(u => u.Phone != null && DigitsOnly(u.Phone) == digits, ct);
    }

    private async Task<bool> PharmacyMatchesIdentifierAsync(string identifier, CancellationToken ct)
    {
        var id = identifier.Trim();
        if (id.Length == 0) return false;
        if (await _db.Pharmacies.AnyAsync(u => u.Email == id, ct))
            return true;
        var digits = DigitsOnly(id);
        if (digits.Length < 6) return false;
        return await _db.Pharmacies.AnyAsync(u => u.Phone != null && DigitsOnly(u.Phone) == digits, ct);
    }

    private async Task<bool> AdminMatchesIdentifierAsync(string identifier, CancellationToken ct)
    {
        var id = identifier.Trim();
        if (id.Length == 0) return false;
        if (await _db.Admins.AnyAsync(u => u.Email == id, ct))
            return true;
        var digits = DigitsOnly(id);
        if (digits.Length < 6) return false;
        return await _db.Admins.AnyAsync(u => u.Phone != null && DigitsOnly(u.Phone) == digits, ct);
    }

    [HttpPost("register/patient")]
    public async Task<IActionResult> RegisterPatient([FromBody] PatientRegisterDto dto, CancellationToken ct)
    {
        if (dto.Password != dto.PasswordConfirmation)
            return BadRequest(new { message = "HealUp: Passwords do not match." });

        if (!MeetsStrictPasswordRules(dto.Password))
            return BadRequest(new { message = "HealUp: Password must be 12-15 characters and include at least one of ! @ # $ % ^." });

        if (await _db.Patients.AnyAsync(u => u.Email == dto.Email, ct))
            return Conflict(new { message = "HealUp: Email already registered." });

        var patient = new Patient
        {
            Name = dto.Name,
            Email = dto.Email,
            Phone = dto.Phone,
            PasswordHash = PasswordHasher.HashPassword(dto.Password),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude
        };

        _db.Patients.Add(patient);
        await _db.SaveChangesAsync(ct);

        var hasAddressData =
            !string.IsNullOrWhiteSpace(dto.City) ||
            !string.IsNullOrWhiteSpace(dto.District) ||
            !string.IsNullOrWhiteSpace(dto.AddressDetails) ||
            (dto.Latitude.HasValue && dto.Longitude.HasValue);

        if (hasAddressData)
        {
            _db.PatientAddresses.Add(new PatientAddress
            {
                PatientId = patient.Id,
                Label = "المنزل",
                IconKey = "home",
                City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim(),
                District = string.IsNullOrWhiteSpace(dto.District) ? null : dto.District.Trim(),
                AddressDetails = string.IsNullOrWhiteSpace(dto.AddressDetails) ? null : dto.AddressDetails.Trim(),
                Latitude = dto.Latitude,
                Longitude = dto.Longitude
            });
            await _db.SaveChangesAsync(ct);
        }

        var token = _jwt.GenerateForPatient(patient);

        await _notifications.NotifyAllAdminsAsync(
            "new_patient_registered",
            $"HealUp: New patient account created ({patient.Name}).",
            "/admin/patients",
            new { patient_id = patient.Id },
            ct);

        return Created(string.Empty, new
        {
            message = "Welcome to HealUp. Your patient account has been created.",
            user = new { patient.Id, patient.Name, patient.Email, role = "patient", patient.Latitude, patient.Longitude },
            token,
            token_type = "bearer"
        });
    }

    [HttpPost("register/pharmacy")]
    public async Task<IActionResult> RegisterPharmacy([FromBody] PharmacyRegisterDto dto, CancellationToken ct)
    {
        if (dto.Password != dto.PasswordConfirmation)
            return BadRequest(new { message = "HealUp: Passwords do not match." });

        if (!MeetsStrictPasswordRules(dto.Password))
            return BadRequest(new { message = "HealUp: Password must be 12-15 characters and include at least one of ! @ # $ % ^." });

        if (await _db.Pharmacies.AnyAsync(p => p.Email == dto.Email, ct))
            return Conflict(new { message = "HealUp: Pharmacy email already registered." });

        var pharmacy = new Pharmacy
        {
            Name = dto.Name,
            Email = dto.Email,
            Phone = dto.Phone,
            LicenseNumber = dto.LicenseNumber,
            ResponsiblePharmacistName = string.IsNullOrWhiteSpace(dto.ResponsiblePharmacistName) ? dto.Name : dto.ResponsiblePharmacistName,
            City = dto.City,
            District = dto.District,
            AddressDetails = dto.AddressDetails,
            PasswordHash = PasswordHasher.HashPassword(dto.Password),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            Status = "pending"
        };

        _db.Pharmacies.Add(pharmacy);
        await _db.SaveChangesAsync(ct);

        var token = _jwt.GenerateForPharmacy(pharmacy);

        await _notifications.NotifyAllAdminsAsync(
            "new_pharmacy_registered",
            $"HealUp: New pharmacy registration submitted ({pharmacy.Name}).",
            "/admin/pharmacies",
            new { pharmacy_id = pharmacy.Id },
            ct);

        return Created(string.Empty, new
        {
            message = "Welcome to HealUp. Your pharmacy registration is pending admin approval.",
            pharmacy = new { pharmacy.Id, pharmacy.Name, pharmacy.Email, pharmacy.Status },
            token,
            token_type = "bearer"
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto, CancellationToken ct)
    {
        try
        {
            if (dto.Guard == "pharmacy")
            {
                var pharmacy = await _db.Pharmacies.SingleOrDefaultAsync(p => p.Email == dto.Email, ct);
                if (pharmacy is null || !PasswordHasher.VerifyPassword(dto.Password, pharmacy.PasswordHash))
                    return Unauthorized(new { message = "HealUp: Invalid credentials." });

                if (pharmacy.Status != "approved")
                    return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending approval." });

                var token = _jwt.GenerateForPharmacy(pharmacy);
                return Ok(new
                {
                    message = "Welcome back to HealUp.",
                    pharmacy = new { pharmacy.Id, pharmacy.Name, pharmacy.Email, pharmacy.Status },
                    token,
                    token_type = "bearer"
                });
            }

            if (dto.Guard == "admin")
            {
                var admin = await _db.Admins.SingleOrDefaultAsync(a => a.Email == dto.Email, ct);
                if (admin is null || !PasswordHasher.VerifyPassword(dto.Password, admin.PasswordHash))
                    return Unauthorized(new { message = "HealUp: Invalid credentials." });

                var tokenAdmin = _jwt.GenerateForAdmin(admin);
                return Ok(new
                {
                    message = "Welcome back to HealUp.",
                    user = new { admin.Id, admin.Name, admin.Email, role = "admin" },
                    token = tokenAdmin,
                    token_type = "bearer"
                });
            }

            var patientUser = await _db.Patients.SingleOrDefaultAsync(u => u.Email == dto.Email, ct);
            if (patientUser is null || !PasswordHasher.VerifyPassword(dto.Password, patientUser.PasswordHash))
                return Unauthorized(new { message = "HealUp: Invalid credentials." });

            var tokenUser = _jwt.GenerateForPatient(patientUser);
            return Ok(new
            {
                message = "Welcome back to HealUp.",
                user = new { patientUser.Id, patientUser.Name, patientUser.Email, role = "patient" },
                token = tokenUser,
                token_type = "bearer"
            });
        }
        catch (SqlException)
        {
            return StatusCode(503, new
            {
                message = "HealUp: Database is temporarily unavailable. Please try again in a moment."
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("transient failure", StringComparison.OrdinalIgnoreCase)
                                                   || ex.Message.Contains("instance failure", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(503, new
            {
                message = "HealUp: Database is temporarily unavailable. Please try again in a moment."
            });
        }
    }

    [HttpPost("otp/send")]
    public async Task<IActionResult> SendOtp([FromBody] OtpSendDto dto, CancellationToken ct)
    {
        var id = (dto.Identifier ?? string.Empty).Trim();
        if (id.Length == 0)
            return BadRequest(new { message = "HealUp: Identifier is required." });

        var guard = (dto.Guard ?? string.Empty).Trim().ToLowerInvariant();
        var exists = guard switch
        {
            "pharmacy" => await PharmacyMatchesIdentifierAsync(id, ct),
            "admin" => await AdminMatchesIdentifierAsync(id, ct),
            "user" or "patient" => await PatientMatchesIdentifierAsync(id, ct),
            _ => await PatientMatchesIdentifierAsync(id, ct)
                 || await PharmacyMatchesIdentifierAsync(id, ct)
                 || await AdminMatchesIdentifierAsync(id, ct),
        };

        if (!exists)
            return NotFound(new { message = "HealUp: No account found for this email or phone number." });

        var otp = GetTestingOtp();
        return Ok(new
        {
            message = "HealUp: OTP sent successfully (testing mode).",
            identifier = id,
            otp
        });
    }

    [HttpPost("otp/verify")]
    public IActionResult VerifyOtp([FromBody] OtpVerifyDto dto)
    {
        var otp = GetTestingOtp();
        if (!string.Equals(dto.Otp, otp, StringComparison.Ordinal))
            return Unauthorized(new { message = "HealUp: Invalid OTP." });

        return Ok(new
        {
            message = "HealUp: OTP verified successfully.",
            identifier = dto.Identifier,
            verified = true
        });
    }
}

