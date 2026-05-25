using System;
using BepInEx.Configuration;
using UnityEngine;

namespace STUWard;

internal static class WardPluginConfigBindings
{
    private static bool _handlersBound;

    internal static void BindAll()
    {
        UnbindAll();
        BindGeneral();
        BindClient();
        BindDebug();
        BindHandlers();
    }

    internal static void UnbindAll()
    {
        if (!_handlersBound)
        {
            return;
        }

        UnbindHandler(Plugin.MaxWardRadius, HandleMaxWardRadiusChanged);
        UnbindHandler(Plugin.MaxWardsPerSteamId, HandleMaxWardLimitChanged);
        UnbindHandler(Plugin.HostileCreatureStructureProtection, HandleWardPresenceConfigChanged);
        UnbindHandler(Plugin.UnattendedWardTrustedPlayerRangeBuffer, HandleWardPresenceConfigChanged);
        UnbindHandler(Plugin.UnattendedWardTrustedPresenceGraceSeconds, HandleWardPresenceConfigChanged);
        UnbindHandler(Plugin.UnattendedWardPresenceRefreshInterval, HandleWardPresenceConfigChanged);
        UnbindHandler(Plugin.DisableVanillaGuardStoneRecipe, HandleRecipeSettingsChanged);
        UnbindHandler(Plugin.StuWardRecipe, HandleRecipeSettingsChanged);
        UnbindHandler(Plugin.WardMinimapPinScale, HandleLocalWardPinConfigChanged);
        UnbindHandler(Plugin.WardMinimapActiveRanges, HandleLocalWardPinConfigChanged);
        _handlersBound = false;
    }

    private static void BindGeneral()
    {
        Plugin.ServerConfigLocked = Plugin.BindConfigEntry(
            "1 - General",
            "Lock Configuration",
            Plugin.Toggle.On,
            "If on, the configuration is locked and can be changed by server admins only."
        );
        _ = Plugin.ConfigSync.AddLockingConfigEntry(Plugin.ServerConfigLocked);

        Plugin.MaxWardsPerSteamId = Plugin.BindConfigEntry(
            "1 - General",
            "Max Wards Per Steam ID",
            3,
            "Maximum number of managed Wards allowed per Steam/platform account. Set to -1 for unlimited."
        );

        Plugin.MaxWardRadius = Plugin.BindConfigEntry(
            "1 - General",
            "Max Ward Radius",
            32f,
            "Maximum configurable Ward radius. Valid range: 8 to 64."
        );

        Plugin.PickupBlockMode = Plugin.BindConfigEntry(
            "1 - General",
            "Pickup Block Mode",
            Plugin.PickupBlockRule.BlockAllExceptWhitelist,
            "Pickup rule inside a foreign enabled ward. BlockAllExceptWhitelist blocks every item pickup except pickup_whitelist. AllowAllExceptBlacklist allows item pickup except pickup_blacklist."
        );

        Plugin.HostileCreatureStructureProtection = Plugin.BindConfigEntry(
            "2 - Unattended Protection",
            "Hostile Creature Structure Protection Mode",
            Plugin.HostileCreatureStructureProtectionMode.UnattendedOnly,
            "Controls whether building pieces inside an enabled ward ignore damage from MonsterAI-controlled attackers. Off disables this extra protection. UnattendedOnly protects while no trusted player is nearby. Always protects regardless of trusted player presence."
        );

        Plugin.UnattendedWardTrustedPlayerRangeBuffer = Plugin.BindConfigEntry(
            "2 - Unattended Protection",
            "Unattended Ward Trusted Player Range Buffer",
            16f,
            "Additional distance beyond the ward radius used when checking for nearby trusted players before hostile-creature structure protection turns off."
        );

        Plugin.UnattendedWardTrustedPresenceGraceSeconds = Plugin.BindConfigEntry(
            "2 - Unattended Protection",
            "Unattended Ward Trusted Presence Grace Seconds",
            10f,
            "How long a ward keeps counting as attended after the last nearby trusted player leaves."
        );

        Plugin.UnattendedWardPresenceRefreshInterval = Plugin.BindConfigEntry(
            "2 - Unattended Protection",
            "Unattended Ward Presence Refresh Interval",
            1f,
            "How often nearby trusted-player attendance is recalculated for unattended hostile-creature structure protection."
        );

        Plugin.DisableVanillaGuardStoneRecipe = Plugin.BindConfigEntry(
            "1 - General",
            "Disable Vanilla Guard Stone Recipe",
            Plugin.Toggle.On,
            "If on, the vanilla guard_stone build recipe is removed from the Hammer piece table while STUWard remains available."
        );

        Plugin.StuWardRecipe = Plugin.BindConfigEntry(
            "1 - General",
            "STUWard Recipe",
            "GreydwarfEye:1,BoneFragments:3,Flint:5,Wood:7",
            "STUWard recipe override. Format: ItemPrefab:Amount[:Recover], ..."
        );
    }

