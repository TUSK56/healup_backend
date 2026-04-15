using HealUp.Api.Data;
using HealUp.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HealUp.Api.Services;

/// <summary>
/// One-time demo data: 5 patients, 5 approved pharmacies, orders for analytics (matches local dev).
/// Skips if <c>patient1@demo.healup.local</c> already exists.
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoPatient1Email = "patient1@demo.healup.local";

    /// <summary>Startup path: runs only when <c>DemoSeed:Enabled</c> is true.</summary>
    public static async Task SeedAsync(HealUpDbContext db, IConfiguration configuration, CancellationToken ct = default)
    {
        if (!configuration.GetValue("DemoSeed:Enabled", false))
            return;

        var password = configuration["DemoSeed:Password"] ?? "Demo@2026";
        await TrySeedDemoDataAsync(db, password, ct);
    }

    /// <summary>
    /// Inserts demo rows if not already present. Used by startup (when enabled) or one-time HTTP setup.
    /// </summary>
    /// <returns><c>(true, "created")</c> if data was inserted; <c>(false, "already_seeded")</c> if skipped.</returns>
    public static async Task<(bool Inserted, string Detail)> TrySeedDemoDataAsync(
        HealUpDbContext db,
        string demoPassword,
        CancellationToken ct = default)
    {
        if (await db.Patients.AsNoTracking().AnyAsync(p => p.Email == DemoPatient1Email, ct))
            return (false, "already_seeded");

        var hash = PasswordHasher.HashPassword(demoPassword);

        var patientNames = new[]
        {
            "أحمد محمد", "سارة أحمد", "خالد علي", "ليلى حسن", "يوسف إبراهيم"
        };

        var pharmacyNames = new[]
        {
            "صيدلية النهدي", "صيدلية الدواء", "صيدلية الشفاء", "صيدلية العزيزية", "صيدلية المستقبل"
        };

        var patients = new List<Patient>();
        for (var i = 0; i < 5; i++)
        {
            patients.Add(new Patient
            {
                Name = patientNames[i],
                Email = $"patient{i + 1}@demo.healup.local",
                Phone = $"0100000000{i}",
                PasswordHash = hash,
                Latitude = 30.0444 + i * 0.01,
                Longitude = 31.2357 + i * 0.01
            });
        }

        db.Patients.AddRange(patients);

        var pharmacies = new List<Pharmacy>();
        for (var i = 0; i < 5; i++)
        {
            pharmacies.Add(new Pharmacy
            {
                Name = pharmacyNames[i],
                Email = $"pharmacy{i + 1}@demo.healup.local",
                Phone = $"020000000{i}",
                LicenseNumber = $"LIC-DEMO-{i + 1:000}",
                PasswordHash = hash,
                Latitude = 30.05 + i * 0.008,
                Longitude = 31.24 + i * 0.008,
                Status = "approved"
            });
        }

        db.Pharmacies.AddRange(pharmacies);
        await db.SaveChangesAsync(ct);

        var p = patients.Select(x => x.Id).ToArray();
        var ph = pharmacies.Select(x => x.Id).ToArray();

        var now = DateTime.UtcNow;

        // (patientIdx, pharmacyIdx, status, daysAgo, hoursOffset, medicines as lines)
        var specs = new List<(int Pi, int Phi, string Status, double DaysAgo, (string Name, int Qty, decimal Price)[] Lines)>
        {
            (0, 0, "completed", 6, new[] { ("بانادول إكسترا", 2, 45m), ("فيتامين سي", 1, 120m) }),
            (1, 0, "completed", 5, new[] { ("أوميبرازول", 1, 85m) }),
            (2, 1, "completed", 4, new[] { ("أوجمنتين", 2, 195m), ("شاش طبي", 3, 15m) }),
            (3, 1, "completed", 3, new[] { ("بروفين", 1, 42m) }),
            (4, 2, "completed", 2, new[] { ("كونجستال", 2, 55m) }),
            (0, 2, "completed", 1, new[] { ("فولتارين جل", 1, 78m) }),
            (1, 3, "completed", 0, new[] { ("بانادول نايت", 1, 38m) }),
            (2, 3, "completed", 0.5, new[] { ("مضاد حيوي", 1, 150m) }),
            (3, 4, "completed", 7, new[] { ("فيتامين د", 1, 95m) }),
            (4, 4, "completed", 5, new[] { ("أوميجا 3", 1, 220m) }),
            (0, 1, "out_for_delivery", 0.1, new[] { ("بانادول", 2, 30m) }),
            (1, 2, "ready_for_pickup", 0.05, new[] { ("شراب سعال", 1, 48m) }),
            (2, 0, "preparing", 0.02, new[] { ("قطرة عين", 1, 65m) }),
            (3, 4, "confirmed", 0.01, new[] { ("مرهم", 1, 55m) }),
            (4, 1, "pending_pharmacy_confirmation", 0, new[] { ("مسكن", 2, 25m) }),
            (0, 3, "rejected", 1, new[] { ("دواء نادر", 1, 500m) }),
            (1, 4, "completed", 3.5, new[] { ("كريم واقي", 1, 90m) }),
            (2, 4, "completed", 2.5, new[] { ("محلول", 2, 35m) }),
            (3, 0, "completed", 1.2, new[] { ("أقراص مغص", 1, 28m) }),
            (4, 2, "completed", 0.8, new[] { ("مكمل غذائي", 1, 180m) }),
            (0, 4, "completed", 4, new[] { ("شامبو طبي", 1, 110m) }),
            (1, 0, "completed", 2, new[] { ("لاصق جروح", 5, 8m) }),
            (2, 1, "preparing", 0.03, new[] { ("شراب فيتامين", 1, 72m) }),
            (3, 2, "out_for_delivery", 0.08, new[] { ("بخاخ", 1, 95m) }),
            (4, 3, "completed", 6, new[] { ("كحول طبي", 2, 22m) })
        };

        foreach (var spec in specs)
        {
            var createdAt = now.AddDays(-spec.DaysAgo);
            await AddOrderAsync(db, p[spec.Pi], ph[spec.Phi], spec.Status, createdAt, spec.Lines, 25m, ct);
        }

        await db.SaveChangesAsync(ct);
        return (true, "created");
    }

    private static async Task AddOrderAsync(
        HealUpDbContext db,
        int patientId,
        int pharmacyId,
        string status,
        DateTime createdAt,
        (string Name, int Qty, decimal Price)[] lines,
        decimal deliveryFee,
        CancellationToken ct)
    {
        var request = new MedicineRequest
        {
            PatientId = patientId,
            PrescriptionUrl = null,
            Status = "confirmed",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = createdAt,
            Medicines = lines.Select(l => new RequestMedicine
            {
                MedicineName = l.Name,
                Quantity = l.Qty
            }).ToList()
        };

        db.Requests.Add(request);
        await db.SaveChangesAsync(ct);

        var response = new PharmacyResponse
        {
            PharmacyId = pharmacyId,
            RequestId = request.Id,
            DeliveryFee = deliveryFee,
            CreatedAt = createdAt,
            Medicines = lines.Select(l => new ResponseMedicine
            {
                MedicineName = l.Name,
                Available = true,
                QuantityAvailable = l.Qty,
                Price = l.Price
            }).ToList()
        };

        db.PharmacyResponses.Add(response);
        await db.SaveChangesAsync(ct);

        var subtotal = lines.Sum(l => l.Price * l.Qty);
        var order = new Order
        {
            PatientId = patientId,
            PharmacyId = pharmacyId,
            RequestId = request.Id,
            Delivery = true,
            DeliveryFee = deliveryFee,
            TotalPrice = subtotal + deliveryFee,
            Status = status,
            CreatedAt = createdAt,
            Items = lines.Select(l => new OrderItem
            {
                MedicineName = l.Name,
                Quantity = l.Qty,
                Price = l.Price
            }).ToList()
        };

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
    }
}
