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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Patient>(e =>
        {
            e.ToTable("patients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(255);
            e.Property(x => x.Email).IsRequired().HasMaxLength(255);
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
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Status).IsRequired().HasMaxLength(32);
        });

        modelBuilder.Entity<MedicineRequest>(e =>
        {
            e.ToTable("requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsRequired().HasMaxLength(32);
            e.Property(x => x.ExpiresAt).IsRequired();
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

