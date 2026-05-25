using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using LocalizationManager;
using System;
using UnityEngine;
using System.Collections.Generic;
using HarmonyLib;

namespace STUWard;

[DisallowMultipleComponent]
internal sealed class StuWardArea : MonoBehaviour
{
    internal const string PrefabName = "piece_stuward";
    internal const string BasePrefabName = "guard_stone";
    internal const string DisplayName = WardLocalization.PieceNameToken;
    internal const string Description = WardLocalization.PieceDescriptionToken;

    internal static bool IsManaged(PrivateArea? area)
    {
        return area != null && area.GetComponent<StuWardArea>() != null;
    }
}

internal static class ManagedWardIdentity
{
    internal static bool IsManaged(PrivateArea? area)
    {
        return TryResolve(ManagedWardRef.FromArea(area), repairComponent: false, out _, out _);
    }

    internal static bool EnsureManagedComponent(PrivateArea? area)
    {
        return EnsureManagedComponent(ManagedWardRef.FromArea(area));
    }

    internal static bool EnsureManagedComponent(PrivateArea? area, ZDO? zdo)
    {
        return EnsureManagedComponent(ManagedWardRef.FromArea(area, zdo));
    }

    internal static bool EnsureManagedComponent(ManagedWardRef ward)
    {
        return TryResolve(ward, repairComponent: true, out var matchedByComponent, out _) && matchedByComponent;
    }

    internal static bool TryResolve(
        PrivateArea? area,
        ZDO? zdo,
        bool repairComponent,
        out bool matchedByComponent,
        out bool matchedByZdo)
    {
        return TryResolve(ManagedWardRef.FromArea(area, zdo), repairComponent, out matchedByComponent, out matchedByZdo);
    }

    internal static bool TryResolve(
        ManagedWardRef ward,
        bool repairComponent,
        out bool matchedByComponent,
        out bool matchedByZdo)
    {
        matchedByComponent = ward.HasManagedComponent;
        matchedByZdo = ward.IsManagedZdo;
        if (!ward.HasArea)
        {
            return matchedByZdo;
        }

        if (!matchedByComponent && matchedByZdo && repairComponent)
        {
            var repaired = ward.EnsureManagedComponent(out var added);
            matchedByComponent = repaired.HasManagedComponent;
            if (added)
            {
                Plugin.LogWardDiagnosticVerbose(
                    "Placement.Identity",
                    $"Restored missing StuWardArea component from managed ward ZDO identity. {WardDiagnosticInfo.DescribeWard(repaired.Area)}");
            }
        }

        return matchedByComponent || matchedByZdo;
    }
}

internal sealed class StuWardPlacedHook : MonoBehaviour, IPlaced
{
    public void OnPlaced()
    {
        var area = GetComponent<PrivateArea>();
        var ward = ManagedWardRef.FromArea(area);
        if (!ManagedWardIdentity.EnsureManagedComponent(ward))
        {
            return;
        }

        WardOwnership.TryStampLocalManagedWardOwnerAccount(ward);
        WardOwnership.NotifyServerManagedWardPlaced(ward);
        ManagedWardMapStateService.NotifyLiveWardMutation(area, ManagedWardMapMutationKind.IndexAndPins, "local managed ward placed");
        Plugin.LogWardDiagnosticVerbose(
            "Placement.OnPlaced",
            $"IPlaced.OnPlaced hit for managed ward. {WardDiagnosticInfo.DescribeWard(area)}");
    }
}

internal static class StuWardPrefab
{
    private static bool _registered;
    private static GameObject? _stuWardPrefab;
    private static GameObject? _vanillaGuardStonePrefab;
    private static int _vanillaGuardStoneIndex = -1;
    private static Piece.Requirement[]? _defaultStuWardRequirements;
    private static string? _lastLoggedPieceIconState;

    internal static void Register()
    {
        if (_registered || PieceManager.Instance.GetPiece(StuWardArea.PrefabName) != null)
        {
            _registered = true;
            return;
        }

        if (PrefabManager.Instance.GetPrefab(StuWardArea.BasePrefabName) == null)
        {
            return;
        }

        var pieceConfig = new PieceConfig
        {
            PieceTable = "Hammer",
            Name = StuWardArea.DisplayName,
            Description = StuWardArea.Description
        };

        var customPiece = new CustomPiece(StuWardArea.PrefabName, StuWardArea.BasePrefabName, pieceConfig);
        var prefab = customPiece.PiecePrefab;
        var piece = customPiece.Piece;
        var area = prefab != null ? prefab.GetComponent<PrivateArea>() : null;

        if (prefab == null || piece == null || area == null)
        {
            Plugin.Log.LogWarning("Failed to create STUWard clone prefab from guard_stone.");
            return;
        }

        if (prefab.GetComponent<StuWardArea>() == null)
        {
            prefab.AddComponent<StuWardArea>();
        }

        if (prefab.GetComponent<StuWardPlacedHook>() == null)
        {
            prefab.AddComponent<StuWardPlacedHook>();
        }

        piece.m_name = StuWardArea.DisplayName;
        piece.m_description = StuWardArea.Description;
        piece.m_resources = CloneRequirements(piece.m_resources);
        area.m_name = StuWardArea.DisplayName;
        area.m_radius = WardSettings.MinRadius;
        if (area.m_areaMarker != null)
        {
            area.m_areaMarker.m_radius = WardSettings.MinRadius;
        }

        _stuWardPrefab = prefab;
        _defaultStuWardRequirements = CloneRequirements(piece.m_resources);

        PieceManager.Instance.AddPiece(customPiece);
        _registered = PieceManager.Instance.GetPiece(StuWardArea.PrefabName) != null;

        if (_registered)
        {
            Plugin.Log.LogInfo("Registered STUWard clone piece.");
        }
    }

