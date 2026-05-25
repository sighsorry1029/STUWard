namespace STUWard;

internal enum BuildingDamageBlockReason
{
    None,
    NoAccess,
    FriendlyWardProtection,
    UnresolvedSender
}

internal enum DamageSourceKind
{
    Unknown,
    Player,
    TamedCreature,
    MonsterAI
}

internal readonly struct BuildingDamagePolicyInput
{
    internal BuildingDamagePolicyInput(
        bool isBuildingTarget,
        DamageSourceKind sourceKind,
        long playerId,
        bool insideEnabledWard,
        bool blocksHostileCreatureDamage,
        bool playerHasAccess)
    {
        IsBuildingTarget = isBuildingTarget;
        SourceKind = sourceKind;
        PlayerId = playerId;
        InsideEnabledWard = insideEnabledWard;
        BlocksHostileCreatureDamage = blocksHostileCreatureDamage;
        PlayerHasAccess = playerHasAccess;
    }

    internal bool IsBuildingTarget { get; }
    internal DamageSourceKind SourceKind { get; }
    internal long PlayerId { get; }
    internal bool InsideEnabledWard { get; }
    internal bool BlocksHostileCreatureDamage { get; }
    internal bool PlayerHasAccess { get; }
}

internal static class BuildingDamagePolicy
{
    internal static BuildingDamageBlockReason Evaluate(BuildingDamagePolicyInput input)
    {
        if (input.IsBuildingTarget &&
            input.SourceKind == DamageSourceKind.MonsterAI &&
            input.BlocksHostileCreatureDamage)
        {
            return BuildingDamageBlockReason.FriendlyWardProtection;
        }

        if (input.IsBuildingTarget &&
            IsFriendlyBuildingDamageSource(input.SourceKind) &&
            input.InsideEnabledWard)
        {
            return BuildingDamageBlockReason.FriendlyWardProtection;
        }

        if (input.PlayerId != 0L && !input.PlayerHasAccess)
        {
            return BuildingDamageBlockReason.NoAccess;
        }

        return BuildingDamageBlockReason.None;
    }

    private static bool IsFriendlyBuildingDamageSource(DamageSourceKind sourceKind)
    {
        return sourceKind == DamageSourceKind.Player || sourceKind == DamageSourceKind.TamedCreature;
    }
}
