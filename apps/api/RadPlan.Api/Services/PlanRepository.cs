using System.Globalization;
using Npgsql;
using RadPlan.Api.Models;

namespace RadPlan.Api.Services;

public sealed class PlanRepository(NpgsqlDataSource dataSource, FieldEncryptionService encryption)
{
    public async Task<IReadOnlyList<ScannerResponse>> GetScannersAsync()
    {
        var result = new List<ScannerResponse>();
        await using var command = dataSource.CreateCommand("SELECT id, name, model FROM scanners WHERE is_active = true ORDER BY name");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new ScannerResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public async Task<ScannerResponse> AddScannerAsync(CreateScannerRequest request)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO scanners (name, model) VALUES (@name, @model) RETURNING id, name, model");
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("model", request.Model.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new ScannerResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task<bool> UpdateScannerAsync(Guid id, UpdateScannerRequest request)
    {
        await using var command = dataSource.CreateCommand("UPDATE scanners SET name = @name, model = @model, is_active = @active WHERE id = @id");
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("model", request.Model.Trim()); command.Parameters.AddWithValue("active", request.IsActive);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<IReadOnlyList<IsotopeSettingsResponse>> GetSettingsAsync()
    {
        var result = new List<IsotopeSettingsResponse>();
        await using var command = dataSource.CreateCommand("SELECT isotope_code, half_life_minutes, dose_coefficient_mbq_per_kg, default_source_activity_mbq FROM isotope_settings ORDER BY isotope_code");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        return result;
    }

    public async Task<bool> UpdateSettingsAsync(string isotope, UpdateIsotopeSettingsRequest request)
    {
        if (request.HalfLifeMinutes <= 0 || request.DoseCoefficientMbqPerKg <= 0 || request.DefaultSourceActivityMbq < 0) throw new ArgumentException("Settings have invalid values.");
        await using var command = dataSource.CreateCommand("UPDATE isotope_settings SET half_life_minutes = @halfLife, dose_coefficient_mbq_per_kg = @coefficient, default_source_activity_mbq = @activity, updated_at = now() WHERE isotope_code = @isotope");
        command.Parameters.AddWithValue("isotope", isotope); command.Parameters.AddWithValue("halfLife", request.HalfLifeMinutes); command.Parameters.AddWithValue("coefficient", request.DoseCoefficientMbqPerKg); command.Parameters.AddWithValue("activity", request.DefaultSourceActivityMbq);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<IReadOnlyList<ProtocolResponse>> GetProtocolsAsync()
    {
        var result = new List<ProtocolResponse>();
        await using var command = dataSource.CreateCommand("SELECT id, isotope_code, name, duration_minutes, is_active FROM protocols ORDER BY isotope_code, name");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt16(3), reader.GetBoolean(4)));
        return result;
    }

    public async Task<ProtocolResponse> AddProtocolAsync(UpsertProtocolRequest request)
    {
        if (request.IsotopeCode is not ("F-18" or "Ga-68") || string.IsNullOrWhiteSpace(request.Name) || request.DurationMinutes is < 1 or > 300) throw new ArgumentException("Protocol has invalid values.");
        await using var command = dataSource.CreateCommand("INSERT INTO protocols (isotope_code, name, duration_minutes, is_active) VALUES (@isotope, @name, @duration, @active) RETURNING id, isotope_code, name, duration_minutes, is_active");
        command.Parameters.AddWithValue("isotope", request.IsotopeCode); command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("duration", request.DurationMinutes); command.Parameters.AddWithValue("active", request.IsActive);
        await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync(); return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt16(3), reader.GetBoolean(4));
    }

    public async Task<bool> UpdateProtocolAsync(Guid id, UpsertProtocolRequest request)
    {
        if (request.IsotopeCode is not ("F-18" or "Ga-68") || string.IsNullOrWhiteSpace(request.Name) || request.DurationMinutes is < 1 or > 300) throw new ArgumentException("Protocol has invalid values.");
        await using var command = dataSource.CreateCommand("UPDATE protocols SET isotope_code = @isotope, name = @name, duration_minutes = @duration, is_active = @active WHERE id = @id");
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("isotope", request.IsotopeCode); command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("duration", request.DurationMinutes); command.Parameters.AddWithValue("active", request.IsActive);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<IReadOnlyList<ShiftSummaryResponse>> GetShiftHistoryAsync(DateOnly from, DateOnly to)
    {
        var result = new List<ShiftSummaryResponse>();
        await using var command = dataSource.CreateCommand("SELECT s.id, s.shift_date, s.isotope_code, s.source_activity_mbq, count(a.id), count(a.confirmed_at) FROM shifts s LEFT JOIN appointments a ON a.shift_id = s.id WHERE s.shift_date BETWEEN @from AND @to GROUP BY s.id ORDER BY s.shift_date DESC, s.isotope_code");
        command.Parameters.AddWithValue("from", from); command.Parameters.AddWithValue("to", to);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.GetDecimal(3), checked((int)reader.GetInt64(4)), checked((int)reader.GetInt64(5))));
        return result;
    }