    internal static void ApplyRecipeSettings()
    {
        ApplyVanillaGuardStoneRecipeSetting();
        ApplyStuWardRecipeSetting();
    }

    internal static Sprite? GetPieceIcon()
    {
        var piece = GetStuWardPiece();
        if (piece != null && piece.m_icon != null)
        {
            return LogPieceIconResolution(piece.m_icon, $"stuWardPrefab piece icon '{piece.m_icon.name}'");
        }

        var registeredPiece = PieceManager.Instance.GetPiece(StuWardArea.PrefabName);
        if (registeredPiece?.Piece != null && registeredPiece.Piece.m_icon != null)
        {
            return LogPieceIconResolution(
                registeredPiece.Piece.m_icon,
                $"registered piece icon '{registeredPiece.Piece.m_icon.name}'");
        }

        var prefab = registeredPiece?.PiecePrefab ??
                     PrefabManager.Instance.GetPrefab(StuWardArea.PrefabName) ??
                     PrefabManager.Instance.GetPrefab(StuWardArea.BasePrefabName);
        var prefabIcon = prefab != null ? prefab.GetComponent<Piece>()?.m_icon : null;
        if (prefabIcon != null)
        {
            var prefabName = prefab != null ? prefab.name : "null";
            return LogPieceIconResolution(prefabIcon, $"prefab '{prefabName}' piece icon '{prefabIcon.name}'");
        }

        return LogMissingPieceIcon(
            $"stuWardPrefabPresent={_stuWardPrefab != null}, registeredPiecePresent={registeredPiece?.Piece != null}, registeredPiecePrefabPresent={registeredPiece?.PiecePrefab != null}, fallbackPrefab='{prefab?.name ?? "null"}'");
    }

    internal static Piece.Requirement[] GetCurrentStuWardRequirements()
    {
        return CloneRequirements(GetStuWardPiece()?.m_resources);
    }

    private static Piece? GetStuWardPiece()
    {
        var piece = _stuWardPrefab != null ? _stuWardPrefab.GetComponent<Piece>() : null;
        if (piece != null)
        {
            return piece;
        }

        return PieceManager.Instance.GetPiece(StuWardArea.PrefabName)?.Piece;
    }

    private static Sprite LogPieceIconResolution(Sprite icon, string source)
    {
        var state = $"resolved:{source}";
        if (_lastLoggedPieceIconState != state)
        {
            _lastLoggedPieceIconState = state;
            Plugin.LogWardDiagnosticVerbose("WardPins.Icon", $"Resolved piece_stuward icon from {source}.");
        }

        return icon;
    }

    private static Sprite? LogMissingPieceIcon(string context)
    {
        var state = $"missing:{context}";
        if (_lastLoggedPieceIconState != state)
        {
            _lastLoggedPieceIconState = state;
            Plugin.LogWardDiagnosticFailure("WardPins.Icon", $"Failed to resolve piece_stuward icon. {context}");
        }

        return null;
    }

    internal static void ApplyVanillaGuardStoneRecipeSetting()
    {
        var pieceTable = GetHammerPieceTable();
        var pieces = pieceTable?.m_pieces;
        var guardStonePrefab = PrefabManager.Instance.GetPrefab(StuWardArea.BasePrefabName);
        if (pieceTable == null || pieces == null || guardStonePrefab == null)
        {
            return;
        }

        _vanillaGuardStonePrefab ??= guardStonePrefab;

        var matchingIndexes = GetMatchingGuardStoneIndexes(pieces, guardStonePrefab);
        if (_vanillaGuardStoneIndex < 0 && matchingIndexes.Count > 0)
        {
            _vanillaGuardStoneIndex = matchingIndexes[0];
        }

        if (Plugin.DisableVanillaGuardStoneRecipe != null && Plugin.DisableVanillaGuardStoneRecipe.Value == Plugin.Toggle.On)
        {
            for (var index = matchingIndexes.Count - 1; index >= 0; index--)
            {
                pieces.RemoveAt(matchingIndexes[index]);
            }
        }
        else if (matchingIndexes.Count == 0 && _vanillaGuardStonePrefab != null)
        {
            var insertIndex = _vanillaGuardStoneIndex >= 0
                ? Mathf.Clamp(_vanillaGuardStoneIndex, 0, pieces.Count)
                : pieces.Count;
            pieces.Insert(insertIndex, _vanillaGuardStonePrefab);
        }

        Player.m_localPlayer?.UpdateAvailablePiecesList();
    }

