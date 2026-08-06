namespace RadPlan.Api.Models;

public sealed record ScannerResponse(Guid Id, string Name, string Model, string? SerialNumber, short? ManufactureYear);
public sealed record CreateScannerRequest(string Name, string Model, string? SerialNumber, short? ManufactureYear);
public sealed record UpdateScannerRequest(string Name, string Model, string? SerialNumber, short? ManufactureYear, bool IsActive);
public sealed record ScannerProfileResponse(Guid ScannerId, string PatientCategory, short PreparationMinutes, short ScanMinutes);
public sealed record UpdateScannerProfileRequest(short PreparationMinutes, short ScanMinutes);
public sealed record IsotopeSettingsResponse(string IsotopeCode, decimal HalfLifeMinutes, decimal DoseCoefficientMbqPerKg, decimal DefaultSourceActivityMbq);
public sealed record UpdateIsotopeSettingsRequest(decimal HalfLifeMinutes, decimal DoseCoefficientMbqPerKg, decimal DefaultSourceActivityMbq);
public sealed record ProtocolResponse(Guid Id, string IsotopeCode, string Name, short DurationMinutes, short UptakeMinutes, short? MaximumUptakeMinutes, bool IsActive);
public sealed record UpsertProtocolRequest(string IsotopeCode, string Name, short DurationMinutes, short UptakeMinutes, short? MaximumUptakeMinutes, bool IsActive);
public sealed record ShiftSummaryResponse(Guid Id, DateOnly ShiftDate, string IsotopeCode, decimal SourceActivityMbq, int AppointmentCount, int ConfirmedCount, bool IsClosed);
public sealed record PatientSearchResponse(Guid Id, string PatientNumber, decimal LastWeightKg);
public sealed record CreateShiftRequest(DateOnly ShiftDate, string IsotopeCode, decimal SourceActivityMbq, DateTimeOffset SourceMeasuredAt, IReadOnlyList<CreateAppointmentRequest> Appointments);
public sealed record CreateAppointmentRequest(Guid ScannerId, string PatientNumber, decimal WeightKg, string ProtocolName, DateTimeOffset ScanStartAt, string PatientCategory);
public sealed record UpdateSourceActivityRequest(decimal SourceActivityMbq, DateTimeOffset SourceMeasuredAt, decimal HalfLifeMinutes, decimal DoseCoefficientMbqPerKg);
public sealed record UpdateAppointmentRequest(Guid ScannerId, string PatientNumber, decimal WeightKg, string ProtocolName, DateTimeOffset ScanStartAt, string PatientCategory);
public sealed record AppointmentResponse(Guid Id, Guid ScannerId, string ScannerName, string PatientNumber, decimal WeightKg, string IsotopeCode, string ProtocolName, DateTimeOffset InjectionAt, DateTimeOffset ScanStartAt, short DurationMinutes, short UptakeMinutes, string PatientCategory, bool Confirmed);
public sealed record ShiftResponse(Guid Id, DateOnly ShiftDate, string IsotopeCode, decimal SourceActivityMbq, DateTimeOffset SourceMeasuredAt, decimal HalfLifeMinutes, decimal DoseCoefficientMbqPerKg, bool IsClosed, IReadOnlyList<AppointmentResponse> Appointments);
