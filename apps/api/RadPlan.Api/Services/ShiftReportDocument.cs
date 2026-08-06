using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RadPlan.Api.Models;

namespace RadPlan.Api.Services;

public sealed class ShiftReportDocument(ShiftResponse shift) : IDocument
{
    public DocumentMetadata GetMetadata() => new() { Title = $"Отчёт смены {shift.ShiftDate:dd.MM.yyyy} {shift.IsotopeCode}" };
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape()); page.Margin(28); page.DefaultTextStyle(style => style.FontFamily("DejaVu Sans").FontSize(9));
            page.Header().Column(column => { column.Item().Text("Радиоплан").FontSize(18).SemiBold().FontColor(Colors.Blue.Darken2); column.Item().Text($"Отчёт по смене · {shift.ShiftDate:dd.MM.yyyy} · {shift.IsotopeCode}").FontSize(11).FontColor(Colors.Grey.Darken1); });
            page.Content().PaddingVertical(20).Column(column =>
            {
                column.Spacing(12);
                column.Item().Background(Colors.Grey.Lighten4).Padding(12).Row(row =>
                {
                    row.RelativeItem().Column(item => { item.Item().Text("Активность при поставке").FontColor(Colors.Grey.Darken1); item.Item().Text($"{shift.SourceActivityMbq:N0} МБк").FontSize(16).SemiBold(); });
                    row.RelativeItem().Column(item => { item.Item().Text("Время замера").FontColor(Colors.Grey.Darken1); item.Item().Text($"{shift.SourceMeasuredAt:dd.MM.yyyy HH:mm}").FontSize(12).SemiBold(); });
                    row.RelativeItem().Column(item => { item.Item().Text("Параметры расчёта").FontColor(Colors.Grey.Darken1); item.Item().Text($"T½ {shift.HalfLifeMinutes} мин · {shift.DoseCoefficientMbqPerKg} МБк/кг").FontSize(10).SemiBold(); });
                });
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(1); columns.RelativeColumn(.6f); columns.RelativeColumn(1.15f); columns.RelativeColumn(1.45f); columns.RelativeColumn(.75f); columns.RelativeColumn(.7f); columns.RelativeColumn(.85f); columns.RelativeColumn(.4f); });
                    table.Header(header => { foreach (var heading in new[] { "Пациент", "Вес", "Аппарат", "Протокол", "Инъекция", "Скан", "Доза", "✓" }) header.Cell().Background(Colors.Blue.Darken2).Padding(5).Text(heading).FontColor(Colors.White).SemiBold(); });
                    foreach (var appointment in shift.Appointments.OrderBy(item => item.InjectionAt))
                    {
                        var dose = appointment.WeightKg * shift.DoseCoefficientMbqPerKg;
                        foreach (var value in new[] { appointment.PatientNumber, $"{appointment.WeightKg:N0} кг", appointment.ScannerName, appointment.ProtocolName, appointment.InjectionAt.ToString("HH:mm"), appointment.ScanStartAt.ToString("HH:mm"), $"{dose:N0} МБк" }) table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(value);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(appointment.Confirmed ? "✓" : "○").FontSize(13).SemiBold().FontColor(appointment.Confirmed ? Colors.Green.Darken2 : Colors.Grey.Darken1);
                    }
                });
            });
            page.Footer().AlignCenter().Text(text => { text.Span("Сформировано Радиоплан · "); text.CurrentPageNumber(); text.Span(" / "); text.TotalPages(); });
        });
    }
}
