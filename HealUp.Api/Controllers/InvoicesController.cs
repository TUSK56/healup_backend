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
        if (latestOffer is not null && request.Medicines.Count > 0)
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

        var displayTotal = subtotal ?? request.EstimatedTotal ?? 0m;
        var pharmacyName = latestOffer?.Pharmacy?.Name;
        var createdAt = request.CreatedAt.ToString("yyyy-MM-dd HH:mm");

        TryRegisterArabicFont();

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(24);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(12));

                page.Header()
                    .Row(row =>
                    {
                        row.RelativeItem().AlignRight().Text($"فاتورة طلب #HLP-{request.Id}").FontSize(18).SemiBold();
                        row.ConstantItem(10);
                        row.RelativeItem().AlignLeft().Text($"تاريخ: {createdAt}").FontSize(10).FontColor(Colors.Grey.Darken1);
                    });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    col.Item().AlignRight().Text(pharmacyName is { Length: > 0 } ? $"الصيدلية: {pharmacyName}" : "الصيدلية: (بانتظار عرض)");
                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    col.Item().AlignRight().Text("الأدوية المطلوبة").FontSize(14).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4); // name
                            columns.RelativeColumn(2); // qty
                            columns.RelativeColumn(2); // unit
                            columns.RelativeColumn(2); // total
                        });

                        table.Header(header =>
                        {
                            header.Cell().AlignRight().Text("الدواء").SemiBold();
                            header.Cell().AlignCenter().Text("الكمية").SemiBold();
                            header.Cell().AlignCenter().Text("سعر القطعة").SemiBold();
                            header.Cell().AlignCenter().Text("الإجمالي").SemiBold();
                        });

                        foreach (var med in request.Medicines)
                        {
                            var unit = latestOffer?.Medicines.FirstOrDefault(m =>
                                string.Equals(m.MedicineName, med.MedicineName, StringComparison.OrdinalIgnoreCase))?.Price;
                            var lineTotal = unit.HasValue ? unit.Value * med.Quantity : (decimal?)null;

                            table.Cell().AlignRight().Text(med.MedicineName);
                            table.Cell().AlignCenter().Text(med.Quantity.ToString());
                            table.Cell().AlignCenter().Text(unit.HasValue ? unit.Value.ToString("0.00") : "—");
                            table.Cell().AlignCenter().Text(lineTotal.HasValue ? lineTotal.Value.ToString("0.00") : "—");
                        }
                    });

                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                    col.Item().Row(r =>
                    {
                        r.RelativeItem().AlignRight().Text("الإجمالي (تقديري/نهائي):").SemiBold();
                        r.RelativeItem().AlignLeft().Text($"{displayTotal:0.00} ج.م").FontSize(14).SemiBold();
                    });

                    col.Item().AlignRight().Text(latestOffer is null
                        ? "ملاحظة: هذه قيمة تقديرية حتى تقوم الصيدلية بتحديد السعر."
                        : "ملاحظة: تم تحديث السعر بناءً على آخر عرض من الصيدلية.")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken1);
                });

                page.Footer().AlignCenter().Text("HealUp").FontSize(10).FontColor(Colors.Grey.Darken1);
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
}

