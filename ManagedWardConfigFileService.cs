using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using YamlDotNet.Serialization;

namespace STUWard;

internal readonly struct ManagedWardConfigSnapshot
{
    internal ManagedWardConfigSnapshot(
        IReadOnlyDictionary<string, int> wardLimitOverrides,
        IReadOnlyDictionary<string, int> outpostWardLimitOverrides,
        string itemPrefabPolicyYaml)
    {
        WardLimitOverrides = wardLimitOverrides;
        ItemPrefabPolicyYaml = itemPrefabPolicyYaml ?? string.Empty;
        OutpostWardLimitOverrides = outpostWardLimitOverrides;
    }

    internal IReadOnlyDictionary<string, int> WardLimitOverrides { get; }
    internal string ItemPrefabPolicyYaml { get; }
    internal IReadOnlyDictionary<string, int> OutpostWardLimitOverrides { get; }

    internal int GetWardLimit(string accountId, bool outpost, int regularDefault, int outpostDefault) =>
        WardLimitPolicy.GetEffectiveLimit(accountId,
            outpost ? OutpostWardLimitOverrides : WardLimitOverrides,
            outpost ? outpostDefault : regularDefault);
}

internal static class ManagedWardConfigFileService
{
    // CFG holds scalar settings for both ward types; YAML holds maps and lists.
    internal const string ConfigFileName = "STUWard.yml";
    internal const int MaxItemPolicyYamlBytes = 256 * 1024;

