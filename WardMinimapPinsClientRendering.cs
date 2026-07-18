using System;
using System.Collections.Generic;
using UnityEngine;

namespace STUWard;

internal static partial class WardMinimapPinsManager
{
    private static void ApplySnapshotToMinimap(Minimap? minimap)
    {
        if (minimap == null)
        {
            return;
        }

        var showIconPins = GetWardPinScale() > 0;
        var showActiveRanges = Plugin.WardMinimapActiveRanges != null &&
                               Plugin.WardMinimapActiveRanges.Value == Plugin.Toggle.On;
        var wardIcon = StuWardPrefab.GetPieceIcon();
        var rangeIcon = showActiveRanges ? GetRepresentativeRangeSprite() : null;
        EnsureCustomPinTypes(minimap, wardIcon, rangeIcon);

        var seenWardIds = new HashSet<ZDOID>();
        foreach (var entry in LocalSnapshot.Values)
        {
            seenWardIds.Add(entry.ZdoId);
            if (showIconPins)
            {
                UpsertIconPin(minimap, entry, wardIcon);
            }
            else
            {
                RemoveTrackedPin(minimap, IconPins, entry.ZdoId);
            }

            if (showActiveRanges && entry.IsEnabled)
            {
                UpsertActiveRangePin(minimap, entry);
            }
            else
            {
                RemoveTrackedPin(minimap, ActiveRangePins, entry.ZdoId);
            }
        }

        RemoveMissingPins(minimap, IconPins, seenWardIds);
        RemoveMissingPins(minimap, ActiveRangePins, seenWardIds);
    }

    private static void UpsertIconPin(Minimap minimap, WardMinimapSnapshotEntry entry, Sprite? wardIcon)
    {
        var pinChanged = false;
        var needsNewPin = !IconPins.TryGetValue(entry.ZdoId, out var pin);
        if (!needsNewPin && !IsTrackedPinOnMinimap(minimap, pin))
        {
            needsNewPin = true;
        }

        if (needsNewPin)
        {
            pin = minimap.AddPin(entry.Position, WardIconPinType, string.Empty, false, false);
            IconPins[entry.ZdoId] = pin;
            pinChanged = true;
        }

        if (pin.m_pos != entry.Position)
        {
            pin.m_pos = entry.Position;
            pinChanged = true;
        }

        if (!pin.m_doubleSize)
        {
            pin.m_doubleSize = true;
            pinChanged = true;
        }

        var iconWorldSize = GetIconWorldSize(minimap);
        if (iconWorldSize > 0f && !Mathf.Approximately(pin.m_worldSize, iconWorldSize))
        {
            pin.m_worldSize = iconWorldSize;
            pinChanged = true;
        }

        if (wardIcon != null && pin.m_icon != wardIcon)
        {
            pin.m_icon = wardIcon;
            pinChanged = true;
        }

        if (pin.m_iconElement != null && pin.m_iconElement.sprite != pin.m_icon)
        {
            pin.m_iconElement.sprite = pin.m_icon;
            pinChanged = true;
        }

        if (pinChanged)
        {
            minimap.m_pinUpdateRequired = true;
        }
    }

    private static void UpsertActiveRangePin(Minimap minimap, WardMinimapSnapshotEntry entry)
    {
        var worldSize = Mathf.Max(0f, entry.Radius * 2f);
        var rangeSprite = WardMapRangeSprites.GetRangeSprite(entry.Radius);
        var pinChanged = false;
        var needsNewPin = !ActiveRangePins.TryGetValue(entry.ZdoId, out var pin);
        if (!needsNewPin && !IsTrackedPinOnMinimap(minimap, pin))
        {
            needsNewPin = true;
        }

        if (needsNewPin)
        {
            pin = minimap.AddPin(entry.Position, WardRangePinType, string.Empty, false, false);
            ActiveRangePins[entry.ZdoId] = pin;
            pinChanged = true;
        }
        else if (!Mathf.Approximately(pin.m_worldSize, worldSize))
        {
            // Range pins do not reliably resize in-place, so recreate them when the radius changes.
            minimap.RemovePin(pin);
            pin = minimap.AddPin(entry.Position, WardRangePinType, string.Empty, false, false);
            ActiveRangePins[entry.ZdoId] = pin;
            pinChanged = true;
        }

        if (pin.m_pos != entry.Position)
        {
            pin.m_pos = entry.Position;
            pinChanged = true;
        }

        if (!Mathf.Approximately(pin.m_worldSize, worldSize))
        {
            pin.m_worldSize = worldSize;
            pinChanged = true;
        }

        if (rangeSprite != null && pin.m_icon != rangeSprite)
        {
            pin.m_icon = rangeSprite;
            pinChanged = true;
        }

        if (pin.m_iconElement != null && pin.m_iconElement.sprite != pin.m_icon)
        {
            pin.m_iconElement.sprite = pin.m_icon;
            pinChanged = true;
        }

        if (pinChanged)
        {
            minimap.m_pinUpdateRequired = true;
        }
    }

