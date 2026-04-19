using HealUp.Api.Data;
using HealUp.Api.Hubs;
using HealUp.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Services;

public class NotificationService
{
    private readonly HealUpDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;

    public NotificationService(HealUpDbContext db, IHubContext<NotificationHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task NotifyPatientAsync(
        int patientId,
        string type,
        string message,
        string route,
        object? payload,
        CancellationToken ct)
    {
        _db.Notifications.Add(new Notification
        {
            PatientId = patientId,
            Type = type,
            Message = message,
            IsRead = false,
            TargetRoute = string.IsNullOrWhiteSpace(route) ? null : route.Trim()
        });

        await _db.SaveChangesAsync(ct);

        await _hub.Clients.Group($"healup.patient.{patientId}")
            .SendAsync("HealUpNotification", new
            {
                type,
                message,
                route,
                payload
            }, ct);
    }

    public async Task NotifyPharmacyAsync(
        int pharmacyId,
        string type,
        string message,
        string route,
        object? payload,
        CancellationToken ct)
    {
        _db.Notifications.Add(new Notification
        {
            PharmacyId = pharmacyId,
            Type = type,
            Message = message,
            IsRead = false,
            TargetRoute = string.IsNullOrWhiteSpace(route) ? null : route.Trim()
        });

        await _db.SaveChangesAsync(ct);

        await _hub.Clients.Group($"healup.pharmacy.{pharmacyId}")
            .SendAsync("HealUpNotification", new
            {
                type,
                message,
                route,
                payload
            }, ct);
    }

    public async Task NotifyAdminAsync(
        int adminId,
        string type,
        string message,
        string route,
        object? payload,
        CancellationToken ct)
    {
        _db.Notifications.Add(new Notification
        {
            AdminId = adminId,
            Type = type,
            Message = message,
            IsRead = false,
            TargetRoute = string.IsNullOrWhiteSpace(route) ? null : route.Trim()
        });

        await _db.SaveChangesAsync(ct);

        await _hub.Clients.Group($"healup.admin.{adminId}")
            .SendAsync("HealUpNotification", new
            {
                type,
                message,
                route,
                payload
            }, ct);
    }

    public async Task NotifyAllAdminsAsync(
        string type,
        string message,
        string route,
        object? payload,
        CancellationToken ct)
    {
        var adminIds = await _db.Admins
            .AsNoTracking()
            .Select(a => a.Id)
            .ToListAsync(ct);

        foreach (var adminId in adminIds)
        {
            _db.Notifications.Add(new Notification
            {
                AdminId = adminId,
                Type = type,
                Message = message,
                IsRead = false,
                TargetRoute = string.IsNullOrWhiteSpace(route) ? null : route.Trim()
            });
        }

        if (adminIds.Count > 0)
            await _db.SaveChangesAsync(ct);

        foreach (var adminId in adminIds)
        {
            await _hub.Clients.Group($"healup.admin.{adminId}")
                .SendAsync("HealUpNotification", new
                {
                    type,
                    message,
                    route,
                    payload
                }, ct);
        }
    }
}
