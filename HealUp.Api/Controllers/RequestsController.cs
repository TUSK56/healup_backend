using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using HealUp.Api.Data;
using HealUp.Api.Models;
using HealUp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api/requests")]
[Authorize(Roles = "patient")]
public class RequestsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HealUpDbContext _db;
    private readonly CloudinaryService _cloudinary;
    private readonly NotificationService _notifications;
    private const int DispatchBatchSize = 10;

    private sealed record PharmacyDistanceRow(int PharmacyId, double? Latitude, double? Longitude);

    public RequestsController(HealUpDbContext db, CloudinaryService cloudinary, NotificationService notifications)
    {
        _db = db;
        _cloudinary = cloudinary;
        _notifications = notifications;
    }

    public class MedicineInputDto
    {
        [JsonPropertyName("medicine_name")]
        [Required, MaxLength(255)]
        public string MedicineName { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }
    }

    public class CreateRequestForm
    {
        [FromForm(Name = "prescription")]
        public IFormFile? Prescription { get; set; }

        [FromForm(Name = "prescription_url")]
        public string? PrescriptionUrl { get; set; }

        [FromForm(Name = "medicines")]
        public string Medicines { get; set; } = "[]";

        [FromForm(Name = "estimated_total")]
        public decimal? EstimatedTotal { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromForm] CreateRequestForm form, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        List<MedicineInputDto>? medicines;
        try
        {
            medicines = JsonSerializer.Deserialize<List<MedicineInputDto>>(form.Medicines, JsonOptions);
        }
        catch
        {
            return BadRequest(new { message = "HealUp: Invalid medicines payload." });
        }

        medicines ??= new List<MedicineInputDto>();
        medicines = medicines.Where(m => !string.IsNullOrWhiteSpace(m.MedicineName)).ToList();
        if (medicines.Any(m => m.Quantity < 1))
            return BadRequest(new { message = "HealUp: Medicine quantity must be at least 1." });

        var prescriptionUrl = form.PrescriptionUrl;
        if (form.Prescription is not null)
        {
            prescriptionUrl = await _cloudinary.UploadPrescriptionAsync(form.Prescription, ct);
        }

        var hasPrescription = !string.IsNullOrWhiteSpace(prescriptionUrl);
        if (medicines.Count == 0 && !hasPrescription)
            return BadRequest(new { message = "HealUp: Add at least one medicine or prescription." });

        var request = new MedicineRequest
        {
            PatientId = patientId.Value,
            PrescriptionUrl = prescriptionUrl,
            EstimatedTotal = form.EstimatedTotal,
            Status = "active",
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            Medicines = medicines.Select(m => new RequestMedicine
            {
                MedicineName = m.MedicineName.Trim(),
                Quantity = m.Quantity
            }).ToList()
        };

        _db.Requests.Add(request);
        await _db.SaveChangesAsync(ct);

        var patientLocation = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Id == patientId.Value)
            .Select(p => new { p.Latitude, p.Longitude })
            .SingleOrDefaultAsync(ct);

        var approvedPharmacies = await _db.Pharmacies
            .AsNoTracking()
            .Where(p => p.Status == "approved")
            .Select(p => new PharmacyDistanceRow(p.Id, p.Latitude, p.Longitude))
            .ToListAsync(ct);

        var initialWavePharmacyIds = OrderByDistance(
                approvedPharmacies,
                patientLocation?.Latitude,
                patientLocation?.Longitude)
            .Take(DispatchBatchSize)
            .ToList();

        foreach (var pharmacyId in initialWavePharmacyIds)
        {
            await _notifications.NotifyPharmacyAsync(
                pharmacyId,
                "new_request",
                $"HealUp: New request #{request.Id} is now available.",
                "/pharmacy-dashboard/new-orders",
                new { request_id = request.Id },
                ct);
        }

            request.NotifiedPharmacyCount = initialWavePharmacyIds.Count;
            await _db.SaveChangesAsync(ct);

        var hydrated = await _db.Requests
            .AsNoTracking()
            .Include(r => r.Medicines)
            .SingleAsync(r => r.Id == request.Id, ct);

        return Created($"/api/requests/{request.Id}", new
        {
            message = "HealUp: Request created successfully.",
            request = ToRequestDto(hydrated)
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var data = await _db.Requests
            .AsNoTracking()
            .Where(r => r.PatientId == patientId.Value)
            .Include(r => r.Medicines)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return Ok(new
        {
            data = data.Select(r =>
            {
                return new
                {
                    id = r.Id,
                    patient_id = r.PatientId,
                    prescription_url = r.PrescriptionUrl,
                    estimated_total = r.EstimatedTotal,
                    status = r.Status,
                    expires_at = r.ExpiresAt,
                    created_at = r.CreatedAt,
                    has_offers = false,
                    latest_offer_response_id = (int?)null,
                    latest_pharmacy_name = "بانتظار اختيار الصيدلية",
                    latest_offer_grand_total = (decimal?)null,
                    uses_latest_offer_pricing = false,
                    medicines = r.Medicines.Select(m => new
                    {
                        id = m.Id,
                        medicine_name = m.MedicineName,
                        quantity = m.Quantity
                    })
                };
            })
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var request = await _db.Requests
            .AsNoTracking()
            .Where(r => r.Id == id && r.PatientId == patientId.Value)
            .Include(r => r.Medicines)
            .SingleOrDefaultAsync(ct);

        if (request is null)
            return NotFound(new { message = "HealUp: Request not found." });

        return Ok(ToRequestDto(request));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Cancel([FromRoute] int id, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var request = await _db.Requests
            .Include(r => r.Orders)
            .SingleOrDefaultAsync(r => r.Id == id && r.PatientId == patientId.Value, ct);

        if (request is null)
            return NotFound(new { message = "HealUp: Request not found." });

        if (request.Status != "active")
            return BadRequest(new { message = "HealUp: Only active requests can be cancelled." });

        if (request.Orders.Any())
            return BadRequest(new { message = "HealUp: This request already has an order and cannot be cancelled." });

        request.Status = "cancelled";
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "HealUp: Request cancelled successfully." });
    }

    private int? GetCurrentEntityId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : null;
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

    private static object ToRequestDto(MedicineRequest request) => new
    {
        id = request.Id,
        patient_id = request.PatientId,
        prescription_url = request.PrescriptionUrl,
        estimated_total = request.EstimatedTotal,
        status = request.Status,
        expires_at = request.ExpiresAt,
        created_at = request.CreatedAt,
        medicines = request.Medicines.Select(m => new
        {
            id = m.Id,
            medicine_name = m.MedicineName,
            quantity = m.Quantity
        })
    };
}