    private static void ApplyStuWardRecipeSetting()
    {
        var piece = _stuWardPrefab != null ? _stuWardPrefab.GetComponent<Piece>() : null;
        if (piece == null)
        {
            return;
        }

        var recipeOverride = Plugin.StuWardRecipe?.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(recipeOverride))
        {
            if (_defaultStuWardRequirements != null)
            {
                piece.m_resources = CloneRequirements(_defaultStuWardRequirements);
                Player.m_localPlayer?.UpdateAvailablePiecesList();
            }

            return;
        }

        if (!TryParseRequirements(recipeOverride, out var requirements))
        {
            Plugin.Log.LogWarning($"Invalid STUWard recipe override '{recipeOverride}'. Keeping previous recipe.");
            return;
        }

        piece.m_resources = requirements;
        Player.m_localPlayer?.UpdateAvailablePiecesList();
    }

    private static PieceTable? GetHammerPieceTable()
    {
        var hammerPrefab = PrefabManager.Instance.GetPrefab("Hammer");
        var itemDrop = hammerPrefab != null ? hammerPrefab.GetComponent<ItemDrop>() : null;
        return itemDrop?.m_itemData?.m_shared?.m_buildPieces;
    }

    private static List<int> GetMatchingGuardStoneIndexes(List<GameObject> pieces, GameObject guardStonePrefab)
    {
        var matchingIndexes = new List<int>();
        for (var index = 0; index < pieces.Count; index++)
        {
            var piece = pieces[index];
            if (piece == null)
            {
                continue;
            }

            if (piece == guardStonePrefab || piece.name == StuWardArea.BasePrefabName)
            {
                matchingIndexes.Add(index);
            }
        }

        return matchingIndexes;
    }

    private static Piece.Requirement[] CloneRequirements(Piece.Requirement[]? source)
    {
        if (source == null || source.Length == 0)
        {
            return Array.Empty<Piece.Requirement>();
        }

        var clone = new Piece.Requirement[source.Length];
        for (var index = 0; index < source.Length; index++)
        {
            var requirement = source[index];
            clone[index] = new Piece.Requirement
            {
                m_resItem = requirement.m_resItem,
                m_amount = requirement.m_amount,
                m_extraAmountOnlyOneIngredient = requirement.m_extraAmountOnlyOneIngredient,
                m_amountPerLevel = requirement.m_amountPerLevel,
                m_recover = requirement.m_recover
            };
        }

        return clone;
    }

    private static bool TryParseRequirements(string value, out Piece.Requirement[] requirements)
    {
        requirements = Array.Empty<Piece.Requirement>();
        var parts = value.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var parsedRequirements = new List<Piece.Requirement>(parts.Length);
        foreach (var part in parts)
        {
            var tokens = part.Split(':');
            if (tokens.Length is < 2 or > 3)
            {
                return false;
            }

            var prefabName = tokens[0].Trim();
            if (string.IsNullOrWhiteSpace(prefabName) || !int.TryParse(tokens[1], out var amount) || amount <= 0)
            {
                return false;
            }

            var itemPrefab = ResolveItemPrefab(prefabName);
            var itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            if (itemDrop == null)
            {
                Plugin.Log.LogWarning($"Unable to resolve STUWard recipe item prefab '{prefabName}'.");
                return false;
            }

            var recover = true;
            if (tokens.Length == 3 && !TryParseBool(tokens[2], out recover))
            {
                return false;
            }

            parsedRequirements.Add(new Piece.Requirement
            {
                m_resItem = itemDrop,
                m_amount = amount,
                m_amountPerLevel = 1,
                m_recover = recover
            });
        }

        requirements = parsedRequirements.ToArray();
        return true;
    }

    private static GameObject? ResolveItemPrefab(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return null;
        }

        var itemPrefab = ObjectDB.instance?.GetItemPrefab(prefabName);
        if (itemPrefab != null)
        {
            return itemPrefab;
        }

        return PrefabManager.Instance.GetPrefab(prefabName);
    }

    private static bool TryParseBool(string value, out bool result)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "on":
                result = true;
                return true;
            case "0":
            case "false":
            case "no":
            case "off":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
internal static class ObjectDBAwakePatch
{
    private static void Postfix()
    {
        Localizer.ReloadCurrentLanguageIfAvailable();
        StuWardPrefab.ApplyRecipeSettings();
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
internal static class ObjectDBCopyOtherDbPatch
{
    private static void Postfix()
    {
        Localizer.ReloadCurrentLanguageIfAvailable();
        StuWardPrefab.ApplyRecipeSettings();
    }
}
