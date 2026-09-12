#nullable enable

namespace Eniris.Examples.Helpers;

public static class DateRangeHelper
{
    public static ExampleDateRange LastDays(int days, DateTimeOffset? end = null)
    {
        if (days <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "The date range must be at least one day.");
        }

        var effectiveEnd = end?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        return new ExampleDateRange(effectiveEnd.AddDays(-days), effectiveEnd, $"last {days} days");
    }
}

public sealed record ExampleDateRange(DateTimeOffset Start, DateTimeOffset End, string Label);
