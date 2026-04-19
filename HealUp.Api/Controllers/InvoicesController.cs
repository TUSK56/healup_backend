using System.Security.Claims;
using HealUp.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Drawing;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HealUp.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly HealUpDbContext _db;

    public InvoicesController(HealUpDbContext db)
    {
        _db = db;
    }

    [HttpGet("requests/{requestId:int}/invoice")]
    public async Task<IActionResult> RequestInvoice([FromRoute] int requestId, CancellationToken ct)
    {
        var entityId = GetCurrentEntityId();
        if (entityId is null)
            return Unauthorized(new { message = "HealUp: Invalid token subject." });

        var role = (User.FindFirstValue(ClaimTypes.Role) ?? string.Empty).Trim().ToLowerInvariant();

        if (role == "pharmacy")
        {
            var pharmacy = await _db.Pharmacies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == entityId.Value, ct);
            if (pharmacy is null)
                return NotFound(new { message = "HealUp: Pharmacy not found." });
            if (pharmacy.Status != "approved")
                return StatusCode(403, new { message = "HealUp: Your pharmacy account is pending admin approval." });
        }

        var requestQuery = _db.Requests
            .AsNoTracking()
            .Where(r => r.Id == requestId)
            .Include(r => r.Medicines)
            .AsQueryable();

        if (role == "patient")
        {
            requestQuery = requestQuery.Where(r => r.PatientId == entityId.Value);
        }
        else if (role == "pharmacy")
        {
            requestQuery = requestQuery.Where(r => _db.Orders.Any(o => o.RequestId == r.Id && o.PharmacyId == entityId.Value));
        }
        // admin can access any request invoice

        var request = await requestQuery.SingleOrDefaultAsync(ct);

        if (request is null)
            return NotFound(new { message = "HealUp: Request not found." });

        var latestOrderQuery = _db.Orders
            .AsNoTracking()
            .Where(o => o.RequestId == requestId)
            .Include(o => o.Pharmacy)
            .Include(o => o.Items)
            .AsQueryable();

        if (role == "pharmacy")
            latestOrderQuery = latestOrderQuery.Where(o => o.PharmacyId == entityId.Value);

        var latestOrder = await latestOrderQuery
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var latestOfferQuery = _db.PharmacyResponses
            .AsNoTracking()
            .Where(r => r.RequestId == requestId)
            .Include(r => r.Pharmacy)
            .Include(r => r.Medicines)
            .AsQueryable();

        if (role == "pharmacy")
            latestOfferQuery = latestOfferQuery.Where(r => r.PharmacyId == entityId.Value);

        var latestOffer = await latestOfferQuery
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        decimal? subtotal = null;
        if (latestOrder is not null && latestOrder.Items.Count > 0)
        {
            subtotal = latestOrder.Items.Sum(i => i.Price * i.Quantity);
        }
        else if (latestOffer is not null && request.Medicines.Count > 0)
        {
            subtotal = 0;
            foreach (var reqMed in request.Medicines)
            {
                var matched = latestOffer.Medicines.FirstOrDefault(m =>
                    string.Equals(m.MedicineName, reqMed.MedicineName, StringComparison.OrdinalIgnoreCase));
                if (matched is null) continue;
                subtotal += matched.Price * reqMed.Quantity;
            }
        }

        var qtySum = latestOrder?.Items.Sum(i => i.Quantity)
            ?? request.Medicines.Sum(m => m.Quantity);
        var deliveryFee = latestOrder?.DeliveryFee ?? (qtySum >= 5 ? 0m : 25m);
        var couponPercent = latestOrder?.CouponPercent is > 0m and <= 100m
            ? latestOrder.CouponPercent.Value
            : 0m;
        var couponCode = string.IsNullOrWhiteSpace(latestOrder?.CouponCode) ? null : latestOrder!.CouponCode!.Trim();

        var baseSubtotal = subtotal ?? request.EstimatedTotal ?? 0m;
        var discountAmount = Math.Round(baseSubtotal * (couponPercent / 100m), 2, MidpointRounding.AwayFromZero);
        var subtotalAfterDiscount = Math.Max(0m, baseSubtotal - discountAmount);
        var vatAmount = Math.Round(subtotalAfterDiscount * 0.15m, 2, MidpointRounding.AwayFromZero);
        var displayTotal = latestOrder?.TotalPrice ?? (subtotalAfterDiscount + deliveryFee + vatAmount);

        var pharmacyName = latestOrder?.Pharmacy?.Name ?? latestOffer?.Pharmacy?.Name;
        var orderStatusLabel = ToArabicOrderStatus(latestOrder?.Status);
        var createdAt = request.CreatedAt.ToString("yyyy-MM-dd HH:mm");

        TryRegisterArabicFont();

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(24);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(12));

                page.Background().Layers(layers =>
                {
                    layers.Layer().Element(bg =>
                    {
                        bg.Background(Colors.Grey.Lighten5)
                            .Row(r =>
                            {
                                r.ConstantItem(14).Background(Colors.Blue.Darken2);
                                r.RelativeItem().AlignCenter().AlignMiddle().Text("HealUp")
                                    .FontSize(92)
                                    .SemiBold()
                                    .FontColor(Colors.Blue.Lighten5);
                            });
                    });
                });

                page.Header().Element(header =>
                {
                    header.Background(Colors.White)
                        .Border(1.2f)
                        .BorderColor(Colors.Blue.Lighten4)
                        .Padding(14)
                        .Row(row =>
                        {
                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text("فاتورة شراء إلكترونية").FontSize(22).SemiBold().FontColor(Colors.Blue.Darken3);
                                c.Item().PaddingTop(2).Text($"رقم الفاتورة: HLP-{request.Id}").FontSize(11).FontColor(Colors.Grey.Darken2);
                                c.Item().Text($"تاريخ الإصدار: {createdAt}").FontSize(10).FontColor(Colors.Grey.Darken2);
                            });

                            row.ConstantItem(16);

                            row.RelativeItem().AlignLeft().Row(logo =>
                            {
                                logo.ConstantItem(52).Height(52)
                                    .Background(Colors.Blue.Darken3)
                                    .AlignCenter()
                                    .AlignMiddle()
                                    .Text("H+")
                                    .FontSize(16)
                                    .SemiBold()
                                    .FontColor(Colors.White);
                                logo.ConstantItem(8);
                                logo.RelativeItem().AlignMiddle().Column(b =>
                                {
                                    b.Item().Text("HealUp").FontSize(21).SemiBold().FontColor(Colors.Blue.Darken3);
                                    b.Item().Text("Healthcare Invoice").FontSize(9).FontColor(Colors.Grey.Darken1);
                                });
                            });
                        });
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Background(Colors.Blue.Lighten5).Border(1).BorderColor(Colors.Blue.Lighten3).Padding(10).Row(r =>
                    {
                        r.RelativeItem().AlignRight().Text(pharmacyName is { Length: > 0 } ? $"الصيدلية: {pharmacyName}" : "الصيدلية: (بانتظار عرض)").FontSize(12).SemiBold();
                        r.RelativeItem().AlignLeft().Text($"حالة الطلب: {orderStatusLabel}").FontSize(12).SemiBold().FontColor(Colors.Blue.Darken3);
                    });

                    col.Item().Background(Colors.White).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(section =>
                    {
                        section.Spacing(8);
                        section.Item().AlignRight().Text("تفاصيل الأدوية").FontSize(15).SemiBold().FontColor(Colors.Blue.Darken3);

                        section.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Blue.Lighten4).Padding(6).AlignRight().Text("الدواء").SemiBold();
                                header.Cell().Background(Colors.Blue.Lighten4).Padding(6).AlignCenter().Text("الكمية").SemiBold();
                                header.Cell().Background(Colors.Blue.Lighten4).Padding(6).AlignCenter().Text("سعر القطعة").SemiBold();
                                header.Cell().Background(Colors.Blue.Lighten4).Padding(6).AlignCenter().Text("الإجمالي").SemiBold();
                            });

                            foreach (var med in request.Medicines)
                            {
                                var unit = latestOffer?.Medicines.FirstOrDefault(m =>
                                    string.Equals(m.MedicineName, med.MedicineName, StringComparison.OrdinalIgnoreCase))?.Price;
                                if (latestOrder is not null)
                                    unit = latestOrder.Items.FirstOrDefault(i => string.Equals(i.MedicineName, med.MedicineName, StringComparison.OrdinalIgnoreCase))?.Price;
                                var lineTotal = unit.HasValue ? unit.Value * med.Quantity : (decimal?)null;

                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(7).AlignRight().Text(med.MedicineName);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(7).AlignCenter().Text(med.Quantity.ToString());
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(7).AlignCenter().Text(unit.HasValue ? unit.Value.ToString("0.00") : "—");
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(7).AlignCenter().Text(lineTotal.HasValue ? lineTotal.Value.ToString("0.00") : "—");
                            }
                        });
                    });

                    col.Item().Background(Colors.White).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).Column(sum =>
                    {
                        sum.Spacing(7);

                        sum.Item().Row(r =>
                        {
                            r.RelativeItem().AlignRight().Text("المجموع الفرعي:").SemiBold();
                            r.RelativeItem().AlignLeft().Text($"{baseSubtotal:0.00} ج.م");
                        });

                        if (discountAmount > 0m)
                        {
                            var discountLabel = couponCode is { Length: > 0 }
                                ? $"الخصم ({couponCode} - {couponPercent:0.##}%):"
                                : "الخصم:";

                            sum.Item().Row(r =>
                            {
                                r.RelativeItem().AlignRight().Text(discountLabel).SemiBold();
                                r.RelativeItem().AlignLeft().Text($"-{discountAmount:0.00} ج.م").FontColor(Colors.Green.Darken2);
                            });
                        }

                        sum.Item().Row(r =>
                        {
                            r.RelativeItem().AlignRight().Text("رسوم التوصيل:").SemiBold();
                            r.RelativeItem().AlignLeft().Text($"{deliveryFee:0.00} ج.م");
                        });

                        sum.Item().Row(r =>
                        {
                            r.RelativeItem().AlignRight().Text("ضريبة القيمة المضافة (15%):").SemiBold();
                            r.RelativeItem().AlignLeft().Text($"{vatAmount:0.00} ج.م");
                        });

                        sum.Item().PaddingTop(6).Background(Colors.Blue.Lighten5).Border(1).BorderColor(Colors.Blue.Lighten3).Padding(8).Row(r =>
                        {
                            r.RelativeItem().AlignRight().Text("الإجمالي:").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken2);
                            r.RelativeItem().AlignLeft().Text($"{displayTotal:0.00} ج.م").FontSize(16).SemiBold().FontColor(Colors.Blue.Darken2);
                        });
                    });

                    col.Item().AlignRight().Text(latestOffer is null
                        ? "ملاحظة: هذه قيمة تقديرية حتى تقوم الصيدلية بتحديد السعر."
                        : "ملاحظة: تم تحديث السعر بناءً على بيانات الطلب المؤكد وآخر عرض متاح.")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken1);
                });

                page.Footer().PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().AlignRight().Text("شكراً لاستخدام HealUp").FontSize(10).FontColor(Colors.Grey.Darken1);
                    r.RelativeItem().AlignLeft().Text("www.healup.local").FontSize(10).FontColor(Colors.Blue.Darken2);
                });
            });
        }).GeneratePdf();

        return File(pdfBytes, "application/pdf", $"healup-receipt-request-{request.Id}.pdf");
    }

    private int? GetCurrentEntityId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(value, out var id) ? id : null;
    }

    private static void TryRegisterArabicFont()
    {
        // Use Windows system font that supports Arabic. This avoids bundling font binaries in repo.
        try
        {
            var arial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
            if (System.IO.File.Exists(arial))
                FontManager.RegisterFont(System.IO.File.OpenRead(arial));
        }
        catch
        {
            // best-effort only
        }
    }

    private static string ToArabicOrderStatus(string? status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "pending_pharmacy_confirmation" => "بانتظار تأكيد الصيدلية",
            "confirmed" => "تم التأكيد",
            "preparing" => "قيد التجهيز",
            "out_for_delivery" => "خارج للتوصيل",
            "ready_for_pickup" => "جاهز للاستلام",
            "completed" => "تم التسليم",
            "rejected" => "مرفوض",
            _ => "بانتظار التأكيد"
        };
    }
}

