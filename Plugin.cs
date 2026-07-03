using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace STUWard;

internal sealed class ConfigurationManagerAttributes
{
    public int? Order;
}

[BepInPlugin(ModGuid, ModName, ModVersion)]
[BepInDependency("com.jotunn.jotunn")]
[BepInDependency("org.bepinex.plugins.guilds", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const string ModName = "STUWard";
    internal const string ModVersion = "1.2.1";
    internal const string Author = "sighsorry";
    internal const string ModGuid = $"{Author}.{ModName}";

    internal static readonly ConfigSync ConfigSync = new(ModGuid)
    {
        DisplayName = ModName,
        CurrentVersion = ModVersion,
        MinimumRequiredVersion = ModVersion
    };

    private Harmony _harmony = null!;

    internal static ManualLogSource Log = null!;
    internal static Plugin Instance = null!;
    internal static WardGuiController WardGui = null!;

    internal static ConfigEntry<Toggle> ServerConfigLocked = null!;
    internal static ConfigEntry<int> MaxWardsPerSteamId = null!;
    internal static ConfigEntry<float> MaxWardRadius = null!;
    internal static ConfigEntry<PickupBlockRule> PickupBlockMode = null!;
    internal static ConfigEntry<HostileCreatureStructureProtectionMode> HostileCreatureStructureProtection = null!;
    internal static ConfigEntry<RestrictionServerMode> DoorsRestriction = null!;
    internal static ConfigEntry<RestrictionServerMode> PortalsRestriction = null!;
    internal static ConfigEntry<RestrictionServerMode> PickupRestriction = null!;
    internal static ConfigEntry<RestrictionServerMode> PlacedConsumablesRestriction = null!;
    internal static ConfigEntry<RestrictionServerMode> ItemStandsRestriction = null!;
    internal static ConfigEntry<RestrictionServerMode> ArmorStandsRestriction = null!;
    internal static ConfigEntry<RestrictionServerMode> ContainersRestriction = null!;
    internal static ConfigEntry<RestrictionServerMode> CraftingStationsRestriction = null!;
    internal static ConfigEntry<RestrictionServerMode> TameablesAndSaddlesRestriction = null!;
    internal static ConfigEntry<Toggle> DisableVanillaGuardStoneRecipe = null!;
    internal static ConfigEntry<string> StuWardRecipe = null!;
    internal static ConfigEntry<KeyboardShortcut> WardSettingsShortcut = null!;
    internal static ConfigEntry<int> WardMinimapPinScale = null!;
    internal static ConfigEntry<Toggle> WardMinimapActiveRanges = null!;
    internal static ConfigEntry<DiagnosticLogMode> WardDiagnosticLogging = null!;

    internal enum Toggle
    {
        Off = 0,
        On = 1
    }

    internal enum PickupBlockRule
    {
        BlockAllExceptWhitelist = 0,
        AllowAllExceptBlacklist = 1
    }

    internal enum HostileCreatureStructureProtectionMode
    {
        Off = 0,
        UnattendedOnly = 1,
        Always = 2
    }

    internal enum RestrictionServerMode
    {
        NotForced = 0,
        ForcedOn = 1
    }

    internal enum DiagnosticLogMode
    {
        Off = 0,
        Failures = 1,
        Verbose = 2
    }

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        WardPluginBootstrap.InitializeCore();

        var saveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            WardPluginConfigBindings.BindAll();
            WardPluginBootstrap.InitializeFeatures();

            _harmony = new Harmony(ModGuid);
            WardPatchRegistry.ApplyAll(_harmony);
            WardGui = CreateOrReuseWardGuiController();

            Config.Save();
        }
        finally
        {
            Config.SaveOnConfigSet = saveOnSet;
        }
    }

    private void Update()
    {
        WardPluginBootstrap.Update();
    }

    private void OnDestroy()
    {
        WardPluginBootstrap.Shutdown();
        _harmony?.UnpatchSelf();
        Config.Save();
    }

    internal static bool IsBlockedItem(string prefabName)
    {
        return WardItemPrefabPolicy.IsBlockedItem(prefabName);
    }

    internal static bool HasBlockedItems()
    {
        return WardItemPrefabPolicy.HasBlockedItems();
    }

    internal static bool IsWardSettingsShortcutDown()
    {
        return WardSettingsShortcut != null &&
               WardSettingsShortcut.Value.MainKey != KeyCode.None &&
               WardSettingsShortcut.Value.IsDown();
    }

    internal static bool HasWardSettingsShortcutBinding()
    {
        return WardSettingsShortcut != null && WardSettingsShortcut.Value.MainKey != KeyCode.None;
    }

    internal static string GetWardSettingsShortcutLabel()
    {
        if (WardSettingsShortcut == null || WardSettingsShortcut.Value.MainKey == KeyCode.None)
        {
            return WardLocalization.Localize(WardLocalization.ShortcutUnboundToken, WardLocalization.ShortcutUnboundFallback);
        }

        var shortcut = WardSettingsShortcut.Value;
        var parts = new List<string>();

        AddModifierLabel(parts, shortcut.Modifiers, KeyCode.LeftControl, KeyCode.RightControl, "Ctrl");
        AddModifierLabel(parts, shortcut.Modifiers, KeyCode.LeftAlt, KeyCode.RightAlt, "Alt");
        AddModifierLabel(parts, shortcut.Modifiers, KeyCode.LeftShift, KeyCode.RightShift, "Shift");

        foreach (var modifier in shortcut.Modifiers)
        {
            if (modifier is KeyCode.LeftControl or KeyCode.RightControl or KeyCode.LeftAlt or KeyCode.RightAlt or KeyCode.LeftShift or KeyCode.RightShift)
            {
                continue;
            }

            parts.Add(GetKeyLabel(modifier));
        }

        parts.Add(GetKeyLabel(shortcut.MainKey));
        return string.Join("+", parts);
    }

    private static void AddModifierLabel(List<string> parts, IEnumerable<KeyCode> modifiers, KeyCode left, KeyCode right, string label)
    {
        foreach (var modifier in modifiers)
        {
            if (modifier == left || modifier == right)
            {
                parts.Add(label);
                return;
            }
        }
    }

    private static string GetKeyLabel(KeyCode keyCode)
    {
        return keyCode switch
        {
            KeyCode.LeftAlt or KeyCode.RightAlt => "Alt",
            KeyCode.LeftControl or KeyCode.RightControl => "Ctrl",
            KeyCode.LeftShift or KeyCode.RightShift => "Shift",
            KeyCode.Alpha0 => "0",
            KeyCode.Alpha1 => "1",
            KeyCode.Alpha2 => "2",
            KeyCode.Alpha3 => "3",
            KeyCode.Alpha4 => "4",
            KeyCode.Alpha5 => "5",
            KeyCode.Alpha6 => "6",
            KeyCode.Alpha7 => "7",
            KeyCode.Alpha8 => "8",
            KeyCode.Alpha9 => "9",
            _ => keyCode.ToString()
        };
    }

    internal static ConfigEntry<T> BindConfigEntry<T>(
        string group,
        string name,
        T value,
        string description,
        bool synchronizedSetting = true,
        int? configManagerOrder = null)
    {
        return Instance.BindConfig(group, name, value, description, synchronizedSetting, configManagerOrder);
    }

    internal static bool ShouldLogWardDiagnosticFailures()
    {
        return WardDiagnosticLogging != null && WardDiagnosticLogging.Value != DiagnosticLogMode.Off;
    }

    internal static bool ShouldLogWardDiagnosticVerbose()
    {
        return WardDiagnosticLogging != null && WardDiagnosticLogging.Value == DiagnosticLogMode.Verbose;
    }

    internal static void LogWardDiagnosticFailure(string context, string message)
    {
        if (!ShouldLogWardDiagnosticFailures() || Log == null)
        {
            return;
        }

        Log.LogWarning($"[WardDiag:{context}] {message}");
    }

    internal static void LogWardDiagnosticVerbose(string context, string message)
    {
        if (!ShouldLogWardDiagnosticVerbose() || Log == null)
        {
            return;
        }

        Log.LogInfo($"[WardDiag:{context}] {message}");
    }

    private static WardGuiController CreateOrReuseWardGuiController()
    {
        if (WardGuiController.Instance != null)
        {
            return WardGuiController.Instance;
        }

        var guiRoot = new GameObject($"{ModName}.WardGui");
        DontDestroyOnLoad(guiRoot);
        return guiRoot.AddComponent<WardGuiController>();
    }

    private ConfigEntry<T> BindConfig<T>(
        string group,
        string name,
        T value,
        string description,
        bool synchronizedSetting = true,
        int? configManagerOrder = null)
    {
        var syncDescription = synchronizedSetting ? "Synced with server." : "Not synced with server.";
        var combinedDescription = string.IsNullOrWhiteSpace(description)
            ? syncDescription
            : $"{description.TrimEnd()} {syncDescription}";
        var configDescription = configManagerOrder.HasValue
            ? new ConfigDescription(combinedDescription, null, new ConfigurationManagerAttributes { Order = configManagerOrder.Value })
            : new ConfigDescription(combinedDescription);
        var configEntry = Config.Bind(group, name, value, configDescription);
        if (synchronizedSetting)
        {
            var syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = true;
        }

        return configEntry;
    }
}
