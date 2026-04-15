using System.ComponentModel.DataAnnotations;

namespace HealUp.Api.Models;

public class Patient
{
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Name { get; set; } = default!;

    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = default!;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [Required]
    public string PasswordHash { get; set; } = default!;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MedicineRequest> Requests { get; set; } = new List<MedicineRequest>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class Admin
{
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Name { get; set; } = default!;

    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = default!;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [Required]
    public string PasswordHash { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Pharmacy
{
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Name { get; set; } = default!;

    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = default!;

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? LicenseNumber { get; set; }

    [Required]
    public string PasswordHash { get; set; } = default!;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>pending | approved | disabled</summary>
    [Required, MaxLength(32)]
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PharmacyResponse> Responses { get; set; } = new List<PharmacyResponse>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class MedicineRequest
{
    public int Id { get; set; }

    [Required]
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = default!;

    public string? PrescriptionUrl { get; set; }

    /// <summary>active | expired | confirmed | cancelled</summary>
    [Required, MaxLength(32)]
    public string Status { get; set; } = "active";

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RequestMedicine> Medicines { get; set; } = new List<RequestMedicine>();
    public ICollection<PharmacyResponse> PharmacyResponses { get; set; } = new List<PharmacyResponse>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class RequestMedicine
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public MedicineRequest Request { get; set; } = default!;

    [Required, MaxLength(255)]
    public string MedicineName { get; set; } = default!;

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;
}

public class PharmacyResponse
{
    public int Id { get; set; }

    public int PharmacyId { get; set; }
    public Pharmacy Pharmacy { get; set; } = default!;

    public int RequestId { get; set; }
    public MedicineRequest Request { get; set; } = default!;

    [Range(0, double.MaxValue)]
    public decimal DeliveryFee { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ResponseMedicine> Medicines { get; set; } = new List<ResponseMedicine>();
}

public class ResponseMedicine
{
    public int Id { get; set; }

    public int ResponseId { get; set; }
    public PharmacyResponse Response { get; set; } = default!;

    [Required, MaxLength(255)]
    public string MedicineName { get; set; } = default!;

    public bool Available { get; set; } = true;

    [Range(0, int.MaxValue)]
    public int QuantityAvailable { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }
}

public class Order
{
    public int Id { get; set; }

    public int PatientId { get; set; }
    public Patient Patient { get; set; } = default!;

    public int PharmacyId { get; set; }
    public Pharmacy Pharmacy { get; set; } = default!;

    public int RequestId { get; set; }
    public MedicineRequest Request { get; set; } = default!;

    public bool Delivery { get; set; }

    [Range(0, double.MaxValue)]
    public decimal DeliveryFee { get; set; }

    [Range(0, double.MaxValue)]
    public decimal TotalPrice { get; set; }

    /// <summary>waiting_responses | confirmed | preparing | out_for_delivery | ready_for_pickup | completed</summary>
    [Required, MaxLength(32)]
    public string Status { get; set; } = "waiting_responses";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;

    [Required, MaxLength(255)]
    public string MedicineName { get; set; } = default!;

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }
}

public class Notification
{
    public int Id { get; set; }

    public int? PatientId { get; set; }
    public Patient? Patient { get; set; }

    public int? PharmacyId { get; set; }
    public Pharmacy? Pharmacy { get; set; }

    [Required, MaxLength(64)]
    public string Type { get; set; } = default!;

    [Required, MaxLength(1000)]
    public string Message { get; set; } = default!;

    /// <summary>Optional deep link (e.g. <c>/patient-order-tracking?id=12</c>). When null, API falls back to type-based routes.</summary>
    [MaxLength(512)]
    public string? TargetRoute { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

