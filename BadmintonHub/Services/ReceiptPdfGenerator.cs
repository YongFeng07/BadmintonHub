using BadmintonHub.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BadmintonHub.Services;

/// <summary>
/// Generates the PDF e-receipt for a paid booking (M3 feature).
/// QuestPDF Community licence — free under the revenue limits stated by the vendor.
/// </summary>
public static class ReceiptPdfGenerator
{
    public static byte[] Generate(Reservation r)
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
                            c.Item().Text("BadmintonHub").FontSize(20).SemiBold().FontColor(Colors.Green.Darken3);
                            c.Item().Text("Badminton Court Reservation System").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().PaddingTop(12).Text("Payment Receipt").FontSize(14).SemiBold();
                        });
                        row.ConstantItem(40);
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text(r.ReservationReference).FontSize(12).SemiBold();
                            c.Item().Text($"Issued: {DateTime.Now:dd MMM yyyy HH:mm}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().PaddingTop(4).Text("Receipt No: " + r.Payment?.PaymentReference).FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    col.Item().PaddingTop(12).LineHorizontal(1).LineColor(Colors.Green.Darken3);
                });

                page.Content().PaddingTop(24).Column(col =>
                {
                    col.Item().PaddingBottom(4).Text("Booking Details").FontSize(11).SemiBold();
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(Item("Court", $"Court {r.Court?.CourtNumber} ({r.Court?.CourtType})"));
                        row.RelativeItem().Column(Item("Facility", r.Court?.Facility?.Name ?? "—"));
                        row.RelativeItem().Column(Item("Date", r.ReservationDate.ToString("dd MMM yyyy")));
                        row.RelativeItem().Column(Item("Time", $"{r.StartTime:HH:mm} – {r.EndTime:HH:mm}"));
                    });
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(Item("Member", r.User?.FullName ?? "—"));
                        row.RelativeItem().Column(Item("Duration", $"{r.DurationHours:0.#} hour(s)"));
                        row.RelativeItem().Column(Item("Status", r.Status.ToString()));
                        row.RelativeItem().Column(Item("Payment Method", r.Payment?.Method.ToString() ?? "—"));
                    });

                    col.Item().PaddingTop(20).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Paid At").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().Text(r.Payment?.PaidAt?.ToString("dd MMM yyyy HH:mm") ?? "—").FontSize(11);
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text("Amount Paid").FontSize(9).FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"RM {r.TotalAmount:0.00}").FontSize(18).SemiBold().FontColor(Colors.Green.Darken3);
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
                    col.Item().PaddingTop(4).Text("BadmintonHub · 12 Jalan Ampang, 50450 Kuala Lumpur · 03-4142 8899 · info@badmintonhub.my")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf(stream);

        return stream.ToArray();
    }
}
