using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using HealUp.Api.Data;
using HealUp.Api.Models;
using HealUp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api/pharmacy")]
[Authorize(Roles = "pharmacy")]
public class PharmacyController : ControllerBase
{
    private readonly HealUpDbContext _db;
    private readonly GoogleMapsService _maps;
    private readonly NotificationService _notifications;
    private const int DispatchBatchSize = 10;
    private const int DispatchWindowMinutes = 10;

    private sealed record PharmacyDistanceRow(int PharmacyId, double? Latitude, double? Longitude);

    public PharmacyController(HealUpDbContext db, GoogleMapsService maps, NotificationService notifications)
    {
        _db = db;
        _maps = maps;
        _notifications = notifications;
    }

    public sealed class UpdatePharmacyProfileDto
    {
        [MaxLength(50)]
        public string? Phone { get; set; }

        [EmailAddress, MaxLength(255)]
        public string? Email { get; set; }

        [MaxLength(120)]
        public string? City { get; set; }

        [MaxLength(120)]
        public string? District { get; set; }

        [MaxLength(500)]
        public string? AddressDetails { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
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

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var pharmacyId = GetCurrentEntityId();
        if (pharmacyId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var pharmacy = await _db.Pharmacies
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == pharmacyId.Value, ct);

        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });

        return Ok(new
        {
            data = ToPharmacyMeDto(pharmacy)
        });
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdatePharmacyProfileDto dto, CancellationToken ct)
    {
        var pharmacyId = GetCurrentEntityId();
        if (pharmacyId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var pharmacy = await _db.Pharmacies
            .SingleOrDefaultAsync(p => p.Id == pharmacyId.Value, ct);

        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var normalizedEmail = dto.Email.Trim();
            var emailExists = await _db.Pharmacies
                .AnyAsync(p => p.Email == normalizedEmail && p.Id != pharmacy.Id, ct);
            if (emailExists)
                return Conflict(new { message = "HealUp: Pharmacy email already registered." });

            pharmacy.Email = normalizedEmail;
        }

        pharmacy.Phone = NormalizeOptional(dto.Phone);
        pharmacy.City = NormalizeOptional(dto.City);
        pharmacy.District = NormalizeOptional(dto.District);
        pharmacy.AddressDetails = NormalizeOptional(dto.AddressDetails);
        pharmacy.Latitude = dto.Latitude;
        pharmacy.Longitude = dto.Longitude;

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            message = "HealUp: Pharmacy profile updated successfully.",
            data = ToPharmacyMeDto(pharmacy)
        });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto, CancellationToken ct)
    {
        if (dto.NewPassword != dto.NewPasswordConfirmation)
            return BadRequest(new { message = "HealUp: New passwords do not match." });

        var pharmacyId = GetCurrentEntityId();
        if (pharmacyId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var pharmacy = await _db.Pharmacies
            .SingleOrDefaultAsync(p => p.Id == pharmacyId.Value, ct);

        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });

        if (!PasswordHasher.VerifyPassword(dto.CurrentPassword, pharmacy.PasswordHash))
            return BadRequest(new { message = "HealUp: Current password is incorrect.", field = "current_password" });

        pharmacy.PasswordHash = PasswordHasher.HashPassword(dto.NewPassword);
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "HealUp: Password changed successfully." });
    }

    [HttpGet("requests")]
    public async Task<IActionResult> IncomingRequests(CancellationToken ct)
    {
        var pharmacyId = GetCurrentEntityId();
        if (pharmacyId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var pharmacy = await _db.Pharmacies
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == pharmacyId.Value, ct);

        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });

        if (pharmacy.Status != "approved")
            return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending admin approval." });

        await DispatchPendingRequestWavesAsync(ct);

        var now = DateTime.UtcNow;
        var approvedPharmacies = await _db.Pharmacies
            .AsNoTracking()
            .Where(p => p.Status == "approved")
            .Select(p => new PharmacyDistanceRow(p.Id, p.Latitude, p.Longitude))
            .ToListAsync(ct);

        var requests = await _db.Requests
            .AsNoTracking()
            .Where(r => r.Status == "active" && r.ExpiresAt > now)
            .Where(r => !_db.PharmacyResponses.Any(pr => pr.RequestId == r.Id))
            .Where(r => !_db.PharmacyResponses.Any(pr => pr.RequestId == r.Id && pr.PharmacyId == pharmacyId.Value))
            .Where(r => !_db.PharmacyDeclinedRequests.Any(d => d.RequestId == r.Id && d.PharmacyId == pharmacyId.Value))
            .Include(r => r.Medicines)
            .Include(r => r.Patient)
            .OrderBy(r => r.ExpiresAt)
            .ToListAsync(ct);

        var eligibleRequests = requests.Where(req =>
        {
            var sortedPharmacyIds = OrderByDistance(approvedPharmacies, req.Patient.Latitude, req.Patient.Longitude).ToList();
            if (sortedPharmacyIds.Count == 0)
                return false;

            var targetCount = Math.Min(GetTargetNotifiedCount(req.CreatedAt, now), sortedPharmacyIds.Count);
            return sortedPharmacyIds.Take(targetCount).Contains(pharmacyId.Value);
        }).ToList();

        var results = new List<object>();
        foreach (var req in eligibleRequests)
        {
            var distance = await _maps.GetDistanceKmAsync(
                pharmacy.Latitude,
                pharmacy.Longitude,
                req.Patient.Latitude,
                req.Patient.Longitude,
                ct);

            results.Add(new
            {
                request = new
                {
                    id = req.Id,
                    status = req.Status,
                    expires_at = req.ExpiresAt,
                    created_at = req.CreatedAt,
                    prescription_url = req.PrescriptionUrl,
                    patient = new
                    {
                        id = req.Patient.Id,
                        name = req.Patient.Name
                    },
                    medicines = req.Medicines.Select(m => new
                    {
                        id = m.Id,
                        medicine_name = m.MedicineName,
                        quantity = m.Quantity
                    })
                },
                distance_km = distance
            });
        }

        return Ok(new { data = results });
    }

    [HttpPost("requests/{requestId:int}/decline")]
    public async Task<IActionResult> DeclineRequest([FromRoute] int requestId, CancellationToken ct)
    {
        var pharmacyId = GetCurrentEntityId();
        if (pharmacyId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var pharmacy = await _db.Pharmacies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pharmacyId.Value, ct);
        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });
        if (pharmacy.Status != "approved")
            return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending admin approval." });

        var exists = await _db.Requests.AsNoTracking().AnyAsync(r => r.Id == requestId && r.Status == "active" && r.ExpiresAt > DateTime.UtcNow, ct);
        if (!exists)
            return NotFound(new { message = "HealUp: Request not found or no longer active." });

        if (await _db.PharmacyDeclinedRequests.AnyAsync(d => d.PharmacyId == pharmacyId.Value && d.RequestId == requestId, ct))
            return Ok(new { message = "HealUp: Already declined." });

        _db.PharmacyDeclinedRequests.Add(new PharmacyDeclinedRequest
        {
            PharmacyId = pharmacyId.Value,
            RequestId = requestId,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "HealUp: Request hidden for your pharmacy." });
    }

    /// <summary>
    /// Active requests where this pharmacy submitted an offer and the patient has not created an order yet.
    /// Shown on current-orders as «بانتظار المريض».
    /// </summary>
    [HttpGet("awaiting-patient-orders")]
    public async Task<IActionResult> AwaitingPatientOrders(CancellationToken ct)
    {
        var pharmacyId = GetCurrentEntityId();
        if (pharmacyId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var pharmacy = await _db.Pharmacies.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == pharmacyId.Value, ct);
        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });
        if (!string.Equals(pharmacy.Status, "approved", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending admin approval." });

        var now = DateTime.UtcNow;

        var requestIdsWithOurOffers = await _db.PharmacyResponses
            .AsNoTracking()
            .Where(pr => pr.PharmacyId == pharmacyId.Value)
            .Select(pr => pr.RequestId)
            .Distinct()
            .ToListAsync(ct);

        if (requestIdsWithOurOffers.Count == 0)
            return Ok(new { data = Array.Empty<object>() });

        var requestIdsWithOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => requestIdsWithOurOffers.Contains(o.RequestId))
            .Select(o => o.RequestId)
            .Distinct()
            .ToListAsync(ct);

        var waitingRequestIds = requestIdsWithOurOffers
            .Where(id => !requestIdsWithOrders.Contains(id))
            .Distinct()
            .ToList();

        var reqs = await _db.Requests
            .AsNoTracking()
            .Where(r => waitingRequestIds.Contains(r.Id)
                        && r.Status == "active"
                        && r.ExpiresAt > now)
            .Include(r => r.Patient)
            .Include(r => r.Medicines)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var list = new List<object>();
        foreach (var r in reqs)
        {
            var latest = await _db.PharmacyResponses
                .AsNoTracking()
                .Where(pr => pr.RequestId == r.Id && pr.PharmacyId == pharmacyId.Value)
                .OrderByDescending(pr => pr.CreatedAt)
                .Select(pr => new { pr.Id, pr.CreatedAt })
                .FirstOrDefaultAsync(ct);

            if (latest is null)
                continue;

            list.Add(new
            {
                request_id = r.Id,
                response_id = latest.Id,
                created_at = r.CreatedAt,
                patient_name = r.Patient?.Name,
                medicines = r.Medicines.Select(m => new
                {
                    medicine_name = m.MedicineName,
                    quantity = m.Quantity
                })
            });
        }

        return Ok(new { data = list });
    }

    private int? GetCurrentEntityId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : null;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task DispatchPendingRequestWavesAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var activeRequests = await _db.Requests
            .Include(r => r.Patient)
            .Where(r => r.Status == "active" && r.ExpiresAt > now)
            .Where(r => !_db.PharmacyResponses.Any(pr => pr.RequestId == r.Id))
            .ToListAsync(ct);

        if (activeRequests.Count == 0)
            return;

        var approvedPharmacies = await _db.Pharmacies
            .AsNoTracking()
            .Where(p => p.Status == "approved")
            .Select(p => new PharmacyDistanceRow(p.Id, p.Latitude, p.Longitude))
            .ToListAsync(ct);

        if (approvedPharmacies.Count == 0)
            return;

        var didUpdate = false;

        foreach (var request in activeRequests)
        {
            var sortedPharmacyIds = OrderByDistance(
                    approvedPharmacies,
                    request.Patient.Latitude,
                    request.Patient.Longitude)
                .ToList();

            if (sortedPharmacyIds.Count == 0)
                continue;

            var targetCount = Math.Min(GetTargetNotifiedCount(request.CreatedAt, now), sortedPharmacyIds.Count);
            var alreadyNotified = Math.Clamp(request.NotifiedPharmacyCount, 0, sortedPharmacyIds.Count);

            if (targetCount <= alreadyNotified)
                continue;

            var newlyReleasedPharmacyIds = sortedPharmacyIds
                .Skip(alreadyNotified)
                .Take(targetCount - alreadyNotified)
                .ToList();

            foreach (var pharmacyId in newlyReleasedPharmacyIds)
            {
                await _notifications.NotifyPharmacyAsync(
                    pharmacyId,
                    "new_request",
                    $"HealUp: New request #{request.Id} is now available.",
                    "/pharmacy-dashboard/new-orders",
                    new { request_id = request.Id },
                    ct);
            }

            request.NotifiedPharmacyCount = targetCount;
            didUpdate = true;
        }

        if (didUpdate)
            await _db.SaveChangesAsync(ct);
    }

    private static int GetTargetNotifiedCount(DateTime createdAt, DateTime now)
    {
        var elapsedMinutes = Math.Max(0d, (now - createdAt).TotalMinutes);
        var waveIndex = (int)Math.Floor(elapsedMinutes / DispatchWindowMinutes);
        return (waveIndex + 1) * DispatchBatchSize;
    }

    private static IEnumerable<int> OrderByDistance(
        IEnumerable<PharmacyDistanceRow> pharmacies,
        double? fromLat,
        double? fromLon)
    {
        if (fromLat is null || fromLon is null)
        {
            return pharmacies
                .OrderBy(p => p.PharmacyId)
                .Select(p => p.PharmacyId);
        }

        return pharmacies
            .OrderBy(p => HaversineKm(fromLat.Value, fromLon.Value, p.Latitude, p.Longitude))
            .ThenBy(p => p.PharmacyId)
            .Select(p => p.PharmacyId);
    }

    private static double HaversineKm(double fromLat, double fromLon, double? toLat, double? toLon)
    {
        if (toLat is null || toLon is null)
            return double.MaxValue;

        const double r = 6371d;
        var dLat = ToRadians(toLat.Value - fromLat);
        var dLon = ToRadians(toLon.Value - fromLon);
        var lat1 = ToRadians(fromLat);
        var lat2 = ToRadians(toLat.Value);

        var a = Math.Pow(Math.Sin(dLat / 2d), 2d)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2d), 2d);
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return r * c;
    }

    private static double ToRadians(double degrees) => degrees * (Math.PI / 180d);

    private static object ToPharmacyMeDto(Pharmacy pharmacy) => new
    {
        id = pharmacy.Id,
        name = pharmacy.Name,
        email = pharmacy.Email,
        phone = pharmacy.Phone,
        license_number = pharmacy.LicenseNumber,
        responsible_pharmacist_name = string.IsNullOrWhiteSpace(pharmacy.ResponsiblePharmacistName)
            ? pharmacy.Name
            : pharmacy.ResponsiblePharmacistName,
        city = pharmacy.City,
        district = pharmacy.District,
        address_details = pharmacy.AddressDetails,
        latitude = pharmacy.Latitude,
        longitude = pharmacy.Longitude
    };
}
