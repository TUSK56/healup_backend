using System.Security.Claims;
using HealUp.Api.Data;
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

    public PharmacyController(HealUpDbContext db, GoogleMapsService maps)
    {
        _db = db;
        _maps = maps;
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

        var requests = await _db.Requests
            .AsNoTracking()
            .Where(r => r.Status == "active" && r.ExpiresAt > DateTime.UtcNow)
            .Where(r => !_db.PharmacyResponses.Any(pr => pr.RequestId == r.Id && pr.PharmacyId == pharmacyId.Value))
            .Include(r => r.Medicines)
            .Include(r => r.Patient)
            .OrderBy(r => r.ExpiresAt)
            .ToListAsync(ct);

        var results = new List<object>();
        foreach (var req in requests)
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

    private int? GetCurrentEntityId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : null;
    }
}
