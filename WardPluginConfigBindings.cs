using System;
using BepInEx.Configuration;
using UnityEngine;

namespace STUWard;

internal static class WardPluginConfigBindings
{
    private static bool _handlersBound;
    private const int GeneralOrderStart = 700;
    private const int RestrictionOrderStart = 900;
    private const int ClientOrderStart = 300;
    private const int OrderStep = 10;

    internal static void BindAll()
    {
        UnbindAll();
        BindGeneral();
        BindRestrictions();
        BindClient();
        BindHandlers();
    }

    internal static void UnbindAll()
    {
        if (!_handlersBound)
        {
            return;
        }

        UnbindHandler(Plugin.MaxWardRadius, HandleMaxWardRadiusChanged);
        UnbindHandler(Plugin.MaxOutpostWardRadius, HandleMaxWardRadiusChanged);
        UnbindHandler(Plugin.WardRangeConfiguration, HandleWardRangeConfigurationChanged);
        UnbindHandler(Plugin.GuildsIntegration, HandleGroupIntegrationChanged);
        UnbindHandler(Plugin.ClanIntegration, HandleGroupIntegrationChanged);
        UnbindHandler(Plugin.HostileCreatureStructureProtection, HandleWardPresenceConfigChanged);
        UnbindHandler(Plugin.DisableVanillaGuardStoneRecipe, HandleRecipeSettingsChanged);
        UnbindHandler(Plugin.StuWardRecipe, HandleRecipeSettingsChanged);
        UnbindHandler(Plugin.OutpostWardRecipe, HandleRecipeSettingsChanged);
        UnbindHandler(Plugin.WardMinimapPinScale, HandleLocalWardPinConfigChanged);
        UnbindHandler(Plugin.WardMinimapActiveRanges, HandleLocalWardPinConfigChanged);
        UnbindHandler(Plugin.WardBoundaryBrightenMode, HandleLocalBoundaryBrightenModeChanged);
        _handlersBound = false;
    }

