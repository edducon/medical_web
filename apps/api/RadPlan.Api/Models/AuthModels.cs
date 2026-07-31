namespace RadPlan.Api.Models;

public sealed record LoginRequest(string Email, string Password);
public sealed record SetupRequest(string Email, string Password, string SetupToken);
public sealed record CurrentUserResponse(string Email, string Role);
public sealed record UserResponse(Guid Id, string Email, string Role, DateTimeOffset CreatedAt);
public sealed record CreateUserRequest(string Email, string Password, string Role);
public sealed record CalculationRequest(decimal SourceActivityMbq, decimal HalfLifeMinutes, IReadOnlyList<InjectionEvent> Events);
public sealed record InjectionEvent(DateTimeOffset At, decimal DoseMbq);
public sealed record CalculationResponse(IReadOnlyList<CalculationPoint> Points);
public sealed record CalculationPoint(DateTimeOffset At, decimal ActivityBeforeMbq, decimal ActivityAfterMbq);
