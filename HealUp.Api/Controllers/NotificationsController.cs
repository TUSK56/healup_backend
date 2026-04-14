using System.Security.Claims;
using HealUp.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly HealUpDbContext _db;

    public NotificationsController(HealUpDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var entityId = GetCurrentEntityId();
        if (entityId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var query = _db.Notifications.AsNoTracking().AsQueryable();

        if (string.Equals(role, "pharmacy", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(n => n.PharmacyId == entityId.Value);
        }
        else
        {
            query = query.Where(n => n.PatientId == entityId.Value);
        }

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var unread = items.Count(n => !n.IsRead);
        return Ok(new
        {
            unread_count = unread,
            data = items.Select(n => new
            {
                id = n.Id,
                type = n.Type,
                message = n.Message,
                is_read = n.IsRead,
                created_at = n.CreatedAt,
                route = ResolveRoute(n.Type, role)
            })
        });
    }

    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead([FromRoute] int id, CancellationToken ct)
    {
        var entityId = GetCurrentEntityId();
        if (entityId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var notification = await _db.Notifications.SingleOrDefaultAsync(n => n.Id == id, ct);
        if (notification is null)
            return NotFound(new { message = "HealUp: Notification not found." });

        var allowed = string.Equals(role, "pharmacy", StringComparison.OrdinalIgnoreCase)
            ? notification.PharmacyId == entityId.Value
            : notification.PatientId == entityId.Value;
        if (!allowed)
            return StatusCode(403, new { message = "HealUp: You are not allowed to update this notification." });

        notification.IsRead = true;
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "HealUp: Notification marked as read." });
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var entityId = GetCurrentEntityId();
        if (entityId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var query = _db.Notifications.AsQueryable();
        if (string.Equals(role, "pharmacy", StringComparison.OrdinalIgnoreCase))
            query = query.Where(n => n.PharmacyId == entityId.Value && !n.IsRead);
        else
            query = query.Where(n => n.PatientId == entityId.Value && !n.IsRead);

        var items = await query.ToListAsync(ct);
        foreach (var item in items)
            item.IsRead = true;

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "HealUp: All notifications marked as read." });
    }

    private static string ResolveRoute(string type, string role)
    {
        if (string.Equals(role, "pharmacy", StringComparison.OrdinalIgnoreCase))
        {
            return type switch
            {
                "new_request" => "/pharmacy-dashboard/new-orders",
                _ => "/pharmacy-dashboard/new-orders"
            };
        }

        return type switch
        {
            "order_confirmed_by_pharmacy" => "/patient-order-confirmation",
            "order_status_updated" => "/patient-order-tracking",
            _ => "/patient-review-orders"
        };
    }

    private int? GetCurrentEntityId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : null;
    }
}
