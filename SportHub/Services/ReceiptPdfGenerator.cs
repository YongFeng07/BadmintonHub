using SportHub.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace SportHub.Services;

/// <summary>
/// Generates the PDF e-receipt for a paid booking (M3 feature, G-M5 upgrades:
/// verification QR and the site footer from SystemSettings).
/// QuestPDF Community licence — free under the revenue limits stated by the vendor.
/// Receipts are official records and always render in English (invariant culture):
/// the default QuestPDF font cannot render localized month names (e.g. 中文 "8月").
/// </summary>
public static class ReceiptPdfGenerator
{
    /// <param name="footerLine">
    /// Facility contact line shown at the bottom; reads from the SystemSettings
    /// "ReceiptFooter" key (falls back to the built-in address).
    /// </param>
    public static byte[] Generate(Reservation r, string? footerLine = null)
    {
        using var stream = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(48);

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("SportHub").FontSize(20).SemiBold().FontColor(Colors.Green.Darken3);
                            c.Item().Text("SportHub - Multi-Sport Facility Reservation System").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().PaddingTop(12).Text("Payment Receipt").FontSize(14).SemiBold();
                        });
                        row.ConstantItem(110);
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text(r.ReservationReference).FontSize(12).SemiBold();
                            c.Item().Text($"Issued: {DateTime.Now.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().PaddingTop(4).Text("Receipt No: " + r.Payment?.PaymentReference).FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    col.Item().PaddingTop(12).LineHorizontal(1).LineColor(Colors.Green.Darken3);
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().PaddingBottom(4).Text("Booking Details").FontSize(11).SemiBold();
                            c.Item().Row(r2 =>
                            {
                                r2.RelativeItem().Column(Item("Court", $"Court {r.Court?.CourtNumber} ({r.Court?.CourtType})"));
                                r2.RelativeItem().Column(Item("Facility", r.Court?.Facility?.Name ?? "—"));
                                r2.RelativeItem().Column(Item("Date", r.ReservationDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
                                r2.RelativeItem().Column(Item("Time", $"{r.StartTime:HH:mm} – {r.EndTime:HH:mm}"));
                            });
                            c.Item().Row(r2 =>
                            {
                                r2.RelativeItem().Column(Item("Member", r.User?.FullName ?? "—"));
                                r2.RelativeItem().Column(Item("Duration", $"{r.DurationHours:0.#} hour(s)"));
                                r2.RelativeItem().Column(Item("Status", r.Status.ToString()));
                                r2.RelativeItem().Column(Item("Payment Method", r.Payment?.Method.ToString() ?? "—"));
                            });
                        });
                        // G-M5: the same verification QR used on screen — it only ever
                        // encodes the public reservation reference.
                        row.ConstantItem(96);
                        row.RelativeItem().Column(c =>
                        {
                            var qrBytes = QrCodeHelper.GenerateBytes(r.ReservationReference);
                            c.Item().AlignCenter().Width(84).Height(84).Image(qrBytes);
                            c.Item().AlignCenter().Text("Scan to verify booking")
                                .FontSize(7).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    col.Item().PaddingTop(20).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Paid At").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().Text(r.Payment?.PaidAt?.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—").FontSize(11);
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            if (r.DiscountAmount > 0)
                            {
                                c.Item().Text("Booking Total").FontSize(9).FontColor(Colors.Grey.Darken1);
                                c.Item().Text($"RM {r.TotalAmount:0.00}").FontSize(11);
                                c.Item().PaddingTop(2)
                                    .Text($"Voucher discount ({r.VoucherCode})").FontSize(9).FontColor(Colors.Grey.Darken1);
                                c.Item().Text($"− RM {r.DiscountAmount:0.00}").FontSize(11).FontColor(Colors.Red.Medium);
                            }
                            c.Item().PaddingTop(4).Text("Amount Paid").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"RM {r.TotalAmount - r.DiscountAmount:0.00}")
                                .FontSize(18).SemiBold().FontColor(Colors.Green.Darken3);
                        });
                    });

                    col.Item().PaddingTop(28).Text(
                        "This receipt confirms that the reservation has been paid and the court booking is confirmed. " +
                        "Please present this receipt (or the booking QR code) at the facility counter.")
                        .FontSize(9).FontColor(Colors.Grey.Darken2);

                    static Action<ColumnDescriptor> Item(string label, string value) => c =>
                    {
                        c.Item().Text(label).FontSize(9).FontColor(Colors.Grey.Darken1);
                        c.Item().Text(value).FontSize(11).SemiBold();
                    };
                });

                page.Footer().AlignCenter().Column(col =>
                {
                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    col.Item().PaddingTop(4).Text(footerLine ?? "SportHub · 12 Jalan Ampang, 50450 Kuala Lumpur · 03-4142 8899 · info@sporthub.my")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf(stream);

        return stream.ToArray();
    }
}
