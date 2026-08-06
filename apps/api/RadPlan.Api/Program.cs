using System.Security.Claims;
using System.Security.Cryptography;
using Isopoh.Cryptography.Argon2;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using RadPlan.Api.Models;
using RadPlan.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Postgres") ?? throw new InvalidOperationException("Postgres connection string is required.");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<FieldEncryptionService>();
builder.Services.AddSingleton<PlanRepository>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "__Host-radioplan";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
});
var dataProtectionPath = builder.Configuration["DataProtection:Path"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Radioplan");
if (!string.IsNullOrWhiteSpace(dataProtectionPath))
{
    Directory.CreateDirectory(dataProtectionPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));
}
builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
QuestPDF.Settings.License = Enum.TryParse<LicenseType>(builder.Configuration["QuestPdf:License"], true, out var pdfLicense) ? pdfLicense : LicenseType.Community;

var app = builder.Build();
await app.Services.GetRequiredService<PlanRepository>().BackfillPatientSearchTokensAsync();
app.UseExceptionHandler(exception => exception.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
}));
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/api/setup/status", async (NpgsqlDataSource dataSource) =>
{
    await using var command = dataSource.CreateCommand("SELECT count(*) = 0 FROM app_users");
    return Results.Ok(new { needsSetup = (bool)(await command.ExecuteScalarAsync() ?? false) });
}).AllowAnonymous();

app.MapPost("/api/setup", async (SetupRequest request, NpgsqlDataSource dataSource, IConfiguration configuration) =>
{
    var expectedToken = configuration["Setup:Token"];
    if (string.IsNullOrWhiteSpace(expectedToken) || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expectedToken), System.Text.Encoding.UTF8.GetBytes(request.SetupToken))) return Results.NotFound();
    await using var connection = await dataSource.OpenConnectionAsync();
    await using var countCommand = new NpgsqlCommand("SELECT count(*) FROM app_users", connection);
    if ((long)(await countCommand.ExecuteScalarAsync() ?? 0L) > 0) return Results.Conflict(new { error = "Setup already completed" });
    var hash = Argon2.Hash(request.Password);
    await using var command = new NpgsqlCommand("INSERT INTO app_users (email, password_hash, role) VALUES (@email, @passwordHash, 'administrator')", connection);
    command.Parameters.AddWithValue("email", request.Email.Trim().ToLowerInvariant());
    command.Parameters.AddWithValue("passwordHash", hash);
    await command.ExecuteNonQueryAsync();
    return Results.NoContent();
}).AllowAnonymous();

app.MapPost("/api/auth/login", async (HttpContext context, LoginRequest request, NpgsqlDataSource dataSource) =>
{
    await using var connection = await dataSource.OpenConnectionAsync();
    await using var command = new NpgsqlCommand("SELECT id, email, password_hash, role FROM app_users WHERE email = @email", connection);
    command.Parameters.AddWithValue("email", request.Email.Trim().ToLowerInvariant());
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || !Argon2.Verify(reader.GetString(2), request.Password)) return Results.Unauthorized();
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, reader.GetGuid(0).ToString()), new Claim(ClaimTypes.Name, reader.GetString(1)), new Claim(ClaimTypes.Role, reader.GetString(3)) };
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Ok(new CurrentUserResponse(reader.GetString(1), reader.GetString(3)));
}).AllowAnonymous();

