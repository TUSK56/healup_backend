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
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly HealUpDbContext _db;
    private readonly NotificationService _notifications;

    public OrdersController(HealUpDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public class CreateOrderDto
    {
        [JsonPropertyName("response_id")]
        [Range(1, int.MaxValue)]
        public int ResponseId { get; set; }

        [JsonPropertyName("delivery")]
        public bool Delivery { get; set; } = true;
    }

    public class UpdateOrderStatusDto
    {
        [JsonPropertyName("order_id")]
        [Range(1, int.MaxValue)]
        public int OrderId { get; set; }

        [JsonPropertyName("status")]
        [Required, MaxLength(32)]
        public string Status { get; set; } = string.Empty;
    }

    [HttpPost]
    [Authorize(Roles = "patient")]
    public async Task<IActionResult> Create([FromBody] CreateOrderDto dto, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var response = await _db.PharmacyResponses
            .Include(r => r.Request)
            .Include(r => r.Pharmacy)
            .Include(r => r.Medicines)
            .SingleOrDefaultAsync(r => r.Id == dto.ResponseId, ct);

        if (response is null)
            return NotFound(new { message = "HealUp: Offer not found." });

        if (response.Request.PatientId != patientId.Value)
            return StatusCode(403, new { message = "HealUp: You can only order from your own requests." });

        var existingOrder = await _db.Orders.AnyAsync(o => o.RequestId == response.RequestId, ct);
        if (existingOrder)
            return Conflict(new { message = "HealUp: An order already exists for this request." });

        var selectedMedicines = response.Medicines
            .Where(m => m.Available && m.QuantityAvailable > 0)
            .ToList();
        if (selectedMedicines.Count == 0)
            return BadRequest(new { message = "HealUp: Selected offer has no available medicines." });

        var items = selectedMedicines.Select(m => new OrderItem
        {
            MedicineName = m.MedicineName,
            Quantity = m.QuantityAvailable,
            Price = m.Price
        }).ToList();

        var subtotal = items.Sum(i => i.Price * i.Quantity);
        var deliveryFee = dto.Delivery ? response.DeliveryFee : 0;

        var order = new Order
        {
            PatientId = patientId.Value,
            PharmacyId = response.PharmacyId,
            RequestId = response.RequestId,
            Delivery = dto.Delivery,
            DeliveryFee = deliveryFee,
            TotalPrice = subtotal + deliveryFee,
            Status = "pending_pharmacy_confirmation",
            Items = items
        };

        response.Request.Status = "confirmed";

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        await _notifications.NotifyPharmacyAsync(
            order.PharmacyId,
            "new_order",
            $"HealUp: New order #{order.Id} was placed and waiting for your confirmation.",
            "/pharmacy-dashboard/new-orders",
            new { order_id = order.Id },
            ct);

        var hydrated = await QueryOrders().SingleAsync(o => o.Id == order.Id, ct);
        return Ok(new
        {
            message = "HealUp: Order created successfully.",
            order = ToOrderDto(hydrated)
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var id = GetCurrentEntityId();
        if (id is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var role = User.FindFirstValue(ClaimTypes.Role);
        var query = QueryOrders().AsNoTracking();

        if (string.Equals(role, "pharmacy", StringComparison.OrdinalIgnoreCase))
        {
            var pharmacy = await _db.Pharmacies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id.Value, ct);
            if (pharmacy is null)
                return NotFound(new { message = "HealUp: Pharmacy not found." });
            if (pharmacy.Status != "approved")
                return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending admin approval." });

            query = query.Where(o => o.PharmacyId == id.Value);
        }
        else if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            // all orders
        }
        else
        {
            query = query.Where(o => o.PatientId == id.Value);
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        return Ok(new { data = orders.Select(ToOrderDto) });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct)
    {
        var entityId = GetCurrentEntityId();
        if (entityId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var role = User.FindFirstValue(ClaimTypes.Role);
        var query = QueryOrders().AsNoTracking();

        if (string.Equals(role, "pharmacy", StringComparison.OrdinalIgnoreCase))
        {
            var pharmacy = await _db.Pharmacies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == entityId.Value, ct);
            if (pharmacy is null)
                return NotFound(new { message = "HealUp: Pharmacy not found." });
            if (pharmacy.Status != "approved")
                return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending admin approval." });

            query = query.Where(o => o.PharmacyId == entityId.Value);
        }
        else if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(o => o.PatientId == entityId.Value);
        }

        var order = await query.SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            return NotFound(new { message = "HealUp: Order not found." });

        return Ok(ToOrderDto(order));
    }

    [HttpPatch("status")]
    [Authorize(Roles = "pharmacy")]
    public async Task<IActionResult> UpdateStatus([FromBody] UpdateOrderStatusDto dto, CancellationToken ct)
    {
        var pharmacyId = GetCurrentEntityId();
        if (pharmacyId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var pharmacy = await _db.Pharmacies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pharmacyId.Value, ct);
        if (pharmacy is null)
            return NotFound(new { message = "HealUp: Pharmacy not found." });

        if (pharmacy.Status != "approved")
            return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending admin approval." });

        var order = await QueryOrders().SingleOrDefaultAsync(o => o.Id == dto.OrderId && o.PharmacyId == pharmacyId.Value, ct);
        if (order is null)
            return NotFound(new { message = "HealUp: Order not found." });

        if (!IsValidPharmacyTransition(order.Status, dto.Status))
            return BadRequest(new { message = "HealUp: Invalid order status transition." });

        order.Status = dto.Status;
        await _db.SaveChangesAsync(ct);

        var route = string.Equals(order.Status, "confirmed", StringComparison.OrdinalIgnoreCase)
            ? $"/patient-order-confirmation?id={order.Id}"
            : $"/patient-order-tracking?id={order.Id}";
        var type = string.Equals(order.Status, "confirmed", StringComparison.OrdinalIgnoreCase)
            ? "order_confirmed_by_pharmacy"
            : "order_status_updated";

        await _notifications.NotifyPatientAsync(
            order.PatientId,
            type,
            $"HealUp: Order #{order.Id} is now {order.Status}.",
            route,
            new { order_id = order.Id, status = order.Status },
            ct);

        return Ok(new
        {
            message = "HealUp: Order status updated successfully.",
            order = ToOrderDto(order)
        });
    }

    [HttpPatch("{orderId:int}/patient-confirm")]
    [Authorize(Roles = "patient")]
    public async Task<IActionResult> PatientConfirm([FromRoute] int orderId, CancellationToken ct)
    {
        var patientId = GetCurrentEntityId();
        if (patientId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var order = await QueryOrders().SingleOrDefaultAsync(o => o.Id == orderId && o.PatientId == patientId.Value, ct);
        if (order is null)
            return NotFound(new { message = "HealUp: Order not found." });

        if (!string.Equals(order.Status, "confirmed", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "HealUp: Order is not ready for patient confirmation." });

        order.Status = "preparing";
        await _db.SaveChangesAsync(ct);

        await _notifications.NotifyPharmacyAsync(
            order.PharmacyId,
            "patient_confirmed_order",
            $"HealUp: Patient confirmed order #{order.Id}. Start preparing it.",
            "/pharmacy-dashboard/current-orders",
            new { order_id = order.Id, status = order.Status },
            ct);

        return Ok(new
        {
            message = "HealUp: Order confirmed successfully.",
            order = ToOrderDto(order)
        });
    }

    private static bool IsValidPharmacyTransition(string currentStatus, string nextStatus)
    {
        if (string.IsNullOrWhiteSpace(nextStatus))
            return false;

        var current = currentStatus.Trim().ToLowerInvariant();
        var next = nextStatus.Trim().ToLowerInvariant();
        return (current, next) switch
        {
            ("pending_pharmacy_confirmation", "confirmed") => true,
            ("pending_pharmacy_confirmation", "rejected") => true,
            ("preparing", "out_for_delivery") => true,
            ("preparing", "ready_for_pickup") => true,
            ("out_for_delivery", "completed") => true,
            ("ready_for_pickup", "completed") => true,
            _ => false
        };
    }

    private IQueryable<Order> QueryOrders() => _db.Orders
        .Include(o => o.Pharmacy)
        .Include(o => o.Patient)
        .Include(o => o.Items);

    private static object ToOrderDto(Order order) => new
    {
        id = order.Id,
        patient_id = order.PatientId,
        pharmacy_id = order.PharmacyId,
        request_id = order.RequestId,
        delivery = order.Delivery,
        delivery_fee = order.DeliveryFee,
        total_price = order.TotalPrice,
        status = order.Status,
        created_at = order.CreatedAt,
        pharmacy = order.Pharmacy is null ? null : new
        {
            id = order.Pharmacy.Id,
            name = order.Pharmacy.Name,
            latitude = order.Pharmacy.Latitude,
            longitude = order.Pharmacy.Longitude
        },
        patient = order.Patient is null ? null : new
        {
            id = order.Patient.Id,
            name = order.Patient.Name,
            latitude = order.Patient.Latitude,
            longitude = order.Patient.Longitude
        },
        items = order.Items.Select(i => new
        {
            id = i.Id,
            medicine_name = i.MedicineName,
            quantity = i.Quantity,
            price = i.Price
        })
    };

    private int? GetCurrentEntityId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : null;
    }
}
