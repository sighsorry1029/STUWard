using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using YamlDotNet.Serialization;

namespace STUWard;

internal readonly struct ManagedWardConfigSnapshot
{
    internal ManagedWardConfigSnapshot(
        IReadOnlyDictionary<string, int> wardLimitOverrides,
        string itemPrefabPolicyYaml)
    {
        WardLimitOverrides = wardLimitOverrides;
        ItemPrefabPolicyYaml = itemPrefabPolicyYaml ?? string.Empty;
    }

    internal IReadOnlyDictionary<string, int> WardLimitOverrides { get; }
    internal string ItemPrefabPolicyYaml { get; }
}

internal static class ManagedWardConfigFileService
{
    // BepInEx cfg remains the home for scalar runtime settings.
    // STUWard.yml is reserved for structured server policy (lists, maps, overrides).
    internal const string ConfigFileName = "STUWard.yml";

    private const double ReloadIntervalSeconds = 1d;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    private static readonly ManagedWardConfigSnapshot DefaultSnapshot = CreateDefaultSnapshot();

    private static ManagedWardConfigSnapshot _currentSnapshot = DefaultSnapshot;
    // Tracks the last local STUWard.yml version we processed, even if parsing failed,
    // so a single malformed edit does not re-log every polling interval.
    private static DateTime _lastProcessedWriteUtc = DateTime.MinValue;
    private static DateTime _nextReloadCheckUtc = DateTime.MinValue;
    private static bool _initialized;

    internal static event Action? ConfigChanged;

