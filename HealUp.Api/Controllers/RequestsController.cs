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
        if (medicines.Count == 0)
            return BadRequest(new { message = "HealUp: Add at least one medicine." });

        if (medicines.Any(m => m.Quantity < 1))
            return BadRequest(new { message = "HealUp: Medicine quantity must be at least 1." });

        var prescriptionUrl = form.PrescriptionUrl;
        if (form.Prescription is not null)
        {
            prescriptionUrl = await _cloudinary.UploadPrescriptionAsync(form.Prescription, ct);
        }

        var request = new MedicineRequest
        {
            PatientId = patientId.Value,
            PrescriptionUrl = prescriptionUrl,
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

        var pharmacyIds = await _db.Pharmacies
            .AsNoTracking()
            .Where(p => p.Status == "approved")
            .Select(p => p.Id)
            .ToListAsync(ct);

        foreach (var pharmacyId in pharmacyIds)
        {
            await _notifications.NotifyPharmacyAsync(
                pharmacyId,
                "new_request",
                $"HealUp: New request #{request.Id} is now available.",
                "/pharmacy-dashboard/new-orders",
                new { request_id = request.Id },
                ct);
        }

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

        return Ok(new { data = data.Select(ToRequestDto) });
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

    private static object ToRequestDto(MedicineRequest request) => new
    {
        id = request.Id,
        patient_id = request.PatientId,
        prescription_url = request.PrescriptionUrl,
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