    private static void BindGeneral()
    {
        Plugin.ServerConfigLocked = Plugin.BindConfigEntry(
            "1 - General",
            "Lock Configuration",
            Plugin.Toggle.On,
            "If on, the configuration is locked and can be changed by server admins only.",
            configManagerOrder: GeneralOrderStart
        );
        _ = Plugin.ConfigSync.AddLockingConfigEntry(Plugin.ServerConfigLocked);

        Plugin.MaxWardRadius = Plugin.BindConfigEntry(
            "1 - General",
            "Max Ward Radius",
            32f,
            "Maximum radius automatically assigned when a regular Ward is placed. Valid range: 8 to 64.",
            configManagerOrder: GeneralOrderStart - OrderStep
        );

        Plugin.MaxOutpostWardRadius = Plugin.BindConfigEntry(
            "1 - General",
            "Max Outpost Ward Radius",
            16f,
            "Maximum radius automatically assigned when an Outpost Ward is placed. Valid range: 8 to 64.",
            configManagerOrder: GeneralOrderStart - OrderStep - OrderStep / 2
        );

        Plugin.MaxWardsPerSteamId = Plugin.BindConfigEntry(
            "1 - General",
            "Max Wards Per Steam ID",
            3,
            "Maximum number of regular Wards allowed per Steam/platform account. Set to -1 for unlimited. Account exceptions use ward_limit_overrides in STUWard.yml.",
            configManagerOrder: GeneralOrderStart - OrderStep * 2
        );

        Plugin.MaxOutpostWardsPerSteamId = Plugin.BindConfigEntry(
            "1 - General",
            "Max Outpost Wards Per Steam ID",
            3,
            "Maximum number of Outpost Wards allowed per Steam/platform account, separate from regular Wards. Set to -1 for unlimited. Account exceptions use outpost_ward_limit_overrides in STUWard.yml.",
            configManagerOrder: GeneralOrderStart - OrderStep * 2 - OrderStep / 2
        );

        Plugin.PickupBlockMode = Plugin.BindConfigEntry(
            "1 - General",
            "Pickup Block Mode",
            Plugin.PickupBlockRule.BlockAllExceptWhitelist,
            "Pickup rule inside a foreign enabled ward. BlockAllExceptWhitelist blocks every item pickup except pickup_whitelist. AllowAllExceptBlacklist allows item pickup except pickup_blacklist.",
            configManagerOrder: GeneralOrderStart - OrderStep * 3
        );

        Plugin.HostileCreatureStructureProtection = Plugin.BindConfigEntry(
            "1 - General",
            "Hostile Creature Structure Protection Mode",
            Plugin.HostileCreatureStructureProtectionMode.UnattendedOnly,
            "Controls whether building pieces inside an enabled ward ignore damage from MonsterAI-controlled attackers. Off disables this extra protection. UnattendedOnly protects while no trusted player is nearby. Always protects regardless of trusted player presence. UnattendedOnly uses an 8 m trusted-player range buffer, 10 s grace time, and 1 s presence refresh.",
            configManagerOrder: GeneralOrderStart - OrderStep * 4
        );

        Plugin.WardRangeConfiguration = Plugin.BindConfigEntry(
            "1 - General",
            "Ward Range Configuration",
            Plugin.Toggle.Off,
            "Controls whether trusted players can adjust each ward's radius in Ward Settings. Off hides the control and preserves the stored radius; new wards still receive the largest legal radius.",
            configManagerOrder: GeneralOrderStart - OrderStep * 5
        );

        Plugin.DisableVanillaGuardStoneRecipe = Plugin.BindConfigEntry(
            "1 - General",
            "Disable Vanilla Guard Stone Recipe",
            Plugin.Toggle.On,
            "If on, the vanilla guard_stone build recipe is removed from the Hammer piece table while STUWard remains available.",
            configManagerOrder: GeneralOrderStart - OrderStep * 6
        );

        Plugin.StuWardRecipe = Plugin.BindConfigEntry(
            "1 - General",
            "STUWard Recipe",
            "GreydwarfEye:1,BoneFragments:3,Flint:5,Wood:7",
            "Recipe for regular Wards. Format: ItemPrefab:Amount[:Recover], ...",
            configManagerOrder: GeneralOrderStart - OrderStep * 7
        );

        Plugin.OutpostWardRecipe = Plugin.BindConfigEntry(
            "1 - General",
            "Outpost Ward Recipe",
            "Ectoplasm:3,Iron:5,Chitin:7,ElderBark:9",
            "Recipe for Outpost Wards. Format: ItemPrefab:Amount[:Recover], ...",
            configManagerOrder: GeneralOrderStart - OrderStep * 7 - OrderStep / 2
        );

        Plugin.GuildsIntegration = Plugin.BindConfigEntry(
            "1 - General",
            "Guilds Integration",
            Plugin.Toggle.On,
            "Grants automatic ward trust and same-guild ward overlap through Guilds. Off requires individual registration; owner and admin-debug access remain. Existing overlapping wards keep their radius, but shared areas require access to every covering ward. Does not disable the Guilds mod itself.",
            configManagerOrder: GeneralOrderStart - OrderStep * 8
        );

        Plugin.ClanIntegration = Plugin.BindConfigEntry(
            "1 - General",
            "Clan Integration",
            Plugin.Toggle.On,
            "Grants automatic ward trust and same-clan ward overlap through Clan's authorized primary membership; Guests are excluded. Off requires individual registration; owner and admin-debug access remain. Existing overlapping wards keep their radius, but shared areas require access to every covering ward. Does not disable the Clan mod itself.",
            configManagerOrder: GeneralOrderStart - OrderStep * 9
        );
    }

