using RadPlan.Api.Models;

namespace RadPlan.Api.Services;

public static class ActivityCalculator
{
    public static CalculationResponse Calculate(CalculationRequest request)
    {
        if (request.SourceActivityMbq < 0 || request.HalfLifeMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(request));
        var ordered = request.Events.OrderBy(item => item.At).ToList();
        var remaining = request.SourceActivityMbq;
        DateTimeOffset? previous = null;
        var points = new List<CalculationPoint>(ordered.Count);
        foreach (var item in ordered)
        {
            if (item.DoseMbq < 0) throw new ArgumentOutOfRangeException(nameof(request));
            if (previous is not null)
            {
                var elapsedMinutes = (decimal)(item.At - previous.Value).TotalMinutes;
                remaining *= (decimal)Math.Pow(2d, (double)(-elapsedMinutes / request.HalfLifeMinutes));
            }
            var before = decimal.Round(remaining, 2, MidpointRounding.AwayFromZero);
            remaining = Math.Max(0, remaining - item.DoseMbq);
            points.Add(new CalculationPoint(item.At, before, decimal.Round(remaining, 2, MidpointRounding.AwayFromZero)));
            previous = item.At;
        }
        return new CalculationResponse(points);
    }
}
