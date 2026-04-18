using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using HealUp.Api.Data;
using HealUp.Api.Models;
using HealUp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api/patient")]
[Authorize(Roles = "patient")]
public class PatientController : ControllerBase
{
    private readonly HealUpDbContext _db;
    private readonly CloudinaryService _cloudinary;

    public PatientController(HealUpDbContext db, CloudinaryService cloudinary)
    {
        _db = db;
        _cloudinary = cloudinary;
    }

    public sealed class UpdatePatientMeDto
    {
        [MaxLength(255)]
        public string? Name { get; set; }

        [EmailAddress, MaxLength(255)]
        public string? Email { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        public DateTime? DateOfBirth { get; set; }
    }

    public sealed class ChangePasswordDto
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;

        [Required, MinLength(6)]
        public string NewPasswordConfirmation { get; set; } = string.Empty;
    }

    public sealed class CreateAddressDto
    {
        [Required, MaxLength(80)]
        public string Label { get; set; } = string.Empty;

        [Required, MaxLength(32)]
        public string IconKey { get; set; } = "home";

        [MaxLength(120)]
        public string? City { get; set; }

        [MaxLength(120)]
        public string? District { get; set; }

        [MaxLength(500)]
        public string? AddressDetails { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var patient = await _db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId.Value, ct);
        if (patient is null)
            return NotFound(new { message = "HealUp: Patient not found." });

        return Ok(new { data = ToPatientMeDto(patient) });
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdatePatientMeDto dto, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var patient = await _db.Patients.SingleOrDefaultAsync(p => p.Id == patientId.Value, ct);
        if (patient is null)
            return NotFound(new { message = "HealUp: Patient not found." });

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var normalized = dto.Email.Trim();
            var exists = await _db.Patients.AnyAsync(p => p.Email == normalized && p.Id != patient.Id, ct);
            if (exists)
                return Conflict(new { message = "HealUp: Email already registered." });
            patient.Email = normalized;
        }

        if (!string.IsNullOrWhiteSpace(dto.Name))
            patient.Name = dto.Name.Trim();

        patient.Phone = NormalizeOptional(dto.Phone);
        patient.DateOfBirth = dto.DateOfBirth;

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "HealUp: Patient profile updated successfully.", data = ToPatientMeDto(patient) });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto, CancellationToken ct)
    {
        if (dto.NewPassword != dto.NewPasswordConfirmation)
            return BadRequest(new { message = "HealUp: New passwords do not match." });

        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var patient = await _db.Patients.SingleOrDefaultAsync(p => p.Id == patientId.Value, ct);
        if (patient is null)
            return NotFound(new { message = "HealUp: Patient not found." });

        if (!PasswordHasher.VerifyPassword(dto.CurrentPassword, patient.PasswordHash))
            return BadRequest(new { message = "HealUp: Current password is incorrect.", field = "current_password" });

        patient.PasswordHash = PasswordHasher.HashPassword(dto.NewPassword);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "HealUp: Password changed successfully." });
    }

    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAvatar([FromForm(Name = "avatar")] IFormFile avatar, CancellationToken ct)
    {
        if (avatar is null || avatar.Length == 0)
            return BadRequest(new { message = "HealUp: Avatar file is required." });

        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var patient = await _db.Patients.SingleOrDefaultAsync(p => p.Id == patientId.Value, ct);
        if (patient is null)
            return NotFound(new { message = "HealUp: Patient not found." });

        var url = await _cloudinary.UploadImageAsync(avatar, "healup/avatars/patients", ct);
        patient.AvatarUrl = string.IsNullOrWhiteSpace(url) ? patient.AvatarUrl : url;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "HealUp: Avatar updated successfully.", avatar_url = patient.AvatarUrl });
    }

    [HttpGet("addresses")]
    public async Task<IActionResult> ListAddresses(CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var data = await _db.PatientAddresses
            .AsNoTracking()
            .Where(a => a.PatientId == patientId.Value)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return Ok(new { data = data.Select(ToAddressDto) });
    }

    [HttpPost("addresses")]
    public async Task<IActionResult> CreateAddress([FromBody] CreateAddressDto dto, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        // Guard against null/invalid payload values to avoid server-side 500s.
        var label = (dto.Label ?? string.Empty).Trim();
        var iconKey = (dto.IconKey ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(label))
            return BadRequest(new { message = "HealUp: Address label is required.", field = "label" });
        if (label.Length > 80)
            return BadRequest(new { message = "HealUp: Address label is too long.", field = "label" });

        if (string.IsNullOrWhiteSpace(iconKey))
            iconKey = "home";
        if (iconKey is not ("home" or "work" or "other"))
            iconKey = "other";

        var patientExists = await _db.Patients.AnyAsync(p => p.Id == patientId.Value, ct);
        if (!patientExists)
            return NotFound(new { message = "HealUp: Patient not found." });

        var address = new PatientAddress
        {
            PatientId = patientId.Value,
            Label = label,
            IconKey = iconKey,
            City = NormalizeOptional(dto.City),
            District = NormalizeOptional(dto.District),
            AddressDetails = NormalizeOptional(dto.AddressDetails),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude
        };

        try
        {
            _db.PatientAddresses.Add(address);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Fallback for environments where DB column naming differs from EF model naming.
            _db.Entry(address).State = EntityState.Detached;
            _db.ChangeTracker.Clear();
            var rawInserted = await InsertAddressRawWithSchemaFallbackAsync(
                patientId.Value,
                label,
                iconKey,
                NormalizeOptional(dto.City) ?? string.Empty,
                NormalizeOptional(dto.District) ?? string.Empty,
                NormalizeOptional(dto.AddressDetails) ?? string.Empty,
                dto.Latitude,
                dto.Longitude,
                ct
            );
            if (rawInserted is not null)
                return Ok(new { message = "HealUp: Address created successfully.", data = rawInserted });

            return BadRequest(new
            {
                message = "HealUp: Unable to save address. Please verify address data and try again."
            });
        }

        return Ok(new { message = "HealUp: Address created successfully.", data = ToAddressDto(address) });
    }

    [HttpDelete("addresses/{id:int}")]
    public async Task<IActionResult> DeleteAddress([FromRoute] int id, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var address = await _db.PatientAddresses.SingleOrDefaultAsync(a => a.Id == id && a.PatientId == patientId.Value, ct);
        if (address is null)
            return NotFound(new { message = "HealUp: Address not found." });

        _db.PatientAddresses.Remove(address);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "HealUp: Address deleted successfully." });
    }

    private int? GetCurrentEntityId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : null;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object ToPatientMeDto(Patient patient) => new
    {
        id = patient.Id,
        name = patient.Name,
        email = patient.Email,
        phone = patient.Phone,
        date_of_birth = patient.DateOfBirth,
        avatar_url = patient.AvatarUrl
    };

    private static object ToAddressDto(PatientAddress address) => new
    {
        id = address.Id,
        label = address.Label,
        icon_key = address.IconKey,
        city = address.City,
        district = address.District,
        address_details = address.AddressDetails,
        latitude = address.Latitude,
        longitude = address.Longitude,
        created_at = address.CreatedAt
    };

    private async Task<object?> InsertAddressRawWithSchemaFallbackAsync(
        int patientId,
        string label,
        string iconKey,
        string city,
        string district,
        string addressDetails,
        double? latitude,
        double? longitude,
        CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        const string schema = "dbo";
        const string table = "patient_addresses";
        // Resolve each column independently (handles hybrid schemas, e.g. Pascal PatientId + snake icon_key).
        var idCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "Id", "id");
        var patientIdCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "PatientId", "patient_id");
        var labelCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "Label", "label");
        var iconCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "IconKey", "icon_key", "Kind");
        var cityCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "City", "city");
        var districtCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "District", "district");
        var detailsCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "AddressDetails", "address_details", "Street", "FormattedAddress");
        var latCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "Latitude", "latitude");
        var lngCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "Longitude", "longitude");
        var createdCol = await ResolveAddressColumnAsync(conn, schema, table, ct, "CreatedAt", "created_at");

        await using var cmd = conn.CreateCommand();
        // Avoid OUTPUT INSERTED without INTO: it fails when the table has triggers. Use SCOPE_IDENTITY() instead.
        cmd.CommandText = $@"