    private static bool IsTrackedPinOnMinimap(Minimap minimap, Minimap.PinData pin)
    {
        return pin != null && minimap.m_pins != null && minimap.m_pins.Contains(pin);
    }

    private static void RemoveMissingPins(Minimap minimap, Dictionary<ZDOID, Minimap.PinData> pins, HashSet<ZDOID> seenWardIds)
    {
        List<ZDOID>? missingWardIds = null;
        foreach (var trackedPin in pins)
        {
            if (seenWardIds.Contains(trackedPin.Key))
            {
                continue;
            }

            missingWardIds ??= new List<ZDOID>();
            missingWardIds.Add(trackedPin.Key);
        }

        if (missingWardIds == null)
        {
            return;
        }

        for (var index = 0; index < missingWardIds.Count; index++)
        {
            RemoveTrackedPin(minimap, pins, missingWardIds[index]);
        }
    }

    private static void RemoveTrackedPin(Minimap minimap, Dictionary<ZDOID, Minimap.PinData> pins, ZDOID wardId)
    {
        if (!pins.Remove(wardId, out var pin))
        {
            return;
        }

        minimap.RemovePin(pin);
    }

    private static void ClearLocalPins(bool clearSnapshot)
    {
        if (_boundMinimap != null)
        {
            foreach (var pin in IconPins.Values)
            {
                _boundMinimap.RemovePin(pin);
            }

            foreach (var pin in ActiveRangePins.Values)
            {
                _boundMinimap.RemovePin(pin);
            }
        }

        IconPins.Clear();
        ActiveRangePins.Clear();
        if (!clearSnapshot)
        {
            return;
        }

        _lastViewerRevisionToken = 0;
        _pendingSnapshotRequestId = 0;
        LocalSnapshot.Clear();
        QueueRemoteSnapshotBootstrapRequest();
    }

    private static void ReplaceLocalSnapshot(IReadOnlyList<WardMinimapSnapshotEntry> snapshotEntries)
    {
        LocalSnapshot.Clear();
        UpsertLocalSnapshotEntries(snapshotEntries);
    }

    private static void UpsertLocalSnapshotEntries(IReadOnlyList<WardMinimapSnapshotEntry> snapshotEntries)
    {
        for (var index = 0; index < snapshotEntries.Count; index++)
        {
            var entry = snapshotEntries[index];
            LocalSnapshot[entry.ZdoId] = entry;
        }
    }

    private static void ApplyLocalSnapshotDelta(IReadOnlyList<WardMinimapSnapshotEntry> snapshotEntries, IReadOnlyList<ZDOID> removedWardIds)
    {
        for (var index = 0; index < removedWardIds.Count; index++)
        {
            LocalSnapshot.Remove(removedWardIds[index]);
        }

        UpsertLocalSnapshotEntries(snapshotEntries);
    }

    private static void QueueForceRefresh()
    {
        if (_pendingForceRefresh)
        {
            return;
        }

        _pendingForceRefresh = true;
    }

    private static void QueueRemoteSnapshotBootstrapRequest()
    {
        _snapshotState = ClientSnapshotState.AwaitingFullSnapshot;
    }

    private static void ClearPendingRemoteSnapshotBootstrapRequest()
    {
        _snapshotState = ClientSnapshotState.Ready;
    }

    private static void ClearPendingForceRefresh()
    {
        _pendingForceRefresh = false;
    }