    private const double ReloadIntervalSeconds = 1d;
    private const long MaxConfigFileBytes = 2L * 1024L * 1024L;
    private const int MaxWardLimitOverrideCount = 4096;
    private const int MaxItemPrefabCountPerList = 4096;
    private const int MaxPrefabNameLength = 256;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithDuplicateKeyChecking()
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
            EnsureConfigFileExists();
            if (!File.Exists(path))
            {
                return;
            }
        }

        DateTime lastWriteUtc;
        string yaml;
        try
        {
            var fileLength = new FileInfo(path).Length;
            if (fileLength > MaxConfigFileBytes)
            {
                Plugin.Log.LogWarning(
                    $"Managed ward config file '{path}' is {fileLength} bytes; the maximum is {MaxConfigFileBytes} bytes. Keeping the last valid configuration.");
                _lastProcessedWriteUtc = File.GetLastWriteTimeUtc(path);
                return;
            }

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

        if (!TryParseYaml(yaml, out var snapshot, out var error))
        {
            Plugin.Log.LogWarning($"Failed to parse managed ward config YAML '{ConfigFileName}': {error}");
            _lastProcessedWriteUtc = lastWriteUtc;
            return;
        }

        _lastProcessedWriteUtc = lastWriteUtc;
        _currentSnapshot = snapshot;
        ConfigChanged?.Invoke();
    }

    private static string GetConfigFilePath()
    {
        return Path.Combine(Plugin.DataDirectory, ConfigFileName);
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
            Plugin.EnsureDataDirectory();
            File.WriteAllText(path, GetDefaultConfigFileContents());
            Plugin.Log.LogInfo($"Created managed ward config file '{path}'.");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Failed to create managed ward config file '{path}': {exception.Message}");
        }
    }

    private static bool TryParseYaml(string yaml, out ManagedWardConfigSnapshot snapshot, out string error)
    {
        error = string.Empty;
        snapshot = DefaultSnapshot;
        yaml ??= string.Empty;

        try
        {
            if (Encoding.UTF8.GetByteCount(yaml) > MaxConfigFileBytes)
            {
                throw new FormatException($"The YAML exceeds the {MaxConfigFileBytes}-byte limit.");
            }

            var data = string.IsNullOrWhiteSpace(yaml)
                ? new ManagedWardConfigYaml()
                : Deserializer.Deserialize<ManagedWardConfigYaml>(yaml) ?? new ManagedWardConfigYaml();

            var wardLimitOverrides = ParseLimitOverrides(data.WardLimitOverrides, "ward_limit_overrides");
            var outpostWardLimitOverrides = ParseLimitOverrides(data.OutpostWardLimitOverrides, "outpost_ward_limit_overrides");

            var itemPolicyData = data.ItemPrefabPolicy ?? new ManagedWardItemPrefabPolicyYaml();
            ValidateItemPrefabPolicy(itemPolicyData);
            var itemPolicyYaml = SerializeItemPrefabPolicy(itemPolicyData);
            if (Encoding.UTF8.GetByteCount(itemPolicyYaml) > MaxItemPolicyYamlBytes)
            {
                throw new FormatException(
                    $"item_prefab_policy exceeds the {MaxItemPolicyYamlBytes}-byte synchronized payload limit.");
            }

            snapshot = new ManagedWardConfigSnapshot(wardLimitOverrides, outpostWardLimitOverrides, itemPolicyYaml);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static Dictionary<string, int> ParseLimitOverrides(Dictionary<string, int>? entries, string section)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (entries == null) return result;
        if (entries.Count > MaxWardLimitOverrideCount)
            throw new FormatException($"{section} contains {entries.Count} entries; the maximum is {MaxWardLimitOverrideCount}.");

        foreach (var entry in entries)
        {
            var accountId = WardOwnership.NormalizeAccountIdValue(entry.Key);
            if (string.IsNullOrWhiteSpace(accountId) || !ulong.TryParse(accountId, out _))
                throw new FormatException($"{section} contains invalid account id '{entry.Key}'.");
            if (entry.Value < -1)
                throw new FormatException($"{section}['{entry.Key}'] is {entry.Value}; only -1 or a non-negative ward count is allowed.");
            if (result.ContainsKey(accountId))
                throw new FormatException($"{section} contains multiple keys for normalized account id '{accountId}'.");
            result.Add(accountId, entry.Value);
        }
        return result;
    }

    private static string SerializeItemPrefabPolicy(ManagedWardItemPrefabPolicyYaml data)
    {
        return Serializer.Serialize(data);
    }

    internal static void ValidateItemPrefabPolicy(ManagedWardItemPrefabPolicyYaml data)
    {
        ValidatePrefabList(data.BlockedItemPrefabs, "blocked_item_prefabs");
        ValidatePrefabList(data.PickupWhitelist, "pickup_whitelist");
        ValidatePrefabList(data.PickupBlacklist, "pickup_blacklist");
    }

    private static void ValidatePrefabList(IReadOnlyCollection<string>? prefabNames, string fieldName)
    {
        if (prefabNames == null)
        {
            return;
        }

        if (prefabNames.Count > MaxItemPrefabCountPerList)
        {
            throw new FormatException(
                $"item_prefab_policy.{fieldName} contains {prefabNames.Count} entries; the maximum is {MaxItemPrefabCountPerList}.");
        }

        foreach (var prefabName in prefabNames)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
            {
                throw new FormatException($"item_prefab_policy.{fieldName} contains an empty prefab name.");
            }

            if (prefabName.Length > MaxPrefabNameLength)
            {
                throw new FormatException(
                    $"item_prefab_policy.{fieldName} contains a prefab name longer than {MaxPrefabNameLength} characters.");
            }
        }
    }

    private static string GetDefaultConfigFileContents()
    {
        var data = new ManagedWardConfigYaml
        {
            ItemPrefabPolicy = CreateDefaultItemPrefabPolicy()
        };
        var defaultFileSerializer = new SerializerBuilder()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        return
            "# Generated by STUWard\n" +
            "# Server config for account ward limit overrides and item prefab policy.\n" +
            "# Default limits and maximum radii for both ward types are in sighsorry.STUWard.cfg.\n" +
            "#\n" +
            "# item_prefab_policy.blocked_item_prefabs:\n" +
            "#   Cannot be used, equipped, or attacked with inside a foreign enabled ward.\n" +
            "#\n" +
            "# item_prefab_policy.pickup_whitelist:\n" +
            "#   Allowed when Pickup Block Mode = BlockAllExceptWhitelist.\n" +
            "#\n" +
            "# item_prefab_policy.pickup_blacklist:\n" +
            "#   Blocked when Pickup Block Mode = AllowAllExceptBlacklist.\n" +
            "#\n" +
            "# ward_limit_overrides / outpost_ward_limit_overrides:\n" +
            "#   Map Steam64 account ids to regular / Outpost ward limits independently.\n" +
            "#   Use -1 for unlimited wards, 0 to block new placements.\n" +
            "\n" +
            "ward_limit_overrides:\n" +
            "  # \"76561198000000000\": 6\n" +
            "  # \"76561198000000001\": -1\n" +
            "\n" +
            "outpost_ward_limit_overrides:\n" +
            "  # \"76561198000000000\": 5\n" +
            "  # \"76561198000000001\": -1\n" +
            "\n" +
            defaultFileSerializer.Serialize(data);
    }

    private static ManagedWardConfigSnapshot CreateDefaultSnapshot()
    {
        return new ManagedWardConfigSnapshot(
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal),
            SerializeItemPrefabPolicy(CreateDefaultItemPrefabPolicy()));
    }

    private static ManagedWardItemPrefabPolicyYaml CreateDefaultItemPrefabPolicy()
    {
        return new ManagedWardItemPrefabPolicyYaml
        {
            BlockedItemPrefabs = new List<string>
            {
                "kg_TameableCollector",
                "PalStone",
                "PalStoneSpeed",
                "PalStoneArmour",
                "PalStoneHeal",
                "FLG_TamingOrb"
            },
            PickupWhitelist = new List<string>
            {
                "Wood"
            },
            PickupBlacklist = new List<string>()
        };
    }

    private sealed class ManagedWardConfigYaml
    {
        [YamlMember(Alias = "outpost_ward_limit_overrides")]
        public Dictionary<string, int>? OutpostWardLimitOverrides { get; set; }

        [YamlMember(Alias = "ward_limit_overrides")]
        public Dictionary<string, int>? WardLimitOverrides { get; set; }

        [YamlMember(Alias = "item_prefab_policy")]
        public ManagedWardItemPrefabPolicyYaml? ItemPrefabPolicy { get; set; }
    }

    internal sealed class ManagedWardItemPrefabPolicyYaml
    {
        [YamlMember(Alias = "blocked_item_prefabs")]
        public List<string>? BlockedItemPrefabs { get; set; }

        [YamlMember(Alias = "pickup_whitelist")]
        public List<string>? PickupWhitelist { get; set; }

        [YamlMember(Alias = "pickup_blacklist")]
        public List<string>? PickupBlacklist { get; set; }
    }
}