INSERT INTO [dbo].[patient_addresses]
    ([{patientIdCol}], [{labelCol}], [{iconCol}], [{cityCol}], [{districtCol}], [{detailsCol}], [{latCol}], [{lngCol}], [{createdCol}])
VALUES (@pid, @label, @icon, @city, @district, @details, @lat, @lng, @createdAt);

SELECT [{idCol}], [{labelCol}], [{iconCol}], [{cityCol}], [{districtCol}], [{detailsCol}], [{latCol}], [{lngCol}], [{createdCol}]
FROM [dbo].[patient_addresses]
WHERE [{idCol}] = CAST(SCOPE_IDENTITY() AS int);";

        AddParam(cmd, "@pid", patientId);
        AddParam(cmd, "@label", label);
        AddParam(cmd, "@icon", iconKey);
        AddParam(cmd, "@city", string.IsNullOrEmpty(city) ? DBNull.Value : city);
        AddParam(cmd, "@district", string.IsNullOrEmpty(district) ? DBNull.Value : district);
        AddParam(cmd, "@details", string.IsNullOrEmpty(addressDetails) ? DBNull.Value : addressDetails);
        AddNullableDoubleParam(cmd, "@lat", latitude);
        AddNullableDoubleParam(cmd, "@lng", longitude);
        AddParam(cmd, "@createdAt", DateTime.UtcNow);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new
            {
                id = reader.GetValue(0),
                label = reader.GetValue(1)?.ToString(),
                icon_key = reader.GetValue(2)?.ToString(),
                city = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString(),
                district = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString(),
                address_details = reader.IsDBNull(5) ? null : reader.GetValue(5)?.ToString(),
                latitude = reader.IsDBNull(6) ? null : reader.GetValue(6),
                longitude = reader.IsDBNull(7) ? null : reader.GetValue(7),
                created_at = reader.IsDBNull(8) ? null : reader.GetValue(8)
            };
        }
        catch
        {
            return null;
        }
    }

    private static void AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void AddNullableDoubleParam(IDbCommand cmd, string name, double? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value.HasValue ? value.Value : DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static async Task<string> ResolveAddressColumnAsync(
        System.Data.Common.DbConnection conn,
        string schemaName,
        string tableName,
        CancellationToken ct,
        params string[] candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            if (await TableHasColumnAsync(conn, schemaName, tableName, candidateName, ct))
                return candidateName;
        }

        return candidateNames.Length > 0
            ? candidateNames[0]
            : throw new InvalidOperationException("No address column candidates were provided.");
    }

    private static async Task<bool> TableHasColumnAsync(
        System.Data.Common.DbConnection conn,
        string schemaName,
        string tableName,
        string columnName,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.columns c
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @schema AND t.name = @table AND c.name = @column
            ) THEN 1 ELSE 0 END
            """;
        AddParam(cmd, "@schema", schemaName);
        AddParam(cmd, "@table", tableName);
        AddParam(cmd, "@column", columnName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result ?? 0) == 1;
    }
}