    private static void EnsureCustomPinTypes(Minimap minimap, Sprite? wardIcon, Sprite? rangeIcon)
    {
        if (minimap.m_visibleIconTypes == null || minimap.m_icons == null)
        {
            return;
        }

        if (!ReferenceEquals(_customPinTypesMinimap, minimap) ||
            !IsValidPinType(minimap, WardIconPinType) ||
            !IsValidPinType(minimap, WardRangePinType) ||
            !HasSpriteData(minimap, WardIconPinType) ||
            !HasSpriteData(minimap, WardRangePinType))
        {
            WardIconPinType = AddCustomPinType(minimap, wardIcon);
            WardRangePinType = AddCustomPinType(minimap, rangeIcon ?? wardIcon);
            _customPinTypesMinimap = minimap;
            return;
        }

        UpdateCustomPinTypeSprite(minimap, WardIconPinType, wardIcon);
        UpdateCustomPinTypeSprite(minimap, WardRangePinType, rangeIcon ?? wardIcon);
    }

    private static Minimap.PinType AddCustomPinType(Minimap minimap, Sprite? icon)
    {
        var pinTypeIndex = minimap.m_visibleIconTypes.Length;
        var pinType = (Minimap.PinType)pinTypeIndex;
        ExpandVisibleIconTypes(minimap, pinTypeIndex + 1);
        minimap.m_visibleIconTypes[pinTypeIndex] = true;
        UpdateCustomPinTypeSprite(minimap, pinType, icon);
        return pinType;
    }

    private static void ExpandVisibleIconTypes(Minimap minimap, int requiredLength)
    {
        var visibleIconTypes = minimap.m_visibleIconTypes;
        if (visibleIconTypes.Length >= requiredLength)
        {
            return;
        }

        var expanded = new bool[requiredLength];
        Array.Copy(visibleIconTypes, expanded, visibleIconTypes.Length);
        for (var index = visibleIconTypes.Length; index < expanded.Length; index++)
        {
            expanded[index] = true;
        }

        minimap.m_visibleIconTypes = expanded;
    }

    private static void UpdateCustomPinTypeSprite(Minimap minimap, Minimap.PinType pinType, Sprite? icon)
    {
        var icons = minimap.m_icons;
        if (icons == null)
        {
            return;
        }

        for (var index = 0; index < icons.Count; index++)
        {
            var spriteData = icons[index];
            if (spriteData.m_name != pinType)
            {
                continue;
            }

            if (icon != null && spriteData.m_icon != icon)
            {
                spriteData.m_icon = icon;
                icons[index] = spriteData;
            }

            return;
        }

        icons.Add(new Minimap.SpriteData
        {
            m_name = pinType,
            m_icon = icon
        });
    }

    private static bool IsValidPinType(Minimap minimap, Minimap.PinType pinType)
    {
        var pinTypeIndex = (int)pinType;
        return minimap.m_visibleIconTypes != null &&
               pinTypeIndex >= 0 &&
               pinTypeIndex < minimap.m_visibleIconTypes.Length;
    }

    private static bool HasSpriteData(Minimap minimap, Minimap.PinType pinType)
    {
        var icons = minimap.m_icons;
        if (icons == null)
        {
            return false;
        }

        for (var index = 0; index < icons.Count; index++)
        {
            if (icons[index].m_name == pinType)
            {
                return true;
            }
        }

        return false;
    }

    private static Sprite? GetRepresentativeRangeSprite()
    {
        foreach (var entry in LocalSnapshot.Values)
        {
            if (entry.IsEnabled)
            {
                return WardMapRangeSprites.GetRangeSprite(entry.Radius);
            }
        }

        return null;
    }

    private static float GetIconWorldSize(Minimap minimap)
    {
        var basePinSize = minimap.m_pinSizeSmall * 2f;
        var mapImage = minimap.m_mapImageSmall != null ? minimap.m_mapImageSmall : minimap.m_mapImageLarge;
        if (basePinSize <= 0f || mapImage == null)
        {
            return 0f;
        }

        var referenceZoom = DefaultSmallMapZoom;
        var rectHeight = mapImage.rectTransform.rect.height;
        if (referenceZoom <= 0f || rectHeight <= 0f)
        {
            return 0f;
        }

        var iconScale = Mathf.Clamp(GetWardPinScale(), 0, 100);
        return basePinSize * iconScale * minimap.m_pixelSize * minimap.m_textureSize * referenceZoom / rectHeight;
    }

    private static int GetWardPinScale()
    {
        return Plugin.WardMinimapPinScale != null
            ? Plugin.WardMinimapPinScale.Value
            : 1;
    }

    private static bool IsLargeMapOpen(Minimap? minimap)
    {
        return minimap != null && minimap.m_mode == Minimap.MapMode.Large;
    }
}
