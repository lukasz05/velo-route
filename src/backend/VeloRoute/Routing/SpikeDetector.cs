namespace VeloRoute.Routing;

/// <summary>
/// Locality-aware companion to <see cref="SmoothnessCalculator"/>: the longest run of
/// consecutive sharp turns, which a global count-average cannot surface (a single severe
/// local out-and-back gets diluted across the whole route length).
/// </summary>
internal static class SpikeDetector
{
    public static int Compute(RouteResult route)
    {
        var flags = SmoothnessCalculator.ComputeSharpTurnFlags(route);

        int longest = 0;
        int current = 0;
        foreach (bool flag in flags)
        {
            current = flag ? current + 1 : 0;
            if (current > longest)
                longest = current;
        }

        return longest;
    }
}
