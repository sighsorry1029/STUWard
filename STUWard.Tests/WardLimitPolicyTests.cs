namespace STUWard.Tests;

using STUWard;
using Xunit;

public sealed class WardLimitPolicyTests
{
    [Fact]
    public void GetEffectiveLimit_uses_account_override_before_default()
    {
        var overrides = new Dictionary<string, int>
        {
            ["123"] = 12
        };

        var limit = WardLimitPolicy.GetEffectiveLimit("123", overrides, defaultLimit: 3);

        Assert.Equal(12, limit);
    }

    [Fact]
    public void GetEffectiveLimit_falls_back_to_default_when_override_is_missing()
    {
        var overrides = new Dictionary<string, int>
        {
            ["123"] = 12
        };

        var limit = WardLimitPolicy.GetEffectiveLimit("456", overrides, defaultLimit: 3);

        Assert.Equal(3, limit);
    }

    [Fact]
    public void EvaluatePlacement_allows_unlimited_negative_limit()
    {
        var result = WardLimitPolicy.EvaluatePlacement(limit: -1, currentCount: 100);

        Assert.True(result.Allowed);
        Assert.True(result.IsUnlimited);
        Assert.Equal("unlimited", result.Reason);
    }

    [Theory]
    [InlineData(3, 0, true, "under_limit")]
    [InlineData(3, 2, true, "under_limit")]
    [InlineData(3, 3, false, "limit_reached")]
    [InlineData(3, 4, false, "limit_reached")]
    public void EvaluatePlacement_blocks_when_current_count_reaches_limit(
        int limit,
        int currentCount,
        bool expectedAllowed,
        string expectedReason)
    {
        var result = WardLimitPolicy.EvaluatePlacement(limit, currentCount);

        Assert.Equal(expectedAllowed, result.Allowed);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(limit, result.Limit);
        Assert.Equal(currentCount, result.CurrentCount);
    }
}
