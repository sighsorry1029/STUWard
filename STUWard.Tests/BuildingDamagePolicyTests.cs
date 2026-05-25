namespace STUWard.Tests;

using STUWard;
using Xunit;

public sealed class BuildingDamagePolicyTests
{
    [Fact]
    public void Monster_damage_to_protected_building_uses_friendly_ward_reason()
    {
        var result = BuildingDamagePolicy.Evaluate(new BuildingDamagePolicyInput(
            isBuildingTarget: true,
            sourceKind: DamageSourceKind.MonsterAI,
            playerId: 0L,
            insideEnabledWard: true,
            blocksHostileCreatureDamage: true,
            playerHasAccess: true));

        Assert.Equal(BuildingDamageBlockReason.FriendlyWardProtection, result);
    }

    [Theory]
    [InlineData((int)DamageSourceKind.Player)]
    [InlineData((int)DamageSourceKind.TamedCreature)]
    public void Friendly_damage_to_building_inside_enabled_ward_uses_friendly_ward_reason(int sourceKind)
    {
        var result = BuildingDamagePolicy.Evaluate(new BuildingDamagePolicyInput(
            isBuildingTarget: true,
            sourceKind: (DamageSourceKind)sourceKind,
            playerId: 42L,
            insideEnabledWard: true,
            blocksHostileCreatureDamage: false,
            playerHasAccess: true));

        Assert.Equal(BuildingDamageBlockReason.FriendlyWardProtection, result);
    }

    [Fact]
    public void Player_without_access_gets_no_access_reason()
    {
        var result = BuildingDamagePolicy.Evaluate(new BuildingDamagePolicyInput(
            isBuildingTarget: false,
            sourceKind: DamageSourceKind.Player,
            playerId: 42L,
            insideEnabledWard: false,
            blocksHostileCreatureDamage: false,
            playerHasAccess: false));

        Assert.Equal(BuildingDamageBlockReason.NoAccess, result);
    }

    [Fact]
    public void Unknown_or_unresolved_actor_does_not_block_by_access()
    {
        var result = BuildingDamagePolicy.Evaluate(new BuildingDamagePolicyInput(
            isBuildingTarget: false,
            sourceKind: DamageSourceKind.Unknown,
            playerId: 0L,
            insideEnabledWard: false,
            blocksHostileCreatureDamage: false,
            playerHasAccess: false));

        Assert.Equal(BuildingDamageBlockReason.None, result);
    }
}
