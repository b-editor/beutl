using Beutl.Media;

namespace Beutl.UnitTests;

// Builds TimeRange values from seconds for the tests; production code builds them from TimeSpans.
internal static class TimeRangeTestExtensions
{
    extension(TimeRange)
    {
        public static TimeRange FromSeconds(double start, double duration)
            => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(duration));

        public static TimeRange FromSeconds(double duration)
            => new(TimeSpan.FromSeconds(duration));
    }
}
