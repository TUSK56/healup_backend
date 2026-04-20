using HealUp.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HealUp.Api.Data;

public class HealUpDbContext : DbContext
{
    public HealUpDbContext(DbContextOptions<HealUpDbContext> options) : base(options)
    {
    }

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Pharmacy> Pharmacies => Set<Pharmacy>();
    public DbSet<MedicineRequest> Requests => Set<MedicineRequest>();
    public DbSet<RequestMedicine> RequestMedicines => Set<RequestMedicine>();
    public DbSet<PharmacyResponse> PharmacyResponses => Set<PharmacyResponse>();
    public DbSet<ResponseMedicine> ResponseMedicines => Set<ResponseMedicine>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PatientAddress> PatientAddresses => Set<PatientAddress>();
    public DbSet<PharmacyDeclinedRequest> PharmacyDeclinedRequests => Set<PharmacyDeclinedRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Patient>(e =>
        {
            e.ToTable("patients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.Property(x => x.Email).IsRequired().HasMaxLength(255);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.AvatarUrl).HasMaxLength(1000);
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Admin>(e =>
        {
            e.ToTable("admins");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.Property(x => x.Email).IsRequired().HasMaxLength(255);
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Pharmacy>(e =>
        {
            e.ToTable("pharmacies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.Property(x => x.Email).IsRequired().HasMaxLength(255);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.LicenseNumber).HasMaxLength(100);
            e.Property(x => x.ResponsiblePharmacistName).HasMaxLength(255);
            e.Property(x => x.City).HasMaxLength(120);
            e.Property(x => x.District).HasMaxLength(120);
            e.Property(x => x.AddressDetails).HasMaxLength(500);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Status).IsRequired().HasMaxLength(32);
        });

        modelBuilder.Entity<MedicineRequest>(e =>
        {
            e.ToTable("requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsRequired().HasMaxLength(32);
            e.Property(x => x.ExpiresAt).IsRequired();
            e.Property(x => x.EstimatedTotal).HasPrecision(18, 2);
            e.Property(x => x.NotifiedPharmacyCount).HasDefaultValue(0);
            e.HasIndex(x => new { x.PatientId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.ExpiresAt });
            e.HasOne(x => x.Patient)
                .WithMany(x => x.Requests)
                .HasForeignKey(x => x.PatientId);
        });

        modelBuilder.Entity<RequestMedicine>(e =>
        {
            e.ToTable("request_medicines");
            e.HasKey(x => x.Id);
            e.Property(x => x.MedicineName).IsRequired().HasMaxLength(255);
            e.HasOne(x => x.Request)
                .WithMany(x => x.Medicines)
                .HasForeignKey(x => x.RequestId);
        });

        modelBuilder.Entity<PharmacyResponse>(e =>
        {
            e.ToTable("pharmacy_responses");
            e.HasKey(x => x.Id);
            e.Property(x => x.DeliveryFee).HasPrecision(18, 2);
            e.HasIndex(x => new { x.RequestId, x.CreatedAt });
            e.HasIndex(x => new { x.PharmacyId, x.RequestId });
            e.HasOne(x => x.Pharmacy)
                .WithMany(x => x.Responses)
                .HasForeignKey(x => x.PharmacyId);
            e.HasOne(x => x.Request)
                .WithMany(x => x.PharmacyResponses)
                .HasForeignKey(x => x.RequestId);
        });

        modelBuilder.Entity<ResponseMedicine>(e =>
        {
            e.ToTable("response_medicines");
            e.HasKey(x => x.Id);
            e.Property(x => x.MedicineName).IsRequired().HasMaxLength(255);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasOne(x => x.Response)
                .WithMany(x => x.Medicines)
                .HasForeignKey(x => x.ResponseId);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsRequired().HasMaxLength(32);
            e.Property(x => x.DeliveryFee).HasPrecision(18, 2);
            e.Property(x => x.TotalPrice).HasPrecision(18, 2);
            e.HasIndex(x => new { x.PatientId, x.CreatedAt });
            e.HasIndex(x => new { x.PharmacyId, x.CreatedAt });
            e.HasIndex(x => new { x.RequestId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne(x => x.Patient)
                .WithMany(x => x.Orders)
                .HasForeignKey(x => x.PatientId);
            e.HasOne(x => x.Pharmacy)
                .WithMany(x => x.Orders)
                .HasForeignKey(x => x.PharmacyId);
            e.HasOne(x => x.Request)
                .WithMany(x => x.Orders)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.NoAction);
            e.Property(x => x.PaymentMethod).HasMaxLength(256);
            e.Property(x => x.DeliveryAddressSnapshot).HasMaxLength(500);
        });

        modelBuilder.Entity<PharmacyDeclinedRequest>(e =>
        {
            e.ToTable("pharmacy_declined_requests");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PharmacyId, x.RequestId }).IsUnique();
            e.HasOne(x => x.Pharmacy)
                .WithMany(x => x.DeclinedRequests)
                .HasForeignKey(x => x.PharmacyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Request)
                .WithMany(x => x.DeclineRecords)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.MedicineName).IsRequired().HasMaxLength(255);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasOne(x => x.Order)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OrderId);
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).IsRequired().HasMaxLength(64);
            e.Property(x => x.Message).IsRequired().HasMaxLength(1000);
            e.Property(x => x.TargetRoute).HasMaxLength(512);
            e.HasIndex(x => new { x.PatientId, x.IsRead, x.CreatedAt });
            e.HasIndex(x => new { x.PharmacyId, x.IsRead, x.CreatedAt });
            e.HasIndex(x => new { x.AdminId, x.IsRead, x.CreatedAt });
            e.HasOne(x => x.Admin)
                .WithMany(x => x.Notifications)
                .HasForeignKey(x => x.AdminId);
        });

        modelBuilder.Entity<PatientAddress>(e =>
        {
            e.ToTable("patient_addresses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).IsRequired().HasMaxLength(80);
            // DB uses snake_case for these two (common with hand-written / older scripts); map explicitly so SELECT/INSERT match.
            e.Property(x => x.IconKey).IsRequired().HasMaxLength(32).HasColumnName("icon_key");
            e.Property(x => x.City).HasMaxLength(120);
            e.Property(x => x.District).HasMaxLength(120);
            e.Property(x => x.AddressDetails).HasMaxLength(500).HasColumnName("address_details");
            e.HasOne(x => x.Patient)
                .WithMany(x => x.Addresses)
                .HasForeignKey(x => x.PatientId);
        });
    }

    public async Task<int> ExpireOldRequestsAsync()
    {
        var now = DateTime.UtcNow;
        var toExpire = Requests.Where(r => r.Status == "active" && r.ExpiresAt <= now);
        foreach (var r in toExpire)
        {
            r.Status = "expired";
        }
        return await SaveChangesAsync();
    }
}

