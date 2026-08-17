using System;

namespace STUWard;

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

    internal static string GetGuildsAccountId(string accountId)
    {
        var trimmedAccountId = accountId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedAccountId))
        {
            return string.Empty;
        }

        if (trimmedAccountId.StartsWith(SteamPrefix, StringComparison.Ordinal))
        {
            return trimmedAccountId;
        }

        return IsNumericAccountId(trimmedAccountId)
            ? $"{SteamPrefix}{trimmedAccountId}"
            : trimmedAccountId;
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
