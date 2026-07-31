namespace RadPlan.Api.Models;

public sealed record ScannerResponse(Guid Id, string Name, string Model);
public sealed record CreateScannerRequest(string Name, string Model);
public sealed record UpdateScannerRequest(string Name, string Model, bool IsActive);
public sealed record IsotopeSettingsResponse(string IsotopeCode, decimal HalfLifeMinutes, decimal DoseCoefficientMbqPerKg, decimal DefaultSourceActivityMbq);
public sealed record UpdateIsotopeSettingsRequest(decimal HalfLifeMinutes, decimal DoseCoefficientMbqPerKg, decimal DefaultSourceActivityMbq);
public sealed record ProtocolResponse(Guid Id, string IsotopeCode, string Name, short DurationMinutes, bool IsActive);
public sealed record UpsertProtocolRequest(string IsotopeCode, string Name, short DurationMinutes, bool IsActive);
public sealed record ShiftSummaryResponse(Guid Id, DateOnly ShiftDate, string IsotopeCode, decimal SourceActivityMbq, int AppointmentCount, int ConfirmedCount);
public sealed record CreateShiftRequest(DateOnly ShiftDate, string IsotopeCode, decimal SourceActivityMbq, DateTimeOffset SourceMeasuredAt, IReadOnlyList<CreateAppointmentRequest> Appointments);
public sealed record CreateAppointmentRequest(Guid ScannerId, string PatientNumber, decimal WeightKg, string ProtocolName, DateTimeOffset InjectionAt, short DurationMinutes);
public sealed record UpdateSourceActivityRequest(decimal SourceActivityMbq, DateTimeOffset SourceMeasuredAt);
public sealed record UpdateAppointmentRequest(Guid ScannerId, string PatientNumber, decimal WeightKg, string ProtocolName, DateTimeOffset InjectionAt, short DurationMinutes);
public sealed record AppointmentResponse(Guid Id, Guid ScannerId, string ScannerName, string PatientNumber, decimal WeightKg, string IsotopeCode, string ProtocolName, DateTimeOffset InjectionAt, short DurationMinutes, bool Confirmed);
public sealed record ShiftResponse(Guid Id, DateOnly ShiftDate, string IsotopeCode, decimal SourceActivityMbq, DateTimeOffset SourceMeasuredAt, IReadOnlyList<AppointmentResponse> Appointments);