    private static void BindClient()
    {
        Plugin.WardSettingsShortcut = Plugin.BindConfigEntry(
            "3 - Client",
            "Ward Settings Shortcut",
            new KeyboardShortcut(KeyCode.E, KeyCode.LeftAlt),
            "Shortcut used to open the ward settings UI while looking at your ward. Example values: LeftAlt + E, F7",
            synchronizedSetting: false
        );

        Plugin.WardMinimapPinScale = Plugin.BindConfigEntry(
            "3 - Client",
            "Ward Minimap Pin Scale",
            1,
            "0 disables ward icon pins. 1 is the default icon size. 100 means x100 icon size.",
            synchronizedSetting: false
        );

        Plugin.WardMinimapActiveRanges = Plugin.BindConfigEntry(
            "3 - Client",
            "Ward Minimap Active Ranges",
            Plugin.Toggle.On,
            "If on, enabled managed wards also show their active radius on the minimap and map.",
            synchronizedSetting: false
        );

    }

    private static void BindDebug()
    {
        Plugin.WardDiagnosticLogging = Plugin.BindConfigEntry(
            "4 - Debug",
            "Ward Diagnostic Logging",
            Plugin.DiagnosticLogMode.Off,
            "Local-only scalar diagnostic logging for ward ownership/toggle flows. Use Failures for rejection paths only, or Verbose for request and state tracing. Enable separately on each client/server instance you want logs from.",
            synchronizedSetting: false
        );
    }

    private static void BindHandlers()
    {
        BindHandler(Plugin.MaxWardRadius, HandleMaxWardRadiusChanged);
        BindHandler(Plugin.MaxWardsPerSteamId, HandleMaxWardLimitChanged);
        BindHandler(Plugin.HostileCreatureStructureProtection, HandleWardPresenceConfigChanged);
        BindHandler(Plugin.UnattendedWardTrustedPlayerRangeBuffer, HandleWardPresenceConfigChanged);
        BindHandler(Plugin.UnattendedWardTrustedPresenceGraceSeconds, HandleWardPresenceConfigChanged);
        BindHandler(Plugin.UnattendedWardPresenceRefreshInterval, HandleWardPresenceConfigChanged);
        BindHandler(Plugin.DisableVanillaGuardStoneRecipe, HandleRecipeSettingsChanged);
        BindHandler(Plugin.StuWardRecipe, HandleRecipeSettingsChanged);
        BindHandler(Plugin.WardMinimapPinScale, HandleLocalWardPinConfigChanged);
        BindHandler(Plugin.WardMinimapActiveRanges, HandleLocalWardPinConfigChanged);
        _handlersBound = true;
    }

    private static void HandleMaxWardRadiusChanged(object? _, EventArgs __)
    {
        WardSettings.HandleMaxRadiusChanged();
    }

    private static void HandleMaxWardLimitChanged(object? _, EventArgs __)
    {
        WardOwnership.HandleWardLimitPolicyChanged();
    }

    private static void HandleWardPresenceConfigChanged(object? _, EventArgs __)
    {
        ManagedWardRuntimeInvalidationService.PublishPresencePolicyChanged("ward presence config changed");
    }

    private static void HandleRecipeSettingsChanged(object? _, EventArgs __)
    {
        WardPluginBootstrap.ApplyRecipeSettings();
    }

    private static void HandleLocalWardPinConfigChanged(object? _, EventArgs __)
    {
        WardMinimapPinsManager.HandleLocalConfigChanged();
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
