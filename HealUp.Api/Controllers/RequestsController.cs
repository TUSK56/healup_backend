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
    public async Task<IActionResult> List([FromQuery] int? page_size, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        const int defaultPageSize = 100;
        const int maxPageSize = 200;
        var take = Math.Clamp(page_size ?? defaultPageSize, 1, maxPageSize);

        // Two-step: ids first (narrow index seek on PatientId), then details — faster on large DBs.
        var pageIds = await _db.Requests
            .AsNoTracking()
            .Where(r => r.PatientId == patientId.Value)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (pageIds.Count == 0)
        {
            return Ok(new { data = Array.Empty<object>(), page_size = take });
        }

        var rows = await _db.Requests
            .AsNoTracking()
            .Where(r => pageIds.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                r.PatientId,
                r.PrescriptionUrl,
                r.EstimatedTotal,
                r.Status,
                r.ExpiresAt,
                r.CreatedAt,
                Medicines = r.Medicines.Select(m => new { m.Id, m.MedicineName, m.Quantity })
            })
            .ToListAsync(ct);

        var byId = rows.ToDictionary(x => x.Id);
        var ordered = pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        // Latest offer per request (if any) — used by patient "My Orders" cards.
        var requestIds = ordered.Select(r => r.Id).ToList();
        var latestOffersRaw = await _db.PharmacyResponses
            .AsNoTracking()
            .Where(pr => requestIds.Contains(pr.RequestId))
            .Select(pr => new
            {
                pr.Id,
                pr.RequestId,
                pr.CreatedAt,
                PharmacyName = pr.Pharmacy.Name,
                Medicines = pr.Medicines.Select(m => new { m.MedicineName, m.Available, m.Price, m.QuantityAvailable })
            })
            .OrderByDescending(pr => pr.CreatedAt)
            .ToListAsync(ct);

        var latestOfferByRequestId = new Dictionary<int, object>();
        foreach (var offer in latestOffersRaw)
        {
            if (!latestOfferByRequestId.ContainsKey(offer.RequestId))
                latestOfferByRequestId[offer.RequestId] = offer;
        }

        return Ok(new
        {
            data = ordered.Select(r =>
            {
                var hasRx = !string.IsNullOrWhiteSpace(r.PrescriptionUrl);
                var hasReqMeds = r.Medicines.Any();

                // Pull latest offer (if exists) and compute grand total from unit prices.
                decimal? latestGrandTotal = null;
                var usesLatestPricing = false;
                var hasOffers = false;
                int? latestOfferId = null;
                string latestPharmacyName = "بانتظار اختيار الصيدلية";

                if (latestOfferByRequestId.TryGetValue(r.Id, out var boxed) && boxed is not null)
                {
                    dynamic offer = boxed;
                    hasOffers = true;
                    latestOfferId = (int)offer.Id;
                    latestPharmacyName = (string)offer.PharmacyName;

                    // Build requested quantities map (for explicit medicines). For prescription-only, fall back to offered quantities.
                    var reqQty = r.Medicines
                        .Where(m => !string.IsNullOrWhiteSpace(m.MedicineName))
                        .ToDictionary(m => m.MedicineName.Trim().ToLowerInvariant(), m => m.Quantity);

                    decimal subtotal = 0m;
                    int qtySum = 0;
                    foreach (var med in offer.Medicines)
                    {
                        if (!(bool)med.Available) continue;
                        var name = ((string)med.MedicineName ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var key = name.ToLowerInvariant();
                        var unit = (decimal)med.Price;
                        if (unit < 0) continue;

                        // For normal requests, patient quantity drives totals.
                        // For prescription-only, pharmacy enters quantities in the offer.
                        var q = reqQty.TryGetValue(key, out var requested) ? requested : (int)med.QuantityAvailable;
                        if (q < 1) continue;

                        subtotal += unit * q;
                        qtySum += q;
                    }

                    if (subtotal > 0m || (hasRx && !hasReqMeds))
                    {
                        // Same delivery + VAT rules as frontend (coupon handled later at order creation).
                        var deliveryFee = qtySum >= 5 ? 0m : 25m;
                        var tax = subtotal * 0.15m;
                        latestGrandTotal = subtotal + deliveryFee + tax;
                        usesLatestPricing = true;
                    }
                }

                return new
                {
                    id = r.Id,
                    patient_id = r.PatientId,
                    prescription_url = TruncatePrescriptionUrlForList(r.PrescriptionUrl),
                    estimated_total = r.EstimatedTotal,
                    status = r.Status,
                    expires_at = r.ExpiresAt,
                    created_at = r.CreatedAt,
                    has_offers = hasOffers,
                    latest_offer_response_id = latestOfferId,
                    latest_pharmacy_name = latestPharmacyName,
                    latest_offer_grand_total = latestGrandTotal,
                    uses_latest_offer_pricing = usesLatestPricing,
                    medicines = r.Medicines.Select(m => new
                    {
                        id = m.Id,
                        medicine_name = m.MedicineName,
                        quantity = m.Quantity
                    })
                };
            }),
            page_size = take
        });
    }

    private static string? TruncatePrescriptionUrlForList(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return url;
        const int max = 2048;
        return url.Length <= max ? url : null;
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
