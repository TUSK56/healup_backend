using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HealUp.Api.Data;
using HealUp.Api.Models;
using HealUp.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api")]
public class AuthController : ControllerBase
{
    private readonly HealUpDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly IConfiguration _configuration;
    private readonly NotificationService _notifications;
    private readonly IMemoryCache _memoryCache;
    private readonly SmtpEmailSender _smtpEmail;
    private readonly IWebHostEnvironment _env;

    public AuthController(
        HealUpDbContext db,
        JwtTokenService jwt,
        IConfiguration configuration,
        NotificationService notifications,
        IMemoryCache memoryCache,
        SmtpEmailSender smtpEmail,
        IWebHostEnvironment env)
    {
        _db = db;
        _jwt = jwt;
        _configuration = configuration;
        _notifications = notifications;
        _memoryCache = memoryCache;
        _smtpEmail = smtpEmail;
        _env = env;
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

    public class PasswordResetAfterOtpDto
    {
        [Required]
        public string Identifier { get; set; } = string.Empty;

        /// <summary>patient | pharmacy | admin (also accepts user)</summary>
        [Required]
        public string Guard { get; set; } = string.Empty;

        [Required, MinLength(12), MaxLength(15)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, MinLength(12), MaxLength(15)]
        public string NewPasswordConfirmation { get; set; } = string.Empty;
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

    private static string OtpCacheKey(string identifier) =>
        $"otp:{identifier.Trim().ToLowerInvariant()}";

    private static string PasswordResetTicketKey(string identifier) =>
        $"pwdreset:{identifier.Trim().ToLowerInvariant()}";

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private async Task<Patient?> FindPatientByIdentifierAsync(string identifier, CancellationToken ct)
    {
        var id = identifier.Trim();
        if (id.Length == 0) return null;
        var byEmail = await _db.Patients.FirstOrDefaultAsync(u => u.Email == id, ct);
        if (byEmail is not null) return byEmail;
        var digits = DigitsOnly(id);
        if (digits.Length < 6) return null;
        return await _db.Patients.FirstOrDefaultAsync(u => u.Phone != null && DigitsOnly(u.Phone) == digits, ct);
    }

    private async Task<Pharmacy?> FindPharmacyByIdentifierAsync(string identifier, CancellationToken ct)
    {
        var id = identifier.Trim();
        if (id.Length == 0) return null;
        var byEmail = await _db.Pharmacies.FirstOrDefaultAsync(u => u.Email == id, ct);
        if (byEmail is not null) return byEmail;
        var digits = DigitsOnly(id);
        if (digits.Length < 6) return null;
        return await _db.Pharmacies.FirstOrDefaultAsync(u => u.Phone != null && DigitsOnly(u.Phone) == digits, ct);
    }

    private async Task<Admin?> FindAdminByIdentifierAsync(string identifier, CancellationToken ct)
    {
        var id = identifier.Trim();
        if (id.Length == 0) return null;
        var byEmail = await _db.Admins.FirstOrDefaultAsync(u => u.Email == id, ct);
        if (byEmail is not null) return byEmail;
        var digits = DigitsOnly(id);
        if (digits.Length < 6) return null;
        return await _db.Admins.FirstOrDefaultAsync(u => u.Phone != null && DigitsOnly(u.Phone) == digits, ct);
    }

    private async Task<string?> ResolveEmailForDeliveryAsync(string identifier, CancellationToken ct)
    {
        var id = identifier.Trim();
        if (id.Contains('@', StringComparison.Ordinal))
            return id;

        var digits = DigitsOnly(id);
        if (digits.Length < 6) return null;

        var patient = await _db.Patients.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Phone != null && DigitsOnly(u.Phone) == digits, ct);
        if (!string.IsNullOrWhiteSpace(patient?.Email))
            return patient.Email;

        var pharmacy = await _db.Pharmacies.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Phone != null && DigitsOnly(u.Phone) == digits, ct);
        if (!string.IsNullOrWhiteSpace(pharmacy?.Email))
            return pharmacy.Email;

        var admin = await _db.Admins.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Phone != null && DigitsOnly(u.Phone) == digits, ct);
        return string.IsNullOrWhiteSpace(admin?.Email) ? null : admin.Email;
    }

    [HttpPost("register/patient")]
    public async Task<IActionResult> RegisterPatient([FromBody] PatientRegisterDto dto, CancellationToken ct)
    {
        if (dto.Password != dto.PasswordConfirmation)
            return BadRequest(new { message = "HealUp: Passwords do not match." });

        if (!MeetsStrictPasswordRules(dto.Password))
            return BadRequest(new { message = "HealUp: Password must be 12-15 characters and include at least one of ! @ # $ % ^." });

        var email = NormalizeEmail(dto.Email);
        if (await _db.Patients.AnyAsync(u => u.Email == email, ct))
            return Conflict(new { message = "HealUp: Email already registered." });

        var patient = new Patient
        {
            Name = dto.Name,
            Email = email,
            Phone = dto.Phone,
            PasswordHash = PasswordHasher.HashPassword(dto.Password),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude
        };

        _db.Patients.Add(patient);
        try
        {
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
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627))
        {
            return Conflict(new { message = "HealUp: Email already registered." });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                message = "HealUp: Could not save patient account to the database.",
                detail = _env.IsDevelopment() ? ex.Message : null
            });
        }

        var token = _jwt.GenerateForPatient(patient);

        try
        {
            await _notifications.NotifyAllAdminsAsync(
                "new_patient_registered",
                $"HealUp: New patient account created ({patient.Name}).",
                "/admin/patients",
                new { patient_id = patient.Id },
                ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HealUp: patient registration notification skipped: {ex.Message}");
        }

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

        var email = NormalizeEmail(dto.Email);
        if (await _db.Pharmacies.AnyAsync(p => p.Email == email, ct))
            return Conflict(new { message = "HealUp: Pharmacy email already registered." });

        var pharmacy = new Pharmacy
        {
            Name = dto.Name,
            Email = email,
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

        var otpCode = Random.Shared.Next(1000, 10000).ToString("D4");
        var cacheKey = OtpCacheKey(id);
        _memoryCache.Set(
            cacheKey,
            otpCode,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) });

        var smtpOn = _configuration.GetSection("Smtp").GetValue("Enabled", false);
        if (smtpOn)
        {
            var to = await ResolveEmailForDeliveryAsync(id, ct);
            if (string.IsNullOrWhiteSpace(to))
                return BadRequest(new { message = "HealUp: No email address on file for this account." });

            var sent = await _smtpEmail.TrySendAsync(
                to,
                "HealUp — رمز التحقق",
                $"رمز التحقق الخاص بك هو: {otpCode}\nصالح لمدة 15 دقيقة.",
                ct);

            if (!sent)
                return StatusCode(503, new { message = "HealUp: Could not send email. Try again later." });

            return Ok(new
            {
                message = "HealUp: OTP sent to your email.",
                identifier = id,
                otp = _env.IsDevelopment() ? otpCode : null,
            });
        }

        return Ok(new
        {
            message = "HealUp: OTP sent successfully (testing mode — SMTP disabled).",
            identifier = id,
            otp = otpCode,
        });
    }

    [HttpPost("otp/verify")]
    public IActionResult VerifyOtp([FromBody] OtpVerifyDto dto)
    {
        var id = (dto.Identifier ?? string.Empty).Trim();
        if (id.Length == 0 || string.IsNullOrWhiteSpace(dto.Otp))
            return BadRequest(new { message = "HealUp: Identifier and OTP are required." });

        var entered = dto.Otp.Trim();
        var key = OtpCacheKey(id);

        if (_memoryCache.TryGetValue(key, out string? cached) &&
            string.Equals(entered, cached, StringComparison.Ordinal))
        {
            _memoryCache.Remove(key);
            _memoryCache.Set(PasswordResetTicketKey(id), true, TimeSpan.FromMinutes(15));
            return Ok(new
            {
                message = "HealUp: OTP verified successfully.",
                identifier = id,
                verified = true,
            });
        }

        if (string.Equals(entered, GetTestingOtp(), StringComparison.Ordinal))
        {
            _memoryCache.Remove(key);
            _memoryCache.Set(PasswordResetTicketKey(id), true, TimeSpan.FromMinutes(15));
            return Ok(new
            {
                message = "HealUp: OTP verified successfully.",
                identifier = id,
                verified = true,
            });
        }

        return Unauthorized(new { message = "HealUp: Invalid OTP." });
    }

    [HttpPost("password/reset-after-otp")]
    public async Task<IActionResult> ResetPasswordAfterOtp([FromBody] PasswordResetAfterOtpDto dto, CancellationToken ct)
    {
        var id = (dto.Identifier ?? string.Empty).Trim();
        if (id.Length == 0)
            return BadRequest(new { message = "HealUp: Identifier is required." });

        var ticketKey = PasswordResetTicketKey(id);
        if (!_memoryCache.TryGetValue(ticketKey, out bool ticketOk) || !ticketOk)
            return BadRequest(new { message = "انتهت صلاحية خطوة التحقق. اطلب رمزًا جديدًا.", field = "otp" });

        if (!string.Equals(dto.NewPassword, dto.NewPasswordConfirmation, StringComparison.Ordinal))
            return BadRequest(new { message = "كلمتا المرور غير متطابقتين.", field = "new_password_confirmation" });

        if (!MeetsStrictPasswordRules(dto.NewPassword))
            return BadRequest(new { message = "كلمة المرور غير صالحة.", field = "new_password" });

        var guard = (dto.Guard ?? string.Empty).Trim().ToLowerInvariant();
        if (guard is "user" or "patient")
        {
            var patient = await FindPatientByIdentifierAsync(id, ct);
            if (patient is null)
                return NotFound(new { message = "HealUp: Account not found." });

            if (PasswordHasher.VerifyPassword(dto.NewPassword, patient.PasswordHash))
                return BadRequest(new { message = "لا يمكن استخدام نفس كلمة المرور السابقة.", field = "new_password" });

            patient.PasswordHash = PasswordHasher.HashPassword(dto.NewPassword);
            await _db.SaveChangesAsync(ct);
            _memoryCache.Remove(ticketKey);
            return Ok(new { message = "HealUp: Password updated." });
        }

        if (guard == "pharmacy")
        {
            var pharmacy = await FindPharmacyByIdentifierAsync(id, ct);
            if (pharmacy is null)
                return NotFound(new { message = "HealUp: Account not found." });

            if (PasswordHasher.VerifyPassword(dto.NewPassword, pharmacy.PasswordHash))
                return BadRequest(new { message = "لا يمكن استخدام نفس كلمة المرور السابقة.", field = "new_password" });

            pharmacy.PasswordHash = PasswordHasher.HashPassword(dto.NewPassword);
            await _db.SaveChangesAsync(ct);
            _memoryCache.Remove(ticketKey);
            return Ok(new { message = "HealUp: Password updated." });
        }

        if (guard == "admin")
        {
            var admin = await FindAdminByIdentifierAsync(id, ct);
            if (admin is null)
                return NotFound(new { message = "HealUp: Account not found." });

            if (PasswordHasher.VerifyPassword(dto.NewPassword, admin.PasswordHash))
                return BadRequest(new { message = "لا يمكن استخدام نفس كلمة المرور السابقة.", field = "new_password" });

            admin.PasswordHash = PasswordHasher.HashPassword(dto.NewPassword);
            await _db.SaveChangesAsync(ct);
            _memoryCache.Remove(ticketKey);
            return Ok(new { message = "HealUp: Password updated." });
        }

        return BadRequest(new { message = "HealUp: Invalid guard for password reset." });
    }
}

