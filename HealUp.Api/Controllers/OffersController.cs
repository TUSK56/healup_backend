using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using HealUp.Api.Data;
using HealUp.Api.Models;
using HealUp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class OffersController : ControllerBase
{
    private readonly HealUpDbContext _db;
    private readonly GoogleMapsService _maps;
    private readonly NotificationService _notifications;

    public OffersController(HealUpDbContext db, GoogleMapsService maps, NotificationService notifications)
    {
        _db = db;
        _maps = maps;
        _notifications = notifications;
    }

    public class ResponseMedicineDto
    {
        [JsonPropertyName("medicine_name")]
        [Required, MaxLength(255)]
        public string MedicineName { get; set; } = string.Empty;

        [JsonPropertyName("available")]
        public bool Available { get; set; }

        [JsonPropertyName("quantity_available")]
        [Range(0, int.MaxValue)]
        public int QuantityAvailable { get; set; }

        [JsonPropertyName("price")]
        [Range(0, double.MaxValue)]
        public decimal Price { get; set; }
    }

    public class SubmitResponseDto
    {
        [JsonPropertyName("request_id")]
        [Range(1, int.MaxValue)]
        public int RequestId { get; set; }

        [JsonPropertyName("delivery_fee")]
        [Range(0, double.MaxValue)]
        public decimal DeliveryFee { get; set; }

        [JsonPropertyName("medicines")]
        [MinLength(1)]
        public List<ResponseMedicineDto> Medicines { get; set; } = new();
    }

    [HttpPost("pharmacy/respond")]
    [Authorize(Roles = "pharmacy")]
    public async Task<IActionResult> Submit([FromBody] SubmitResponseDto dto, CancellationToken ct)
    {
        var pharmacyId = GetCurrentEntityId();
        if (pharmacyId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var pharmacy = await _db.Pharmacies.SingleOrDefaultAsync(p => p.Id == pharmacyId.Value, ct);
        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });

        if (pharmacy.Status != "approved")
            return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending admin approval." });

        var request = await _db.Requests
            .Include(r => r.Medicines)
            .SingleOrDefaultAsync(r => r.Id == dto.RequestId, ct);

        if (request is null)
            return NotFound(new { message = "HealUp: Request not found." });

        if (request.Status != "active" || request.ExpiresAt <= DateTime.UtcNow)
            return BadRequest(new { message = "HealUp: This request is no longer active." });

        var exists = await _db.PharmacyResponses
            .AnyAsync(pr => pr.RequestId == request.Id && pr.PharmacyId == pharmacy.Id, ct);
        if (exists)
            return Conflict(new { message = "HealUp: You already responded to this request." });

        var knownMedicines = request.Medicines.Select(m => m.MedicineName.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (dto.Medicines.Any(m => !knownMedicines.Contains(m.MedicineName.Trim())))
            return BadRequest(new { message = "HealUp: Response includes a medicine not present in the request." });

        var response = new PharmacyResponse
        {
            PharmacyId = pharmacy.Id,
            RequestId = request.Id,
            DeliveryFee = dto.DeliveryFee,
            Medicines = dto.Medicines.Select(m => new ResponseMedicine
            {
                MedicineName = m.MedicineName.Trim(),
                Available = m.Available,
                QuantityAvailable = m.QuantityAvailable,
                Price = m.Price
            }).ToList()
        };

        _db.PharmacyResponses.Add(response);
        await _db.SaveChangesAsync(ct);

        await _notifications.NotifyPatientAsync(
            request.PatientId,
            "new_offer",
            $"HealUp: New offer for request #{request.Id} from {pharmacy.Name}.",
            "/patient-review-orders",
            new { request_id = request.Id, response_id = response.Id },
            ct);

        return Ok(new
        {
            message = "HealUp: Response submitted successfully.",
            response = new
            {
                id = response.Id,
                pharmacy_id = response.PharmacyId,
                request_id = response.RequestId,
                delivery_fee = response.DeliveryFee,
                created_at = response.CreatedAt
            }
        });
    }

    [HttpGet("requests/{requestId:int}/offers")]
    [Authorize(Roles = "patient")]
    public async Task<IActionResult> List([FromRoute] int requestId, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var request = await _db.Requests
            .AsNoTracking()
            .Include(r => r.Patient)
            .SingleOrDefaultAsync(r => r.Id == requestId && r.PatientId == patientId.Value, ct);

        if (request is null)
            return NotFound(new { message = "HealUp: Request not found." });

        var responses = await _db.PharmacyResponses
            .AsNoTracking()
            .Where(r => r.RequestId == requestId)
            .Include(r => r.Pharmacy)
            .Include(r => r.Medicines)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        var offers = new List<(double? Distance, object Payload)>();
        foreach (var response in responses)
        {
            var distance = await _maps.GetDistanceKmAsync(
                request.Patient.Latitude,
                request.Patient.Longitude,
                response.Pharmacy.Latitude,
                response.Pharmacy.Longitude,
                ct);

            offers.Add((distance, new
            {
                response = new
                {
                    id = response.Id,
                    pharmacy_id = response.PharmacyId,
                    request_id = response.RequestId,
                    delivery_fee = response.DeliveryFee,
                    created_at = response.CreatedAt,
                    pharmacy = new
                    {
                        id = response.Pharmacy.Id,
                        name = response.Pharmacy.Name,
                        latitude = response.Pharmacy.Latitude,
                        longitude = response.Pharmacy.Longitude
                    },
                    response_medicines = response.Medicines.Select(m => new
                    {
                        medicine_name = m.MedicineName,
                        available = m.Available,
                        quantity_available = m.QuantityAvailable,
                        price = m.Price
                    })
                },
                distance_km = distance
            }));
        }

        var sortedOffers = offers
            .OrderBy(o => o.Distance.HasValue ? 0 : 1)
            .ThenBy(o => o.Distance)
            .Select(o => o.Payload)
            .ToList();

        return Ok(new
        {
            request = new
            {
                id = request.Id,
                patient_id = request.PatientId,
                status = request.Status,
                expires_at = request.ExpiresAt,
                created_at = request.CreatedAt,
                prescription_url = request.PrescriptionUrl
            },
            offers = sortedOffers
        });
    }

    private int? GetCurrentEntityId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : null;
    }
}