    public async Task<ShiftResponse?> GetShiftAsync(DateOnly date, string isotopeCode)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var shiftCommand = new NpgsqlCommand("SELECT id, shift_date, isotope_code, source_activity_mbq, source_measured_at FROM shifts WHERE shift_date = @date AND isotope_code = @isotopeCode", connection);
        shiftCommand.Parameters.AddWithValue("date", date);
        shiftCommand.Parameters.AddWithValue("isotopeCode", isotopeCode);
        await using var shiftReader = await shiftCommand.ExecuteReaderAsync();
        if (!await shiftReader.ReadAsync()) return null;
        var id = shiftReader.GetGuid(0);
        var shiftDate = shiftReader.GetFieldValue<DateOnly>(1);
        var code = shiftReader.GetString(2);
        var sourceActivity = shiftReader.GetDecimal(3);
        var measuredAt = shiftReader.GetFieldValue<DateTimeOffset>(4);
        await shiftReader.CloseAsync();
        return new ShiftResponse(id, shiftDate, code, sourceActivity, measuredAt, await GetAppointmentsAsync(connection, id));
    }

    public async Task<ShiftResponse?> GetShiftByIdAsync(Guid shiftId)
    {
        await using var command = dataSource.CreateCommand("SELECT shift_date, isotope_code FROM shifts WHERE id = @shiftId");
        command.Parameters.AddWithValue("shiftId", shiftId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return await GetShiftAsync(reader.GetFieldValue<DateOnly>(0), reader.GetString(1));
    }

    public async Task<ShiftResponse> CreateShiftAsync(Guid actorId, CreateShiftRequest request)
    {
        ValidateShift(request);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var shiftCommand = new NpgsqlCommand("INSERT INTO shifts (shift_date, isotope_code, source_activity_mbq, source_measured_at, created_by) VALUES (@date, @isotopeCode, @activity, @measuredAt, @actorId) RETURNING id", connection, transaction);
            shiftCommand.Parameters.AddWithValue("date", request.ShiftDate);
            shiftCommand.Parameters.AddWithValue("isotopeCode", request.IsotopeCode);
            shiftCommand.Parameters.AddWithValue("activity", request.SourceActivityMbq);
            shiftCommand.Parameters.AddWithValue("measuredAt", request.SourceMeasuredAt);
            shiftCommand.Parameters.AddWithValue("actorId", actorId);
            var shiftId = (Guid)(await shiftCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create shift."));
            foreach (var appointment in request.Appointments)
            {
                await EnsureNoScannerConflictAsync(connection, request.ShiftDate, appointment.ScannerId, appointment.InjectionAt, appointment.DurationMinutes);
                await using var appointmentCommand = new NpgsqlCommand("INSERT INTO appointments (shift_id, scanner_id, patient_number_ciphertext, weight_ciphertext, isotope_code, protocol_name, injection_at, duration_minutes) VALUES (@shiftId, @scannerId, @number, @weight, @isotopeCode, @protocol, @injectionAt, @duration)", connection, transaction);
                appointmentCommand.Parameters.AddWithValue("shiftId", shiftId);
                appointmentCommand.Parameters.AddWithValue("scannerId", appointment.ScannerId);
                appointmentCommand.Parameters.AddWithValue("number", encryption.Encrypt(appointment.PatientNumber.Trim()));
                appointmentCommand.Parameters.AddWithValue("weight", encryption.Encrypt(appointment.WeightKg.ToString(CultureInfo.InvariantCulture)));
                appointmentCommand.Parameters.AddWithValue("isotopeCode", request.IsotopeCode);
                appointmentCommand.Parameters.AddWithValue("protocol", appointment.ProtocolName.Trim());
                appointmentCommand.Parameters.AddWithValue("injectionAt", appointment.InjectionAt);
                appointmentCommand.Parameters.AddWithValue("duration", appointment.DurationMinutes);
                await appointmentCommand.ExecuteNonQueryAsync();
            }
            await using var auditCommand = new NpgsqlCommand("INSERT INTO audit_events (actor_id, action, entity_type, entity_id) VALUES (@actorId, 'created', 'shift', @shiftId)", connection, transaction);
            auditCommand.Parameters.AddWithValue("actorId", actorId);
            auditCommand.Parameters.AddWithValue("shiftId", shiftId);
            await auditCommand.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return (await GetShiftAsync(request.ShiftDate, request.IsotopeCode)) ?? throw new InvalidOperationException("Created shift could not be loaded.");
        }
        catch
        {
            try { await transaction.RollbackAsync(); }
            catch (InvalidOperationException) { }
            throw;
        }
    }

    public async Task<bool> ConfirmAppointmentAsync(Guid actorId, Guid appointmentId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var updateCommand = new NpgsqlCommand("UPDATE appointments SET confirmed_at = now(), confirmed_by = @actorId WHERE id = @appointmentId AND confirmed_at IS NULL", connection, transaction);
        updateCommand.Parameters.AddWithValue("actorId", actorId);
        updateCommand.Parameters.AddWithValue("appointmentId", appointmentId);
        if (await updateCommand.ExecuteNonQueryAsync() != 1) return false;
        await using var auditCommand = new NpgsqlCommand("INSERT INTO audit_events (actor_id, action, entity_type, entity_id) VALUES (@actorId, 'confirmed', 'appointment', @appointmentId)", connection, transaction);
        auditCommand.Parameters.AddWithValue("actorId", actorId);
        auditCommand.Parameters.AddWithValue("appointmentId", appointmentId);
        await auditCommand.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<AppointmentResponse?> AddAppointmentAsync(Guid actorId, Guid shiftId, CreateAppointmentRequest request)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var shiftCommand = new NpgsqlCommand("SELECT shift_date, isotope_code FROM shifts WHERE id = @shiftId", connection, transaction);
        shiftCommand.Parameters.AddWithValue("shiftId", shiftId);
        await using var shiftReader = await shiftCommand.ExecuteReaderAsync();
        if (!await shiftReader.ReadAsync()) return null;
        var shiftDate = shiftReader.GetFieldValue<DateOnly>(0);
        var isotopeCode = shiftReader.GetString(1);
        await shiftReader.CloseAsync();
        ValidateAppointment(shiftDate, isotopeCode, request);
        await EnsureNoScannerConflictAsync(connection, shiftDate, request.ScannerId, request.InjectionAt, request.DurationMinutes);
        await using var appointmentCommand = new NpgsqlCommand("INSERT INTO appointments (shift_id, scanner_id, patient_number_ciphertext, weight_ciphertext, isotope_code, protocol_name, injection_at, duration_minutes) VALUES (@shiftId, @scannerId, @number, @weight, @isotopeCode, @protocol, @injectionAt, @duration) RETURNING id", connection, transaction);
        appointmentCommand.Parameters.AddWithValue("shiftId", shiftId);
        appointmentCommand.Parameters.AddWithValue("scannerId", request.ScannerId);
        appointmentCommand.Parameters.AddWithValue("number", encryption.Encrypt(request.PatientNumber.Trim()));
        appointmentCommand.Parameters.AddWithValue("weight", encryption.Encrypt(request.WeightKg.ToString(CultureInfo.InvariantCulture)));
        appointmentCommand.Parameters.AddWithValue("isotopeCode", isotopeCode);
        appointmentCommand.Parameters.AddWithValue("protocol", request.ProtocolName.Trim());
        appointmentCommand.Parameters.AddWithValue("injectionAt", request.InjectionAt);
        appointmentCommand.Parameters.AddWithValue("duration", request.DurationMinutes);
        var appointmentId = (Guid)(await appointmentCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to add appointment."));
        await using var auditCommand = new NpgsqlCommand("INSERT INTO audit_events (actor_id, action, entity_type, entity_id) VALUES (@actorId, 'created', 'appointment', @appointmentId)", connection, transaction);
        auditCommand.Parameters.AddWithValue("actorId", actorId);
        auditCommand.Parameters.AddWithValue("appointmentId", appointmentId);
        await auditCommand.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return (await GetShiftByIdAsync(shiftId))?.Appointments.Single(item => item.Id == appointmentId);
    }

    public async Task<bool> UpdateSourceActivityAsync(Guid actorId, Guid shiftId, UpdateSourceActivityRequest request)
    {
        if (request.SourceActivityMbq < 0) throw new ArgumentException("Source activity cannot be negative.");
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var updateCommand = new NpgsqlCommand("UPDATE shifts SET source_activity_mbq = @activity, source_measured_at = @measuredAt WHERE id = @shiftId", connection, transaction);
        updateCommand.Parameters.AddWithValue("activity", request.SourceActivityMbq);
        updateCommand.Parameters.AddWithValue("measuredAt", request.SourceMeasuredAt);
        updateCommand.Parameters.AddWithValue("shiftId", shiftId);
        if (await updateCommand.ExecuteNonQueryAsync() != 1) return false;
        await using var auditCommand = new NpgsqlCommand("INSERT INTO audit_events (actor_id, action, entity_type, entity_id) VALUES (@actorId, 'updated_activity', 'shift', @shiftId)", connection, transaction);
        auditCommand.Parameters.AddWithValue("actorId", actorId);
        auditCommand.Parameters.AddWithValue("shiftId", shiftId);
        await auditCommand.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<AppointmentResponse?> UpdateAppointmentAsync(Guid actorId, Guid appointmentId, UpdateAppointmentRequest request)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var lookup = new NpgsqlCommand("SELECT s.id, s.shift_date, s.isotope_code FROM appointments a JOIN shifts s ON s.id = a.shift_id WHERE a.id = @id", connection);
        lookup.Parameters.AddWithValue("id", appointmentId);
        await using var reader = await lookup.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var shiftId = reader.GetGuid(0); var date = reader.GetFieldValue<DateOnly>(1); var isotope = reader.GetString(2); await reader.CloseAsync();
        ValidateAppointment(date, isotope, new CreateAppointmentRequest(request.ScannerId, request.PatientNumber, request.WeightKg, request.ProtocolName, request.InjectionAt, request.DurationMinutes));
        await EnsureNoScannerConflictAsync(connection, date, request.ScannerId, request.InjectionAt, request.DurationMinutes, appointmentId);
        await using var update = new NpgsqlCommand("UPDATE appointments SET scanner_id=@scanner, patient_number_ciphertext=@number, weight_ciphertext=@weight, protocol_name=@protocol, injection_at=@at, duration_minutes=@duration, confirmed_at=NULL, confirmed_by=NULL WHERE id=@id", connection);
        update.Parameters.AddWithValue("id", appointmentId); update.Parameters.AddWithValue("scanner", request.ScannerId); update.Parameters.AddWithValue("number", encryption.Encrypt(request.PatientNumber.Trim())); update.Parameters.AddWithValue("weight", encryption.Encrypt(request.WeightKg.ToString(CultureInfo.InvariantCulture))); update.Parameters.AddWithValue("protocol", request.ProtocolName.Trim()); update.Parameters.AddWithValue("at", request.InjectionAt); update.Parameters.AddWithValue("duration", request.DurationMinutes);
        await update.ExecuteNonQueryAsync();
        await AuditAsync(connection, actorId, "updated", "appointment", appointmentId);
        return (await GetShiftByIdAsync(shiftId))?.Appointments.Single(item => item.Id == appointmentId);
    }

    public async Task<bool> DeleteAppointmentAsync(Guid actorId, Guid appointmentId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("DELETE FROM appointments WHERE id = @id", connection); command.Parameters.AddWithValue("id", appointmentId);
        if (await command.ExecuteNonQueryAsync() != 1) return false;
        await AuditAsync(connection, actorId, "deleted", "appointment", appointmentId); return true;
    }

    private async Task EnsureNoScannerConflictAsync(NpgsqlConnection connection, DateOnly date, Guid scannerId, DateTimeOffset injectionAt, short duration, Guid? excludedId = null)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM appointments a JOIN shifts s ON s.id=a.shift_id WHERE s.shift_date=@date AND a.scanner_id=@scanner AND (@id IS NULL OR a.id<>@id) AND a.injection_at < @end AND a.injection_at + a.duration_minutes * interval '1 minute' > @start)", connection);
        command.Parameters.AddWithValue("date", date); command.Parameters.AddWithValue("scanner", scannerId); command.Parameters.AddWithValue("id", (object?)excludedId ?? DBNull.Value); command.Parameters.AddWithValue("start", injectionAt); command.Parameters.AddWithValue("end", injectionAt.AddMinutes(duration));
        if ((bool)(await command.ExecuteScalarAsync() ?? false)) throw new ArgumentException("Scanner has an overlapping appointment.");
    }

    private static async Task AuditAsync(NpgsqlConnection connection, Guid actorId, string action, string type, Guid entityId)
    {
        await using var command = new NpgsqlCommand("INSERT INTO audit_events (actor_id, action, entity_type, entity_id) VALUES (@actor, @action, @type, @id)", connection);
        command.Parameters.AddWithValue("actor", actorId); command.Parameters.AddWithValue("action", action); command.Parameters.AddWithValue("type", type); command.Parameters.AddWithValue("id", entityId); await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<AppointmentResponse>> GetAppointmentsAsync(NpgsqlConnection connection, Guid shiftId)
    {
        var result = new List<AppointmentResponse>();
        await using var command = new NpgsqlCommand("SELECT a.id, a.scanner_id, s.name, a.patient_number_ciphertext, a.weight_ciphertext, a.isotope_code, a.protocol_name, a.injection_at, a.duration_minutes, a.confirmed_at FROM appointments a JOIN scanners s ON s.id = a.scanner_id WHERE a.shift_id = @shiftId ORDER BY a.injection_at", connection);
        command.Parameters.AddWithValue("shiftId", shiftId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new AppointmentResponse(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), encryption.Decrypt(reader.GetString(3)), decimal.Parse(encryption.Decrypt(reader.GetString(4)), CultureInfo.InvariantCulture), reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7), reader.GetInt16(8), !reader.IsDBNull(9)));
        return result;
    }

    private static void ValidateShift(CreateShiftRequest request)
    {
        if (request.IsotopeCode is not ("F-18" or "Ga-68")) throw new ArgumentException("Unsupported isotope.");
        if (request.SourceActivityMbq < 0 || request.Appointments.Count == 0) throw new ArgumentException("Shift has invalid values.");
        foreach (var appointment in request.Appointments) ValidateAppointment(request.ShiftDate, request.IsotopeCode, appointment);
    }

    private static void ValidateAppointment(DateOnly shiftDate, string isotopeCode, CreateAppointmentRequest appointment)
    {
        if (isotopeCode is not ("F-18" or "Ga-68") || string.IsNullOrWhiteSpace(appointment.PatientNumber) || string.IsNullOrWhiteSpace(appointment.ProtocolName) || appointment.WeightKg <= 0 || appointment.DurationMinutes is < 1 or > 300 || appointment.InjectionAt.Date != shiftDate.ToDateTime(TimeOnly.MinValue).Date)
            throw new ArgumentException("Appointment has invalid values.");
    }
}