    internal static ManagedWardConfigSnapshot CurrentSnapshot => _currentSnapshot;

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Plugin.ConfigSync.SourceOfTruthChanged += HandleSourceOfTruthChanged;
        ReloadAuthoritativeLocalFile(force: true);
    }

    internal static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        Plugin.ConfigSync.SourceOfTruthChanged -= HandleSourceOfTruthChanged;
        _initialized = false;
        _lastProcessedWriteUtc = DateTime.MinValue;
        _nextReloadCheckUtc = DateTime.MinValue;
        _currentSnapshot = DefaultSnapshot;
    }

    internal static void Update()
    {
        if (!_initialized || !Plugin.ConfigSync.IsSourceOfTruth || DateTime.UtcNow < _nextReloadCheckUtc)
        {
            return;
        }

        _nextReloadCheckUtc = DateTime.UtcNow.AddSeconds(ReloadIntervalSeconds);
        ReloadLocalFile(force: false);
    }

    private static void HandleSourceOfTruthChanged(bool isSourceOfTruth)
    {
        if (!isSourceOfTruth)
        {
            return;
        }

        ReloadAuthoritativeLocalFile(force: true);
    }

    private static void ReloadAuthoritativeLocalFile(bool force)
    {
        if (!Plugin.ConfigSync.IsSourceOfTruth)
        {
            return;
        }

        EnsureConfigFileExists();
        _nextReloadCheckUtc = DateTime.MinValue;
        ReloadLocalFile(force);
    }

    private static void ReloadLocalFile(bool force)
    {
        var path = GetConfigFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        DateTime lastWriteUtc;
        string yaml;
        try
        {
            lastWriteUtc = File.GetLastWriteTimeUtc(path);
            if (!force && lastWriteUtc == _lastProcessedWriteUtc)
            {
                return;
            }

            yaml = File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Failed to read managed ward config file '{path}': {exception.Message}");
            return;
        }

        if (!TryParseYaml(yaml, out var snapshot))
        {
            _lastProcessedWriteUtc = lastWriteUtc;
            return;
        }

        _lastProcessedWriteUtc = lastWriteUtc;
        _currentSnapshot = snapshot;
        ConfigChanged?.Invoke();
    }

    private static string GetConfigFilePath()
    {
        return Path.Combine(Paths.ConfigPath, ConfigFileName);
    }

    private static void EnsureConfigFileExists()
    {
        var path = GetConfigFilePath();
        if (File.Exists(path))
        {
            return;
        }

        try
        {
            File.WriteAllText(path, GetDefaultConfigFileContents());
            Plugin.Log.LogInfo($"Created managed ward config file '{path}'.");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Failed to create managed ward config file '{path}': {exception.Message}");
        }
    }

    private static bool TryParseYaml(string yaml, out ManagedWardConfigSnapshot snapshot)
    {
        snapshot = DefaultSnapshot;

        try
        {
            var data = string.IsNullOrWhiteSpace(yaml)
                ? new ManagedWardConfigYaml()
                : Deserializer.Deserialize<ManagedWardConfigYaml>(yaml) ?? new ManagedWardConfigYaml();

            var wardLimitOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
            if (data.WardLimitOverrides != null)
            {
                foreach (var entry in data.WardLimitOverrides)
                {
                    var accountId = WardOwnership.NormalizeOverrideAccountIdValue(entry.Key);
                    if (string.IsNullOrWhiteSpace(accountId) || !ulong.TryParse(accountId, out _))
                    {
                        Plugin.Log.LogWarning($"Ignoring invalid ward_limit_overrides entry for account '{entry.Key}'.");
                        continue;
                    }

                    wardLimitOverrides[accountId] = entry.Value;
                }
            }

            var itemPolicyData = data.ItemPrefabPolicy ?? new ManagedWardItemPrefabPolicyYaml();
            var itemPolicyYaml = SerializeItemPrefabPolicy(itemPolicyData);

            snapshot = new ManagedWardConfigSnapshot(wardLimitOverrides, itemPolicyYaml);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Failed to parse managed ward config YAML '{ConfigFileName}': {exception.Message}");
            return false;
        }
    }

    private static string SerializeItemPrefabPolicy(ManagedWardItemPrefabPolicyYaml data)
    {
        return Serializer.Serialize(data);
    }

    private static string GetDefaultConfigFileContents()
    {
        return
            "# Generated by STUWard\n" +
            "# Unified server config for ward limit overrides and item prefab policy.\n" +
            "#\n" +
            "# ward_limit_overrides:\n" +
            "#   Map Steam64 account ids to max ward counts.\n" +
            "#   Use -1 for unlimited wards.\n" +
            "#\n" +
            "# item_prefab_policy.blocked_item_prefabs:\n" +
            "#   Cannot be used, equipped, or attacked with inside a foreign enabled ward.\n" +
            "#\n" +
            "# item_prefab_policy.pickup_whitelist:\n" +
            "#   Allowed when Pickup Block Mode = BlockAllExceptWhitelist.\n" +
            "#\n" +
            "# item_prefab_policy.pickup_blacklist:\n" +
            "#   Blocked when Pickup Block Mode = AllowAllExceptBlacklist.\n" +
            "\n" +
            "ward_limit_overrides:\n" +
            "  \"76561198000000000\": 6\n" +
            "  \"76561198000000001\": -1\n" +
            "item_prefab_policy:\n" +
            "  blocked_item_prefabs:\n" +
            "    - kg_TameableCollector\n" +
            "    - PalStone\n" +
            "    - PalStoneSpeed\n" +
            "    - PalStoneArmour\n" +
            "    - PalStoneHeal\n" +
            "  pickup_whitelist:\n" +
            "    - Wood\n" +
            "  pickup_blacklist: []\n";
    }

    private static ManagedWardConfigSnapshot CreateDefaultSnapshot()
    {
        var itemPolicy = new ManagedWardItemPrefabPolicyYaml
        {
            BlockedItemPrefabs = new List<string>
            {
                "kg_TameableCollector",
                "PalStone",
                "PalStoneSpeed",
                "PalStoneArmour",
                "PalStoneHeal"
            },
            PickupWhitelist = new List<string>
            {
                "Wood"
            },
            PickupBlacklist = new List<string>()
        };

        return new ManagedWardConfigSnapshot(
            new Dictionary<string, int>(StringComparer.Ordinal),
            SerializeItemPrefabPolicy(itemPolicy));
    }

    private sealed class ManagedWardConfigYaml
    {
        [YamlMember(Alias = "ward_limit_overrides")]
        public Dictionary<string, int>? WardLimitOverrides { get; set; }

        [YamlMember(Alias = "item_prefab_policy")]
        public ManagedWardItemPrefabPolicyYaml? ItemPrefabPolicy { get; set; }
    }

    private sealed class ManagedWardItemPrefabPolicyYaml
    {
        [YamlMember(Alias = "blocked_item_prefabs")]
        public List<string>? BlockedItemPrefabs { get; set; }

        [YamlMember(Alias = "pickup_whitelist")]
        public List<string>? PickupWhitelist { get; set; }

        [YamlMember(Alias = "pickup_blacklist")]
        public List<string>? PickupBlacklist { get; set; }
    }
}
