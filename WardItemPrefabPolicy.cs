using System;
using System.Collections.Generic;
using ServerSync;
using UnityEngine;
using YamlDotNet.Serialization;

namespace STUWard;

internal static class WardItemPrefabPolicy
{
    private static readonly CustomSyncedValue<string> ItemPrefabData = new(Plugin.ConfigSync, "itemPrefabData", string.Empty);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private static HashSet<string> _blockedItemPrefabNames = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _pickupWhitelistPrefabNames = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _pickupBlacklistPrefabNames = new(StringComparer.OrdinalIgnoreCase);

    private static bool _initialized;

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        ItemPrefabData.ValueChanged += ApplySyncedYaml;
        ManagedWardConfigFileService.ConfigChanged += HandleManagedWardConfigChanged;
        ApplyAuthoritativeConfigFromService();
    }

    internal static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        ItemPrefabData.ValueChanged -= ApplySyncedYaml;
        ManagedWardConfigFileService.ConfigChanged -= HandleManagedWardConfigChanged;
        _initialized = false;
    }

    internal static bool IsBlockedItem(string prefabName)
    {
        prefabName = NormalizePrefabName(prefabName);
        return !string.IsNullOrWhiteSpace(prefabName) && _blockedItemPrefabNames.Contains(prefabName);
    }

    internal static bool IsBlockedItem(ItemDrop.ItemData? item)
    {
        return IsBlockedItem(GetItemPrefabName(item));
    }

    internal static bool HasBlockedItems()
    {
        return _blockedItemPrefabNames.Count > 0;
    }

    internal static bool CanAnyPickupBeBlocked()
    {
        return Plugin.PickupBlockMode.Value switch
        {
            Plugin.PickupBlockRule.AllowAllExceptBlacklist => _pickupBlacklistPrefabNames.Count > 0,
            _ => true
        };
    }

    internal static bool ShouldBlockPickup(ItemDrop? itemDrop)
    {
        var prefabName = GetItemPrefabName(itemDrop);
        return ShouldBlockPickup(prefabName);
    }

    internal static bool ShouldBlockPickup(GameObject? go)
    {
        var prefabName = GetItemPrefabName(go);
        return ShouldBlockPickup(prefabName);
    }

    internal static string GetItemPrefabName(ItemDrop.ItemData? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        return NormalizePrefabName(item.m_dropPrefab != null ? item.m_dropPrefab.name : string.Empty);
    }

    private static bool ShouldBlockPickup(string prefabName)
    {
        prefabName = NormalizePrefabName(prefabName);

        return Plugin.PickupBlockMode.Value switch
        {
            Plugin.PickupBlockRule.AllowAllExceptBlacklist => !string.IsNullOrWhiteSpace(prefabName) &&
                                                               _pickupBlacklistPrefabNames.Contains(prefabName),
            _ => string.IsNullOrWhiteSpace(prefabName) || !_pickupWhitelistPrefabNames.Contains(prefabName)
        };
    }

    private static string GetItemPrefabName(ItemDrop? itemDrop)
    {
        if (itemDrop == null)
        {
            return string.Empty;
        }

        var prefabName = GetItemPrefabName(itemDrop.m_itemData);
        return !string.IsNullOrWhiteSpace(prefabName)
            ? prefabName
            : NormalizePrefabName(itemDrop.name);
    }

    private static string GetItemPrefabName(GameObject? go)
    {
        if (go == null)
        {
            return string.Empty;
        }

        var itemDrop = go.GetComponent<ItemDrop>() ?? go.GetComponentInParent<ItemDrop>();
        var prefabName = GetItemPrefabName(itemDrop);
        return !string.IsNullOrWhiteSpace(prefabName)
            ? prefabName
            : NormalizePrefabName(go.name);
    }

    internal static string NormalizePrefabName(string prefabName)
    {
        prefabName = prefabName.Trim();
        if (prefabName.EndsWith("(Clone)", StringComparison.Ordinal))
        {
            prefabName = prefabName.Substring(0, prefabName.Length - "(Clone)".Length).Trim();
        }

        return prefabName;
    }

    private static void HandleManagedWardConfigChanged()
    {
        ApplyAuthoritativeConfigFromService();
    }

    private static void ApplyAuthoritativeConfigFromService()
    {
        if (!_initialized || !Plugin.ConfigSync.IsSourceOfTruth)
        {
            return;
        }

        ItemPrefabData.AssignLocalValue(ManagedWardConfigFileService.CurrentSnapshot.ItemPrefabPolicyYaml);
    }

    private static void ApplySyncedYaml()
    {
        if (!TryParseYaml(
                ItemPrefabData.Value,
                out var blockedItemPrefabNames,
                out var pickupWhitelistPrefabNames,
                out var pickupBlacklistPrefabNames))
        {
            return;
        }

        _blockedItemPrefabNames = blockedItemPrefabNames;
        _pickupWhitelistPrefabNames = pickupWhitelistPrefabNames;
        _pickupBlacklistPrefabNames = pickupBlacklistPrefabNames;
        Plugin.Log.LogInfo(
            $"Applied item prefab policy: blocked_item_prefabs={_blockedItemPrefabNames.Count}, pickup_whitelist={_pickupWhitelistPrefabNames.Count}, pickup_blacklist={_pickupBlacklistPrefabNames.Count}");
    }

    private static bool TryParseYaml(
        string yaml,
        out HashSet<string> blockedItemPrefabNames,
        out HashSet<string> pickupWhitelistPrefabNames,
        out HashSet<string> pickupBlacklistPrefabNames)
    {
        blockedItemPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pickupWhitelistPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pickupBlacklistPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var data = string.IsNullOrWhiteSpace(yaml)
                ? new ItemPrefabYaml()
                : Deserializer.Deserialize<ItemPrefabYaml>(yaml) ?? new ItemPrefabYaml();

            AddEntries(blockedItemPrefabNames, data.BlockedItemPrefabs);
            AddEntries(pickupWhitelistPrefabNames, data.PickupWhitelist);
            AddEntries(pickupBlacklistPrefabNames, data.PickupBlacklist);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning($"Failed to parse item prefab policy YAML '{ManagedWardConfigFileService.ConfigFileName}:item_prefab_policy': {exception.Message}");
            return false;
        }
    }

    private static void AddEntries(HashSet<string> result, IEnumerable<string>? prefabNames)
    {
        if (prefabNames == null)
        {
            return;
        }

        foreach (var prefabName in prefabNames)
        {
            var normalized = NormalizePrefabName(prefabName);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                result.Add(normalized);
            }
        }
    }

    private sealed class ItemPrefabYaml
    {
        [YamlMember(Alias = "blocked_item_prefabs")]
        public List<string>? BlockedItemPrefabs { get; set; }

        [YamlMember(Alias = "pickup_whitelist")]
        public List<string>? PickupWhitelist { get; set; }

        [YamlMember(Alias = "pickup_blacklist")]
        public List<string>? PickupBlacklist { get; set; }
    }
}
