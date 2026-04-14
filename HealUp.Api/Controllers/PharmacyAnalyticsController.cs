using System.Security.Claims;
using HealUp.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api/pharmacy/analytics")]
[Authorize(Roles = "pharmacy")]
public class PharmacyAnalyticsController : ControllerBase
{
    private static readonly HashSet<string> InProgressStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending_pharmacy_confirmation",
        "confirmed",
        "preparing",
        "out_for_delivery",
        "ready_for_pickup"
    };

    private readonly HealUpDbContext _db;

    public PharmacyAnalyticsController(HealUpDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var pharmacyId = GetCurrentEntityId();
        if (pharmacyId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var pharmacy = await _db.Pharmacies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pharmacyId.Value, ct);
        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });
        if (pharmacy.Status != "approved")
            return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending admin approval." });

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.PharmacyId == pharmacyId.Value)
            .Include(o => o.Items)
            .ToListAsync(ct);

        var completed = orders.Where(o => string.Equals(o.Status, "completed", StringComparison.OrdinalIgnoreCase)).ToList();
        var totalRevenue = completed.Sum(o => o.TotalPrice);
        var today = DateTime.UtcNow.Date;
        var completedToday = completed.Count(o => o.CreatedAt.Date == today);
        var inProgress = orders.Count(o => InProgressStatuses.Contains(o.Status));
        var newOrders = orders.Count(o => string.Equals(o.Status, "pending_pharmacy_confirmation", StringComparison.OrdinalIgnoreCase));

        var revenueSeries = new List<object>();
        for (var i = 6; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            var sum = completed.Where(o => o.CreatedAt.Date == d).Sum(o => o.TotalPrice);
            revenueSeries.Add(new
            {
                name = d.ToString("MM/dd"),
                date = d,
                value = sum
            });
        }

        var topMedicines = completed
            .SelectMany(o => o.Items)
            .GroupBy(i => i.MedicineName)
            .Select(g => new
            {
                medicine_name = g.Key,
                orders = g.Count(),
                revenue = g.Sum(x => x.Price * x.Quantity)
            })
            .OrderByDescending(x => x.revenue)
            .Take(5)
            .ToList();

        var categories = completed
            .SelectMany(o => o.Items)
            .GroupBy(i => i.MedicineName)
            .Select(g => new
            {
                name = g.Key,
                orders = g.Count(),
                revenue = g.Sum(x => x.Price * x.Quantity)
            })
            .OrderByDescending(x => x.revenue)
            .Take(5)
            .ToList();

        return Ok(new
        {
            total_revenue = totalRevenue,
            completed_today = completedToday,
            orders_in_progress = inProgress,
            new_orders = newOrders,
            completed_total = completed.Count,
            average_order_value = completed.Count > 0 ? completed.Average(o => o.TotalPrice) : 0,
            revenue_last_7_days = revenueSeries,
            top_medicines = topMedicines,
            category_breakdown = categories
        });
    }

    private int? GetCurrentEntityId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : null;
    }
}