    private static void BindRestrictions()
    {
        Plugin.RestrictionModes.Clear();
        var definitions = WardSettings.RestrictionDefinitions;
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            Plugin.RestrictionModes[definition.Restriction] = BindRestrictionMode(
                definition.ConfigName,
                definition.ConfigDescription,
                RestrictionOrderStart - index * OrderStep);
        }
    }

    private static ConfigEntry<Plugin.RestrictionServerMode> BindRestrictionMode(string name, string description, int configManagerOrder)
    {
        return Plugin.BindConfigEntry(
            "2 - Ward Restrictions",
            $"{name} Restriction",
            Plugin.RestrictionServerMode.ForcedOn,
            $"{description} ForcedOn preserves the server rule. NotForced lets each ward owner toggle this restriction in the ward settings UI.",
            configManagerOrder: configManagerOrder);
    }

    private static void BindClient()
    {
        Plugin.WardSettingsShortcut = Plugin.BindConfigEntry(
            "3 - Client",
            "Ward Settings Shortcut",
            new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt),
            "Shortcut used to open the ward settings UI while looking at your ward. Example values: LeftAlt + E, F7",
            synchronizedSetting: false,
            configManagerOrder: ClientOrderStart
        );

        Plugin.WardMinimapPinScale = Plugin.BindConfigEntry(
            "3 - Client",
            "Ward Minimap Pin Scale",
            1,
            "0 disables ward icon pins. 1 is the default icon size. 100 means x100 icon size.",
            synchronizedSetting: false,
            configManagerOrder: ClientOrderStart - OrderStep
        );

        Plugin.WardMinimapActiveRanges = Plugin.BindConfigEntry(
            "3 - Client",
            "Ward Minimap Active Ranges",
            Plugin.Toggle.On,
            "If on, enabled managed wards also show their active radius on the minimap and map.",
            synchronizedSetting: false,
            configManagerOrder: ClientOrderStart - OrderStep * 2
        );

        Plugin.WardBoundaryBrightenMode = Plugin.BindConfigEntry(
            "3 - Client",
            "Ward Boundary Brighten Mode",
            Plugin.BoundaryBrightenMode.All,
            "Controls which enabled ward boundaries brighten near the local player. TrustedOnly applies to wards where the player is trusted, UntrustedOnly applies to wards where the player is not trusted, All applies to every ward, and Off disables the boundary cue. Placement-conflict highlights remain enabled in every mode.",
            synchronizedSetting: false,
            configManagerOrder: ClientOrderStart - OrderStep * 3
        );

    }

    private static void BindHandlers()
    {
        BindHandler(Plugin.MaxWardRadius, HandleMaxWardRadiusChanged);
        BindHandler(Plugin.MaxOutpostWardRadius, HandleMaxWardRadiusChanged);
        BindHandler(Plugin.WardRangeConfiguration, HandleWardRangeConfigurationChanged);
        BindHandler(Plugin.GuildsIntegration, HandleGroupIntegrationChanged);
        BindHandler(Plugin.ClanIntegration, HandleGroupIntegrationChanged);
        BindHandler(Plugin.HostileCreatureStructureProtection, HandleWardPresenceConfigChanged);
        BindHandler(Plugin.DisableVanillaGuardStoneRecipe, HandleRecipeSettingsChanged);
        BindHandler(Plugin.StuWardRecipe, HandleRecipeSettingsChanged);
        BindHandler(Plugin.OutpostWardRecipe, HandleRecipeSettingsChanged);
        BindHandler(Plugin.WardMinimapPinScale, HandleLocalWardPinConfigChanged);
        BindHandler(Plugin.WardMinimapActiveRanges, HandleLocalWardPinConfigChanged);
        BindHandler(Plugin.WardBoundaryBrightenMode, HandleLocalBoundaryBrightenModeChanged);
        _handlersBound = true;
    }

    private static void HandleMaxWardRadiusChanged(object? _, EventArgs __)
    {
        WardSettings.HandleMaxRadiusChanged();
    }

    private static void HandleWardRangeConfigurationChanged(object? _, EventArgs __)
    {
        WardGuiController.Instance?.HandleWardRangeConfigurationChanged();
    }

    private static void HandleWardPresenceConfigChanged(object? _, EventArgs __)
    {
        ManagedWardPresenceService.Invalidate();
    }

    private static void HandleGroupIntegrationChanged(object? _, EventArgs __)
    {
        WardGroupCompat.HandleIntegrationChanged();
    }

    private static void HandleRecipeSettingsChanged(object? _, EventArgs __)
    {
        StuWardPrefab.ApplyRecipeSettings();
    }

    private static void HandleLocalWardPinConfigChanged(object? _, EventArgs __)
    {
        WardMinimapPinsManager.HandleLocalConfigChanged();
    }

    private static void HandleLocalBoundaryBrightenModeChanged(object? _, EventArgs __)
    {
        WardSettings.HandleLocalBoundaryBrightenModeChanged();
    }

    private static void BindHandler<T>(ConfigEntry<T>? entry, EventHandler handler)
    {
        if (entry == null)
        {
            return;
        }

        entry.SettingChanged += handler;
    }

    private static void UnbindHandler<T>(ConfigEntry<T>? entry, EventHandler handler)
    {
        if (entry == null)
        {
            return;
        }

        entry.SettingChanged -= handler;
    }
}
