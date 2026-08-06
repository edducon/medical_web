using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using RadPlan.Api.Models;

namespace RadPlan.Api.Services;

public sealed class PlanRepository(NpgsqlDataSource dataSource, FieldEncryptionService encryption)
{
    public async Task<IReadOnlyList<ScannerResponse>> GetScannersAsync()
    {
        var result = new List<ScannerResponse>();
        await using var command = dataSource.CreateCommand("SELECT id, name, model, serial_number, manufacture_year FROM scanners WHERE is_active = true ORDER BY name");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt16(4)));
        return result;
    }

    public async Task<IReadOnlyList<PatientSearchResponse>> SearchPatientsAsync(string patientNumber)
    {
        var query = patientNumber.Trim();
        if (query.Length < 2) return [];
        await using var command = dataSource.CreateCommand("SELECT DISTINCT ON (patient_number_search_tokens) id,patient_number_ciphertext,weight_ciphertext FROM appointments WHERE patient_number_search_tokens @> ARRAY[@token]::TEXT[] ORDER BY patient_number_search_tokens,scan_start_at DESC LIMIT 8");
        command.Parameters.AddWithValue("token", encryption.PatientNumberSearchToken(query));
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<PatientSearchResponse>();
        while (await reader.ReadAsync())
            result.Add(new(reader.GetGuid(0), encryption.Decrypt(reader.GetString(1)), decimal.Parse(encryption.Decrypt(reader.GetString(2)), CultureInfo.InvariantCulture)));
        return result;
    }

    public async Task BackfillPatientSearchTokensAsync()
    {
        var rows = new List<(Guid Id, string Number)>();
        await using (var read = dataSource.CreateCommand("SELECT id,patient_number_ciphertext FROM appointments WHERE cardinality(patient_number_search_tokens) = 0"))
        await using (var reader = await read.ExecuteReaderAsync())
            while (await reader.ReadAsync()) rows.Add((reader.GetGuid(0), encryption.Decrypt(reader.GetString(1))));

        if (rows.Count == 0) return;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var row in rows)
        {
            await using var update = new NpgsqlCommand("UPDATE appointments SET patient_number_search_tokens=@tokens WHERE id=@id", connection, transaction);
            update.Parameters.AddWithValue("id", row.Id); update.Parameters.AddWithValue("tokens", NpgsqlDbType.Array | NpgsqlDbType.Text, encryption.PatientNumberSearchTokens(row.Number));
            await update.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public async Task<ScannerResponse> AddScannerAsync(CreateScannerRequest request)
    {
        ValidateScanner(request.Name, request.Model, request.ManufactureYear);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("INSERT INTO scanners (name, model, serial_number, manufacture_year) VALUES (@name, @model, @serial, @year) RETURNING id, name, model, serial_number, manufacture_year", connection, transaction);
        command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("model", request.Model.Trim()); command.Parameters.AddWithValue("serial", (object?)request.SerialNumber?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("year", (object?)request.ManufactureYear ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
        var scanner = new ScannerResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt16(4));
        await reader.CloseAsync();
        foreach (var category in new[] { "S", "M", "F" })
        {
            await using var profile = new NpgsqlCommand("INSERT INTO scanner_profiles (scanner_id, patient_category, preparation_minutes, scan_minutes) VALUES (@id, @category, 20, 20)", connection, transaction);
            profile.Parameters.AddWithValue("id", scanner.Id); profile.Parameters.AddWithValue("category", category); await profile.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return scanner;
    }

    public async Task<bool> UpdateScannerAsync(Guid id, UpdateScannerRequest request)
    {
        ValidateScanner(request.Name, request.Model, request.ManufactureYear);
        await using var command = dataSource.CreateCommand("UPDATE scanners SET name=@name, model=@model, serial_number=@serial, manufacture_year=@year, is_active=@active WHERE id=@id");
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("model", request.Model.Trim()); command.Parameters.AddWithValue("serial", (object?)request.SerialNumber?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("year", (object?)request.ManufactureYear ?? DBNull.Value); command.Parameters.AddWithValue("active", request.IsActive);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<IReadOnlyList<ScannerProfileResponse>> GetScannerProfilesAsync()
    {
        var result = new List<ScannerProfileResponse>();
        await using var command = dataSource.CreateCommand("SELECT scanner_id, patient_category, preparation_minutes, scan_minutes FROM scanner_profiles ORDER BY scanner_id, patient_category");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt16(2), reader.GetInt16(3)));
        return result;
    }

    public async Task<bool> UpdateScannerProfileAsync(Guid scannerId, string category, UpdateScannerProfileRequest request)
    {
        if (category is not ("S" or "M" or "F") || request.PreparationMinutes is < 0 or > 180 || request.ScanMinutes is < 1 or > 300) throw new ArgumentException("Параметры профиля аппарата некорректны.");
        await using var command = dataSource.CreateCommand("UPDATE scanner_profiles SET preparation_minutes=@preparation, scan_minutes=@scan WHERE scanner_id=@scanner AND patient_category=@category");
        command.Parameters.AddWithValue("scanner", scannerId); command.Parameters.AddWithValue("category", category); command.Parameters.AddWithValue("preparation", request.PreparationMinutes); command.Parameters.AddWithValue("scan", request.ScanMinutes);
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
        if (request.HalfLifeMinutes <= 0 || request.DoseCoefficientMbqPerKg <= 0 || request.DefaultSourceActivityMbq < 0) throw new ArgumentException("Параметры расчёта некорректны.");
        await using var command = dataSource.CreateCommand("UPDATE isotope_settings SET half_life_minutes=@halfLife, dose_coefficient_mbq_per_kg=@coefficient, default_source_activity_mbq=@activity, updated_at=now() WHERE isotope_code=@isotope");
        command.Parameters.AddWithValue("isotope", isotope); command.Parameters.AddWithValue("halfLife", request.HalfLifeMinutes); command.Parameters.AddWithValue("coefficient", request.DoseCoefficientMbqPerKg); command.Parameters.AddWithValue("activity", request.DefaultSourceActivityMbq);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<IReadOnlyList<ProtocolResponse>> GetProtocolsAsync()
    {
        var result = new List<ProtocolResponse>();
        await using var command = dataSource.CreateCommand("SELECT id, isotope_code, name, duration_minutes, uptake_minutes, maximum_uptake_minutes, is_active FROM protocols ORDER BY isotope_code, name");
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt16(3), reader.GetInt16(4), reader.IsDBNull(5) ? null : reader.GetInt16(5), reader.GetBoolean(6)));
        return result;
    }

    public async Task<ProtocolResponse> AddProtocolAsync(UpsertProtocolRequest request)
    {
        ValidateProtocol(request);
        await using var command = dataSource.CreateCommand("INSERT INTO protocols (isotope_code, name, duration_minutes, uptake_minutes, maximum_uptake_minutes, is_active) VALUES (@isotope,@name,@duration,@uptake,@maximum,@active) RETURNING id,isotope_code,name,duration_minutes,uptake_minutes,maximum_uptake_minutes,is_active");
        command.Parameters.AddWithValue("isotope", request.IsotopeCode); command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("duration", request.DurationMinutes); command.Parameters.AddWithValue("uptake", request.UptakeMinutes); command.Parameters.AddWithValue("maximum", (object?)request.MaximumUptakeMinutes ?? DBNull.Value); command.Parameters.AddWithValue("active", request.IsActive);
        await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync(); return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt16(3), reader.GetInt16(4), reader.IsDBNull(5) ? null : reader.GetInt16(5), reader.GetBoolean(6));
    }

    public async Task<bool> UpdateProtocolAsync(Guid id, UpsertProtocolRequest request)
    {
        ValidateProtocol(request);
        await using var command = dataSource.CreateCommand("UPDATE protocols SET isotope_code=@isotope,name=@name,duration_minutes=@duration,uptake_minutes=@uptake,maximum_uptake_minutes=@maximum,is_active=@active WHERE id=@id");
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("isotope", request.IsotopeCode); command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("duration", request.DurationMinutes); command.Parameters.AddWithValue("uptake", request.UptakeMinutes); command.Parameters.AddWithValue("maximum", (object?)request.MaximumUptakeMinutes ?? DBNull.Value); command.Parameters.AddWithValue("active", request.IsActive);
        return await command.ExecuteNonQueryAsync() == 1;
    }

    public async Task<IReadOnlyList<ShiftSummaryResponse>> GetShiftHistoryAsync(DateOnly from, DateOnly to)
    {
        var result = new List<ShiftSummaryResponse>();
        await using var command = dataSource.CreateCommand("SELECT s.id,s.shift_date,s.isotope_code,s.source_activity_mbq,count(a.id),count(a.confirmed_at),s.status='closed' FROM shifts s LEFT JOIN appointments a ON a.shift_id=s.id WHERE s.shift_date BETWEEN @from AND @to GROUP BY s.id ORDER BY s.shift_date DESC,s.isotope_code");
        command.Parameters.AddWithValue("from", from); command.Parameters.AddWithValue("to", to);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.GetDecimal(3), checked((int)reader.GetInt64(4)), checked((int)reader.GetInt64(5)), reader.GetBoolean(6)));
        return result;
    }

    public async Task<ShiftResponse?> GetShiftAsync(DateOnly date, string isotopeCode)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT id,shift_date,isotope_code,source_activity_mbq,source_measured_at,half_life_minutes,dose_coefficient_mbq_per_kg,status='closed' FROM shifts WHERE shift_date=@date AND isotope_code=@isotope", connection);
        command.Parameters.AddWithValue("date", date); command.Parameters.AddWithValue("isotope", isotopeCode);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var shift = new ShiftResponse(reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2), reader.GetDecimal(3), reader.GetFieldValue<DateTimeOffset>(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetBoolean(7), []);
        await reader.CloseAsync();
        return shift with { Appointments = await GetAppointmentsAsync(connection, shift.Id) };
    }

    public async Task<ShiftResponse?> GetShiftByIdAsync(Guid shiftId)
    {
        await using var command = dataSource.CreateCommand("SELECT shift_date,isotope_code FROM shifts WHERE id=@id"); command.Parameters.AddWithValue("id", shiftId);
        await using var reader = await command.ExecuteReaderAsync(); if (!await reader.ReadAsync()) return null;
        return await GetShiftAsync(reader.GetFieldValue<DateOnly>(0), reader.GetString(1));
    }

    public async Task<ShiftResponse> CreateShiftAsync(Guid actorId, CreateShiftRequest request)
    {
        if (request.IsotopeCode is not ("F-18" or "Ga-68") || request.SourceActivityMbq < 0) throw new ArgumentException("Параметры смены некорректны.");
        await using var connection = await dataSource.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var settings = await GetIsotopeSettingsAsync(connection, transaction, request.IsotopeCode);
            await using var create = new NpgsqlCommand("INSERT INTO shifts (shift_date,isotope_code,source_activity_mbq,source_measured_at,half_life_minutes,dose_coefficient_mbq_per_kg,created_by) VALUES (@date,@isotope,@activity,@measured,@halfLife,@coefficient,@actor) RETURNING id", connection, transaction);
            create.Parameters.AddWithValue("date", request.ShiftDate); create.Parameters.AddWithValue("isotope", request.IsotopeCode); create.Parameters.AddWithValue("activity", request.SourceActivityMbq); create.Parameters.AddWithValue("measured", request.SourceMeasuredAt); create.Parameters.AddWithValue("halfLife", settings.HalfLife); create.Parameters.AddWithValue("coefficient", settings.Coefficient); create.Parameters.AddWithValue("actor", actorId);
            var shiftId = (Guid)(await create.ExecuteScalarAsync() ?? throw new InvalidOperationException("Не удалось создать смену."));
            foreach (var appointment in request.Appointments) await CreateAppointmentRecordAsync(connection, transaction, shiftId, request.ShiftDate, request.IsotopeCode, appointment);
            await EnsureSufficientActivityAsync(connection, transaction, shiftId, request.SourceActivityMbq, settings.HalfLife, settings.Coefficient);
            await AuditAsync(connection, transaction, actorId, "created", "shift", shiftId);
            await transaction.CommitAsync();
            return (await GetShiftAsync(request.ShiftDate, request.IsotopeCode)) ?? throw new InvalidOperationException("Не удалось загрузить смену.");
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<AppointmentResponse?> AddAppointmentAsync(Guid actorId, Guid shiftId, CreateAppointmentRequest request)
    {
        await using var connection = await dataSource.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var shift = await GetShiftCalculationAsync(connection, transaction, shiftId); if (shift is null) return null;
            var appointmentId = await CreateAppointmentRecordAsync(connection, transaction, shiftId, shift.Value.Date, shift.Value.Isotope, request);
            await EnsureSufficientActivityAsync(connection, transaction, shiftId, shift.Value.Activity, shift.Value.HalfLife, shift.Value.Coefficient);
            await AuditAsync(connection, transaction, actorId, "created", "appointment", appointmentId);
            await transaction.CommitAsync();
            return (await GetShiftByIdAsync(shiftId))?.Appointments.Single(item => item.Id == appointmentId);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<bool> CloseShiftAsync(Guid actorId, Guid shiftId)
    {
        await using var connection = await dataSource.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var check = new NpgsqlCommand("SELECT status,(SELECT count(*) FROM appointments WHERE shift_id=@id AND confirmed_at IS NULL) FROM shifts WHERE id=@id FOR UPDATE", connection, transaction); check.Parameters.AddWithValue("id", shiftId);
            await using var reader = await check.ExecuteReaderAsync(); if (!await reader.ReadAsync()) return false;
            if (reader.GetString(0) == "closed") return true;
            if (reader.GetInt64(1) > 0) throw new ArgumentException("Подтвердите все записи перед закрытием смены.");
            await reader.CloseAsync();
            await using var close = new NpgsqlCommand("UPDATE shifts SET status='closed',closed_at=now(),closed_by=@actor WHERE id=@id", connection, transaction); close.Parameters.AddWithValue("id", shiftId); close.Parameters.AddWithValue("actor", actorId); await close.ExecuteNonQueryAsync();
            await AuditAsync(connection, transaction, actorId, "closed", "shift", shiftId); await transaction.CommitAsync(); return true;
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<bool> UpdateSourceActivityAsync(Guid actorId, Guid shiftId, UpdateSourceActivityRequest request)
    {
        if (request.SourceActivityMbq < 0 || request.HalfLifeMinutes <= 0 || request.DoseCoefficientMbqPerKg <= 0) throw new ArgumentException("Параметры расчёта некорректны.");
        await using var connection = await dataSource.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var update = new NpgsqlCommand("UPDATE shifts SET source_activity_mbq=@activity,source_measured_at=@measured,half_life_minutes=@halfLife,dose_coefficient_mbq_per_kg=@coefficient WHERE id=@id", connection, transaction);
            update.Parameters.AddWithValue("id", shiftId); update.Parameters.AddWithValue("activity", request.SourceActivityMbq); update.Parameters.AddWithValue("measured", request.SourceMeasuredAt); update.Parameters.AddWithValue("halfLife", request.HalfLifeMinutes); update.Parameters.AddWithValue("coefficient", request.DoseCoefficientMbqPerKg);
            if (await update.ExecuteNonQueryAsync() != 1) return false;
            await EnsureSufficientActivityAsync(connection, transaction, shiftId, request.SourceActivityMbq, request.HalfLifeMinutes, request.DoseCoefficientMbqPerKg);
            await AuditAsync(connection, transaction, actorId, "updated_calculation", "shift", shiftId);
            await transaction.CommitAsync(); return true;
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<AppointmentResponse?> UpdateAppointmentAsync(Guid actorId, Guid appointmentId, UpdateAppointmentRequest request)
    {
        await using var connection = await dataSource.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var lookup = new NpgsqlCommand("SELECT s.id,s.shift_date,s.isotope_code,s.source_activity_mbq,s.half_life_minutes,s.dose_coefficient_mbq_per_kg FROM appointments a JOIN shifts s ON s.id=a.shift_id WHERE a.id=@id", connection, transaction); lookup.Parameters.AddWithValue("id", appointmentId);
            await using var reader = await lookup.ExecuteReaderAsync(); if (!await reader.ReadAsync()) return null;
            var shift = (Id: reader.GetGuid(0), Date: reader.GetFieldValue<DateOnly>(1), Isotope: reader.GetString(2), Activity: reader.GetDecimal(3), HalfLife: reader.GetDecimal(4), Coefficient: reader.GetDecimal(5)); await reader.CloseAsync();
            var schedule = await ResolveScheduleAsync(connection, transaction, shift.Date, shift.Isotope, request.ScannerId, request.ProtocolName, request.ScanStartAt, request.PatientCategory);
            ValidatePatient(request.PatientNumber, request.WeightKg);
            await EnsureNoScannerConflictAsync(connection, transaction, shift.Date, request.ScannerId, schedule.ScanStartAt, schedule.Duration, appointmentId);
            await using var update = new NpgsqlCommand("UPDATE appointments SET scanner_id=@scanner,patient_number_ciphertext=@number,patient_number_search_tokens=@tokens,weight_ciphertext=@weight,protocol_name=@protocol,injection_at=@injection,scan_start_at=@scanStart,duration_minutes=@duration,uptake_minutes=@uptake,patient_category=@category,confirmed_at=NULL,confirmed_by=NULL WHERE id=@id", connection, transaction);
            update.Parameters.AddWithValue("id", appointmentId); update.Parameters.AddWithValue("scanner", request.ScannerId); update.Parameters.AddWithValue("number", encryption.Encrypt(request.PatientNumber.Trim())); update.Parameters.AddWithValue("tokens", NpgsqlDbType.Array | NpgsqlDbType.Text, encryption.PatientNumberSearchTokens(request.PatientNumber)); update.Parameters.AddWithValue("weight", encryption.Encrypt(request.WeightKg.ToString(CultureInfo.InvariantCulture))); update.Parameters.AddWithValue("protocol", request.ProtocolName.Trim()); update.Parameters.AddWithValue("injection", schedule.InjectionAt); update.Parameters.AddWithValue("scanStart", schedule.ScanStartAt); update.Parameters.AddWithValue("duration", schedule.Duration); update.Parameters.AddWithValue("uptake", schedule.Uptake); update.Parameters.AddWithValue("category", request.PatientCategory);
            await update.ExecuteNonQueryAsync();
            await UpsertPatientAsync(connection, transaction, request.PatientNumber, request.WeightKg);
            await EnsureSufficientActivityAsync(connection, transaction, shift.Id, shift.Activity, shift.HalfLife, shift.Coefficient);
            await AuditAsync(connection, transaction, actorId, "updated", "appointment", appointmentId);
            await transaction.CommitAsync(); return (await GetShiftByIdAsync(shift.Id))?.Appointments.Single(item => item.Id == appointmentId);
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<bool> ConfirmAppointmentAsync(Guid actorId, Guid appointmentId)
    {
        await using var connection = await dataSource.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await using var update = new NpgsqlCommand("UPDATE appointments SET confirmed_at=now(),confirmed_by=@actor WHERE id=@id AND confirmed_at IS NULL", connection, transaction); update.Parameters.AddWithValue("actor", actorId); update.Parameters.AddWithValue("id", appointmentId);
        if (await update.ExecuteNonQueryAsync() != 1) return false;
        await AuditAsync(connection, transaction, actorId, "confirmed", "appointment", appointmentId); await transaction.CommitAsync(); return true;
    }

    public async Task<bool> DeleteAppointmentAsync(Guid actorId, Guid appointmentId)
    {
        await using var connection = await dataSource.OpenConnectionAsync(); await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("DELETE FROM appointments WHERE id=@id", connection, transaction); command.Parameters.AddWithValue("id", appointmentId);
        if (await command.ExecuteNonQueryAsync() != 1) return false;
        await AuditAsync(connection, transaction, actorId, "deleted", "appointment", appointmentId); await transaction.CommitAsync(); return true;
    }

    private async Task<Guid> CreateAppointmentRecordAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid shiftId, DateOnly shiftDate, string isotope, CreateAppointmentRequest request)
    {
        ValidatePatient(request.PatientNumber, request.WeightKg);
        await UpsertPatientAsync(connection, transaction, request.PatientNumber, request.WeightKg);
        var schedule = await ResolveScheduleAsync(connection, transaction, shiftDate, isotope, request.ScannerId, request.ProtocolName, request.ScanStartAt, request.PatientCategory);
        await EnsureNoScannerConflictAsync(connection, transaction, shiftDate, request.ScannerId, schedule.ScanStartAt, schedule.Duration);
        await using var command = new NpgsqlCommand("INSERT INTO appointments (shift_id,scanner_id,patient_number_ciphertext,patient_number_search_tokens,weight_ciphertext,isotope_code,protocol_name,injection_at,scan_start_at,duration_minutes,uptake_minutes,patient_category) VALUES (@shift,@scanner,@number,@tokens,@weight,@isotope,@protocol,@injection,@scanStart,@duration,@uptake,@category) RETURNING id", connection, transaction);
        command.Parameters.AddWithValue("shift", shiftId); command.Parameters.AddWithValue("scanner", request.ScannerId); command.Parameters.AddWithValue("number", encryption.Encrypt(request.PatientNumber.Trim())); command.Parameters.AddWithValue("tokens", NpgsqlDbType.Array | NpgsqlDbType.Text, encryption.PatientNumberSearchTokens(request.PatientNumber)); command.Parameters.AddWithValue("weight", encryption.Encrypt(request.WeightKg.ToString(CultureInfo.InvariantCulture))); command.Parameters.AddWithValue("isotope", isotope); command.Parameters.AddWithValue("protocol", request.ProtocolName.Trim()); command.Parameters.AddWithValue("injection", schedule.InjectionAt); command.Parameters.AddWithValue("scanStart", schedule.ScanStartAt); command.Parameters.AddWithValue("duration", schedule.Duration); command.Parameters.AddWithValue("uptake", schedule.Uptake); command.Parameters.AddWithValue("category", request.PatientCategory);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Не удалось создать запись."));
    }

    private async Task UpsertPatientAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string patientNumber, decimal weight)
    {
        await using var command = new NpgsqlCommand("INSERT INTO patients (number_ciphertext,number_fingerprint,last_weight_ciphertext) VALUES (@number,@fingerprint,@weight) ON CONFLICT (number_fingerprint) DO UPDATE SET number_ciphertext=EXCLUDED.number_ciphertext,last_weight_ciphertext=EXCLUDED.last_weight_ciphertext,updated_at=now()", connection, transaction);
        command.Parameters.AddWithValue("number", encryption.Encrypt(patientNumber.Trim())); command.Parameters.AddWithValue("fingerprint", encryption.Fingerprint(patientNumber)); command.Parameters.AddWithValue("weight", encryption.Encrypt(weight.ToString(CultureInfo.InvariantCulture)));
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidatePatient(string number, decimal weight)
    {
        if (string.IsNullOrWhiteSpace(number) || weight is <= 0 or > 350) throw new ArgumentException("Данные пациента некорректны.");
    }

    private static void ValidateScanner(string name, string model, short? year)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(model) || year is < 1900 or > 2100) throw new ArgumentException("Данные аппарата некорректны.");
    }

    private static void ValidateProtocol(UpsertProtocolRequest request)
    {
        if (request.IsotopeCode is not ("F-18" or "Ga-68") || string.IsNullOrWhiteSpace(request.Name) || request.DurationMinutes is < 1 or > 300 || request.UptakeMinutes is < 0 or > 360 || request.MaximumUptakeMinutes is < 0 or > 360 || request.MaximumUptakeMinutes is not null && request.MaximumUptakeMinutes < request.UptakeMinutes) throw new ArgumentException("Параметры протокола некорректны.");
    }

    private async Task<(decimal HalfLife, decimal Coefficient)> GetIsotopeSettingsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string isotope)
    {
        await using var command = new NpgsqlCommand("SELECT half_life_minutes,dose_coefficient_mbq_per_kg FROM isotope_settings WHERE isotope_code=@isotope", connection, transaction); command.Parameters.AddWithValue("isotope", isotope);
        await using var reader = await command.ExecuteReaderAsync(); if (!await reader.ReadAsync()) throw new ArgumentException("Изотоп не настроен."); return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private async Task<(DateOnly Date, string Isotope, decimal Activity, decimal HalfLife, decimal Coefficient)?> GetShiftCalculationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid shiftId)
    {
        await using var command = new NpgsqlCommand("SELECT shift_date,isotope_code,source_activity_mbq,half_life_minutes,dose_coefficient_mbq_per_kg FROM shifts WHERE id=@id", connection, transaction); command.Parameters.AddWithValue("id", shiftId);
        await using var reader = await command.ExecuteReaderAsync(); return await reader.ReadAsync() ? (reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4)) : null;
    }

    private async Task<(DateTimeOffset InjectionAt, DateTimeOffset ScanStartAt, short Duration, short Uptake)> ResolveScheduleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DateOnly date, string isotope, Guid scannerId, string protocolName, DateTimeOffset scanStartAt, string category)
    {
        if (category is not ("S" or "M" or "F") || string.IsNullOrWhiteSpace(protocolName) || scanStartAt.Date != date.ToDateTime(TimeOnly.MinValue).Date) throw new ArgumentException("Время исследования или категория пациента некорректны.");
        await using var protocol = new NpgsqlCommand("SELECT uptake_minutes FROM protocols WHERE isotope_code=@isotope AND name=@name AND is_active=true", connection, transaction); protocol.Parameters.AddWithValue("isotope", isotope); protocol.Parameters.AddWithValue("name", protocolName.Trim());
        var uptake = await protocol.ExecuteScalarAsync(); if (uptake is null) throw new ArgumentException("Выбранный протокол недоступен.");
        await using var profile = new NpgsqlCommand("SELECT preparation_minutes,scan_minutes FROM scanner_profiles WHERE scanner_id=@scanner AND patient_category=@category", connection, transaction); profile.Parameters.AddWithValue("scanner", scannerId); profile.Parameters.AddWithValue("category", category);
        await using var reader = await profile.ExecuteReaderAsync(); if (!await reader.ReadAsync()) throw new ArgumentException("Для аппарата не настроен профиль пациента.");
        var duration = checked((short)(reader.GetInt16(0) + reader.GetInt16(1))); var uptakeMinutes = Convert.ToInt16(uptake, CultureInfo.InvariantCulture);
        return (scanStartAt.AddMinutes(-uptakeMinutes), scanStartAt, duration, uptakeMinutes);
    }

    private async Task EnsureNoScannerConflictAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DateOnly date, Guid scannerId, DateTimeOffset scanStartAt, short duration, Guid? excludedId = null)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM appointments a JOIN shifts s ON s.id=a.shift_id WHERE s.shift_date=@date AND a.scanner_id=@scanner AND (@id IS NULL OR a.id<>@id) AND a.scan_start_at < @end AND a.scan_start_at + a.duration_minutes * interval '1 minute' > @start)", connection, transaction);
        command.Parameters.AddWithValue("date", date); command.Parameters.AddWithValue("scanner", scannerId); command.Parameters.Add("id", NpgsqlDbType.Uuid).Value = (object?)excludedId ?? DBNull.Value; command.Parameters.AddWithValue("start", scanStartAt); command.Parameters.AddWithValue("end", scanStartAt.AddMinutes(duration));
        if ((bool)(await command.ExecuteScalarAsync() ?? false)) throw new ArgumentException("Аппарат уже занят в это время.");
    }

    private async Task EnsureSufficientActivityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid shiftId, decimal sourceActivity, decimal halfLife, decimal coefficient)
    {
        await using var command = new NpgsqlCommand("SELECT injection_at,weight_ciphertext FROM appointments WHERE shift_id=@id ORDER BY injection_at", connection, transaction); command.Parameters.AddWithValue("id", shiftId);
        await using var reader = await command.ExecuteReaderAsync();
        var events = new List<(DateTimeOffset At, decimal Weight)>();
        while (await reader.ReadAsync()) events.Add((reader.GetFieldValue<DateTimeOffset>(0), decimal.Parse(encryption.Decrypt(reader.GetString(1)), CultureInfo.InvariantCulture)));
        decimal remaining = sourceActivity; DateTimeOffset? previous = null;
        foreach (var item in events)
        {
            if (previous is not null) remaining *= (decimal)Math.Pow(2d, (double)(-(decimal)(item.At - previous.Value).TotalMinutes / halfLife));
            var dose = item.Weight * coefficient;
            if (remaining + 0.0001m < dose) throw new ArgumentException($"Недостаточно активности для введения в {item.At:HH:mm}: доступно {remaining:F0} МБк, требуется {dose:F0} МБк.");
            remaining -= dose; previous = item.At;
        }
    }

    private async Task<IReadOnlyList<AppointmentResponse>> GetAppointmentsAsync(NpgsqlConnection connection, Guid shiftId)
    {
        var result = new List<AppointmentResponse>();
        await using var command = new NpgsqlCommand("SELECT a.id,a.scanner_id,s.name,a.patient_number_ciphertext,a.weight_ciphertext,a.isotope_code,a.protocol_name,a.injection_at,a.scan_start_at,a.duration_minutes,a.uptake_minutes,a.patient_category,a.confirmed_at FROM appointments a JOIN scanners s ON s.id=a.scanner_id WHERE a.shift_id=@shift ORDER BY a.injection_at", connection); command.Parameters.AddWithValue("shift", shiftId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), encryption.Decrypt(reader.GetString(3)), decimal.Parse(encryption.Decrypt(reader.GetString(4)), CultureInfo.InvariantCulture), reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7), reader.GetFieldValue<DateTimeOffset>(8), reader.GetInt16(9), reader.GetInt16(10), reader.GetString(11), !reader.IsDBNull(12)));
        return result;
    }

    private static async Task AuditAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorId, string action, string type, Guid entityId)
    {
        await using var command = new NpgsqlCommand("INSERT INTO audit_events (actor_id,action,entity_type,entity_id) VALUES (@actor,@action,@type,@id)", connection, transaction); command.Parameters.AddWithValue("actor", actorId); command.Parameters.AddWithValue("action", action); command.Parameters.AddWithValue("type", type); command.Parameters.AddWithValue("id", entityId); await command.ExecuteNonQueryAsync();
    }
}
