using System;

namespace STUWard;

internal readonly struct GuildAccountLookupCandidates
{
    internal GuildAccountLookupCandidates(string primaryAccountId, string fallbackAccountId)
    {
        PrimaryAccountId = primaryAccountId ?? string.Empty;
        FallbackAccountId = fallbackAccountId ?? string.Empty;
    }

    internal string PrimaryAccountId { get; }
    internal string FallbackAccountId { get; }
    internal bool HasFallback =>
        !string.IsNullOrWhiteSpace(FallbackAccountId) &&
        !string.Equals(PrimaryAccountId, FallbackAccountId, StringComparison.Ordinal);
}

internal static class GuildIdentityPolicy
{
    private const string SteamPrefix = "Steam_";

    internal static string NormalizeAccountId(string? accountId)
    {
        var trimmedAccountId = accountId?.Trim() ?? string.Empty;
        return trimmedAccountId.StartsWith(SteamPrefix, StringComparison.Ordinal)
            ? trimmedAccountId.Substring(SteamPrefix.Length)
            : trimmedAccountId;
    }

    internal static GuildAccountLookupCandidates GetAccountLookupCandidates(string accountId)
    {
        var trimmedAccountId = accountId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedAccountId))
        {
            return new GuildAccountLookupCandidates(string.Empty, string.Empty);
        }

        if (trimmedAccountId.StartsWith(SteamPrefix, StringComparison.Ordinal))
        {
            var unprefixedAccountId = trimmedAccountId.Substring(SteamPrefix.Length);
            return new GuildAccountLookupCandidates(trimmedAccountId, unprefixedAccountId);
        }

        return IsNumericAccountId(trimmedAccountId)
            ? new GuildAccountLookupCandidates($"{SteamPrefix}{trimmedAccountId}", trimmedAccountId)
            : new GuildAccountLookupCandidates(trimmedAccountId, string.Empty);
    }

    internal static bool CanApplyAuthoritativeGuild(int reportedGuildId, int authoritativeGuildId)
    {
        return authoritativeGuildId != 0 || reportedGuildId == 0;
    }

    private static bool IsNumericAccountId(string accountId)
    {
        for (var index = 0; index < accountId.Length; index++)
        {
            if (!char.IsDigit(accountId[index]))
            {
                return false;
            }
        }

        return accountId.Length > 0;
    }
}
