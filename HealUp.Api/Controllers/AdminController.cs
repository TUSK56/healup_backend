using HealUp.Api.Data;
using HealUp.Api.Models;
using HealUp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    private readonly HealUpDbContext _db;
    private readonly NotificationService _notifications;

    public AdminController(HealUpDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    [HttpGet("pharmacies")]
    public async Task<IActionResult> ListPharmacies(CancellationToken ct)
    {
        var data = await _db.Pharmacies
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id,
                name = p.Name,
                email = p.Email,
                phone = p.Phone,
                license_number = p.LicenseNumber,
                city = p.City,
                district = p.District,
                address_details = p.AddressDetails,
                status = p.Status,
                created_at = p.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new { data });
    }

    [HttpPatch("pharmacies/{id:int}/approve")]
    public async Task<IActionResult> ApprovePharmacy([FromRoute] int id, CancellationToken ct)
    {
        var pharmacy = await _db.Pharmacies.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });

        pharmacy.Status = "approved";
        await _db.SaveChangesAsync(ct);

        await _notifications.NotifyPharmacyAsync(
            pharmacy.Id,
            "pharmacy_approved",
            "HealUp: Your pharmacy account has been approved.",
            "/pharmacy-dashboard",
            null,
            ct);

        return Ok(new { message = "HealUp: Pharmacy approved successfully." });
    }

    [HttpPatch("pharmacies/{id:int}/disable")]
    public async Task<IActionResult> DisablePharmacy([FromRoute] int id, CancellationToken ct)
    {
        var pharmacy = await _db.Pharmacies.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });

        pharmacy.Status = "disabled";
        await _db.SaveChangesAsync(ct);

        await _notifications.NotifyPharmacyAsync(
            pharmacy.Id,
            "pharmacy_disabled",
            "HealUp: Your pharmacy account has been disabled. Contact admin support.",
            "/pharmacy-dashboard",
            null,
            ct);

        return Ok(new { message = "HealUp: Pharmacy disabled successfully." });
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(CancellationToken ct)
    {
        var data = await _db.Patients
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new
            {
                id = u.Id,
                name = u.Name,
                email = u.Email,
                phone = u.Phone,
                role = "patient",
                created_at = u.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new { data });
    }

    [HttpGet("orders")]
    public async Task<IActionResult> ListOrders(CancellationToken ct)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Pharmacy)
            .Include(o => o.Patient)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        return Ok(new
        {
            data = orders.Select(ToOrderDto)
        });
    }

    private static object ToOrderDto(Order order) => new
    {
        id = order.Id,
        status = order.Status,
        total_price = order.TotalPrice,
        created_at = order.CreatedAt,
        patient = order.Patient is null ? null : new
        {
            id = order.Patient.Id,
            name = order.Patient.Name
        },
        pharmacy = order.Pharmacy is null ? null : new
        {
            id = order.Pharmacy.Id,
            name = order.Pharmacy.Name
        },
        items = order.Items.Select(i => new
        {
            medicine_name = i.MedicineName,
            quantity = i.Quantity,
            price = i.Price
        })
    };
}
