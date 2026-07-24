using System.Collections.Generic;

namespace STUWard;

internal static class WardLimitPolicy
{
    internal static int GetEffectiveLimit(
        string accountId,
        IReadOnlyDictionary<string, int> overrides,
        int defaultLimit)
    {
        if (!string.IsNullOrWhiteSpace(accountId) &&
            overrides != null &&
            overrides.TryGetValue(accountId, out var overrideLimit))
        {
            return overrideLimit;
        }

        return defaultLimit;
    }

    internal static bool CanPlaceWard(int limit, int currentCount)
    {
        return limit < 0 || currentCount < limit;
    }
}
