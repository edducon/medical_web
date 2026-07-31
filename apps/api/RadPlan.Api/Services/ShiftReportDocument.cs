using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RadPlan.Api.Models;

namespace RadPlan.Api.Services;

public sealed class ShiftReportDocument(ShiftResponse shift) : IDocument
{
    public DocumentMetadata GetMetadata() => new() { Title = $"Отчёт смены {shift.ShiftDate:yyyy-MM-dd} {shift.IsotopeCode}" };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(32);
            page.DefaultTextStyle(style => style.FontFamily("DejaVu Sans").FontSize(9));
            page.Header().Column(column =>
            {
                column.Item().Text("Радиоплан").FontSize(18).SemiBold().FontColor(Colors.Blue.Darken2);
                column.Item().Text($"Отчёт по смене · {shift.ShiftDate:dd.MM.yyyy} · {shift.IsotopeCode}").FontSize(11).FontColor(Colors.Grey.Darken1);
            });
            page.Content().PaddingVertical(20).Column(column =>
            {
                column.Spacing(12);
                column.Item().Background(Colors.Grey.Lighten4).Padding(12).Row(row =>
                {
                    row.RelativeItem().Column(item => { item.Item().Text("Активность при поставке").FontColor(Colors.Grey.Darken1); item.Item().Text($"{shift.SourceActivityMbq:N0} МБк").FontSize(16).SemiBold(); });
                    row.RelativeItem().Column(item => { item.Item().Text("Время измерения").FontColor(Colors.Grey.Darken1); item.Item().Text($"{shift.SourceMeasuredAt:dd.MM.yyyy HH:mm zzz}").FontSize(12).SemiBold(); });
                    row.RelativeItem().Column(item => { item.Item().Text("Записей").FontColor(Colors.Grey.Darken1); item.Item().Text(shift.Appointments.Count.ToString()).FontSize(16).SemiBold(); });
                });
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(1.15f); columns.RelativeColumn(.55f); columns.RelativeColumn(1.35f); columns.RelativeColumn(1.4f); columns.RelativeColumn(.7f); columns.RelativeColumn(1.15f); });
                    table.Header(header =>
                    {
                        foreach (var heading in new[] { "Пациент", "Вес", "Аппарат", "Протокол", "Инъекция", "Статус" })
                            header.Cell().Background(Colors.Blue.Darken2).Padding(6).Text(heading).FontColor(Colors.White).SemiBold();
                    });
                    foreach (var appointment in shift.Appointments.OrderBy(item => item.InjectionAt))
                    {
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(appointment.PatientNumber);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text($"{appointment.WeightKg:N1} кг");
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(appointment.ScannerName);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(appointment.ProtocolName);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(appointment.InjectionAt.ToString("HH:mm"));
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(appointment.Confirmed ? "Подтверждено" : "Ожидает врача");
                    }
                });
            });
            page.Footer().AlignCenter().Text(text => { text.Span("Сформировано Радиоплан · "); text.CurrentPageNumber(); text.Span(" / "); text.TotalPages(); });
        });
    }
}
