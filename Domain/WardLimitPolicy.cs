using System.Collections.Generic;

namespace STUWard;

internal readonly struct WardLimitEvaluation
{
    internal WardLimitEvaluation(bool allowed, string reason, int limit, int currentCount)
    {
        Allowed = allowed;
        Reason = reason;
        Limit = limit;
        CurrentCount = currentCount;
    }

    internal bool Allowed { get; }
    internal string Reason { get; }
    internal int Limit { get; }
    internal int CurrentCount { get; }
    internal bool IsUnlimited => Limit < 0;
}

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

    internal static WardLimitEvaluation EvaluatePlacement(int limit, int currentCount)
    {
        if (limit < 0)
        {
            return new WardLimitEvaluation(allowed: true, reason: "unlimited", limit, currentCount);
        }

        if (currentCount >= limit)
        {
            return new WardLimitEvaluation(allowed: false, reason: "limit_reached", limit, currentCount);
        }

        return new WardLimitEvaluation(allowed: true, reason: "under_limit", limit, currentCount);
    }
}
