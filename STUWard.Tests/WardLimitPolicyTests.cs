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
    public void CanPlaceWard_allows_unlimited_negative_limit()
    {
        Assert.True(WardLimitPolicy.CanPlaceWard(limit: -1, currentCount: 100));
    }

    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(3, 2, true)]
    [InlineData(3, 3, false)]
    [InlineData(3, 4, false)]
    public void CanPlaceWard_blocks_when_current_count_reaches_limit(
        int limit,
        int currentCount,
        bool expectedAllowed)
    {
        Assert.Equal(expectedAllowed, WardLimitPolicy.CanPlaceWard(limit, currentCount));
    }
}