app.MapPost("/api/auth/logout", async (HttpContext context) => { await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Results.NoContent(); });
app.MapGet("/api/auth/me", (ClaimsPrincipal user) => Results.Ok(new CurrentUserResponse(user.Identity?.Name ?? string.Empty, user.FindFirstValue(ClaimTypes.Role) ?? string.Empty)));
app.MapPost("/api/calculations/remaining", (CalculationRequest request) => Results.Ok(ActivityCalculator.Calculate(request)));
app.MapGet("/api/scanners", async (PlanRepository repository) => Results.Ok(await repository.GetScannersAsync()));
app.MapPost("/api/scanners", async (CreateScannerRequest request, PlanRepository repository) => Results.Created("/api/scanners", await repository.AddScannerAsync(request))).RequireAuthorization(new AuthorizeAttribute { Roles = "administrator" });
app.MapPut("/api/scanners/{id:guid}", async (Guid id, UpdateScannerRequest request, PlanRepository repository) => await repository.UpdateScannerAsync(id, request) ? Results.NoContent() : Results.NotFound()).RequireAuthorization(new AuthorizeAttribute { Roles = "administrator" });
app.MapGet("/api/scanner-profiles", async (PlanRepository repository) => Results.Ok(await repository.GetScannerProfilesAsync()));
app.MapPut("/api/scanners/{id:guid}/profiles/{category}", async (Guid id, string category, UpdateScannerProfileRequest request, PlanRepository repository) =>
{
    try { return await repository.UpdateScannerProfileAsync(id, category, request) ? Results.NoContent() : Results.NotFound(); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
}).RequireAuthorization(new AuthorizeAttribute { Roles = "administrator" });
app.MapGet("/api/settings/isotopes", async (PlanRepository repository) => Results.Ok(await repository.GetSettingsAsync()));
app.MapPut("/api/settings/isotopes/{isotope}", async (string isotope, UpdateIsotopeSettingsRequest request, PlanRepository repository) =>
{
    try { return await repository.UpdateSettingsAsync(isotope, request) ? Results.NoContent() : Results.NotFound(); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
}).RequireAuthorization(new AuthorizeAttribute { Roles = "administrator" });
app.MapGet("/api/protocols", async (PlanRepository repository) => Results.Ok(await repository.GetProtocolsAsync()));
app.MapPost("/api/protocols", async (UpsertProtocolRequest request, PlanRepository repository) =>
{
    try { return Results.Created("/api/protocols", await repository.AddProtocolAsync(request)); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
}).RequireAuthorization(new AuthorizeAttribute { Roles = "administrator" });
app.MapPut("/api/protocols/{id:guid}", async (Guid id, UpsertProtocolRequest request, PlanRepository repository) =>
{
    try { return await repository.UpdateProtocolAsync(id, request) ? Results.NoContent() : Results.NotFound(); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
}).RequireAuthorization(new AuthorizeAttribute { Roles = "administrator" });
app.MapGet("/api/shifts", async (DateOnly from, DateOnly to, PlanRepository repository) => Results.Ok(await repository.GetShiftHistoryAsync(from, to)));
app.MapGet("/api/shifts/{date}/{isotopeCode}", async (DateOnly date, string isotopeCode, PlanRepository repository) =>
{
    var shift = await repository.GetShiftAsync(date, isotopeCode);
    return shift is null ? Results.NotFound() : Results.Ok(shift);
});
app.MapGet("/api/patients", async (string number, PlanRepository repository) => Results.Ok(await repository.SearchPatientsAsync(number)));
app.MapPost("/api/shifts", async (ClaimsPrincipal user, CreateShiftRequest request, PlanRepository repository) =>
{
    try { return Results.Created($"/api/shifts/{request.ShiftDate}/{request.IsotopeCode}", await repository.CreateShiftAsync(UserId(user), request)); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
app.MapPost("/api/shifts/{shiftId:guid}/appointments", async (ClaimsPrincipal user, Guid shiftId, CreateAppointmentRequest request, PlanRepository repository) =>
{
    try
    {
        var appointment = await repository.AddAppointmentAsync(UserId(user), shiftId, request);
        return appointment is null ? Results.NotFound() : Results.Created($"/api/appointments/{appointment.Id}", appointment);
    }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
app.MapPost("/api/shifts/{shiftId:guid}/close", async (ClaimsPrincipal user, Guid shiftId, PlanRepository repository) =>
{
    try { return await repository.CloseShiftAsync(UserId(user), shiftId) ? Results.NoContent() : Results.NotFound(); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
app.MapPut("/api/shifts/{shiftId:guid}/source-activity", async (ClaimsPrincipal user, Guid shiftId, UpdateSourceActivityRequest request, PlanRepository repository) =>
{
    try { return await repository.UpdateSourceActivityAsync(UserId(user), shiftId, request) ? Results.NoContent() : Results.NotFound(); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
app.MapPut("/api/appointments/{appointmentId:guid}", async (ClaimsPrincipal user, Guid appointmentId, UpdateAppointmentRequest request, PlanRepository repository) =>
{
    try { var appointment = await repository.UpdateAppointmentAsync(UserId(user), appointmentId, request); return appointment is null ? Results.NotFound() : Results.Ok(appointment); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});
app.MapDelete("/api/appointments/{appointmentId:guid}", async (ClaimsPrincipal user, Guid appointmentId, PlanRepository repository) => await repository.DeleteAppointmentAsync(UserId(user), appointmentId) ? Results.NoContent() : Results.NotFound());
app.MapGet("/api/shifts/{shiftId:guid}/report", async (Guid shiftId, PlanRepository repository) =>
{
    var shift = await repository.GetShiftByIdAsync(shiftId);
    return shift is null ? Results.NotFound() : Results.File(new ShiftReportDocument(shift).GeneratePdf(), "application/pdf", $"radioplan-{shift.ShiftDate:yyyy-MM-dd}-{shift.IsotopeCode}.pdf");
});
app.MapPost("/api/appointments/{appointmentId:guid}/confirm", async (ClaimsPrincipal user, Guid appointmentId, PlanRepository repository) =>
    await repository.ConfirmAppointmentAsync(UserId(user), appointmentId) ? Results.NoContent() : Results.NotFound());
app.MapGet("/api/users", async (NpgsqlDataSource dataSource) =>
{
    var users = new List<UserResponse>(); await using var command = dataSource.CreateCommand("SELECT id, email, role, created_at FROM app_users ORDER BY created_at"); await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) users.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3))); return Results.Ok(users);
}).RequireAuthorization(new AuthorizeAttribute { Roles = "administrator" });
app.MapPost("/api/users", async (CreateUserRequest request, NpgsqlDataSource dataSource) =>
{
    if (request.Role is not ("administrator" or "operator") || string.IsNullOrWhiteSpace(request.Email) || request.Password.Length < 12) return Results.BadRequest(new { error = "User has invalid values." });
    await using var command = dataSource.CreateCommand("INSERT INTO app_users (email, password_hash, role) VALUES (@email, @password, @role) RETURNING id, email, role, created_at"); command.Parameters.AddWithValue("email", request.Email.Trim().ToLowerInvariant()); command.Parameters.AddWithValue("password", Argon2.Hash(request.Password)); command.Parameters.AddWithValue("role", request.Role);
    try { await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync(); return Results.Created("/api/users", new UserResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3))); }
    catch (PostgresException exception) when (exception.SqlState == "23505") { return Results.Conflict(new { error = "Email is already used." }); }
}).RequireAuthorization(new AuthorizeAttribute { Roles = "administrator" });

app.Run();

static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());
