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
    internal static bool EnsureManagedComponent(PrivateArea? area)
    {
        return EnsureManagedComponent(ManagedWardRef.FromArea(area));
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
            var repaired = ward.EnsureManagedComponent(out _);
            matchedByComponent = repaired.HasManagedComponent;
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
        ManagedWardMapStateService.NotifyWardMutation(area);
    }
}

internal static class StuWardPrefab
{
    private static GameObject? _prefabRoot;
    private static PieceTable? _hammerTable;
    private static GameObject? _stuWardPrefab;
    private static GameObject? _vanillaGuardStonePrefab;
    private static int _vanillaGuardStoneIndex = -1;
    private static Piece.Requirement[]? _defaultStuWardRequirements;
    // Called before ZNetScene builds its name/hash lookup. Inactive parent prevents
    // Awake from allocating a ZDO or destroying ZNetView on the prefab clone.
    internal static void Register(ZNetScene scene)
    {
        var existing = scene.m_prefabs.Find(prefab => prefab != null && prefab.name == StuWardArea.PrefabName);
        if (existing != null && existing != _stuWardPrefab)
            throw new InvalidOperationException("Another prefab is already registered as " + StuWardArea.PrefabName);

        if (_stuWardPrefab != null)
        {
            if (existing == null) scene.m_prefabs.Add(_stuWardPrefab);
            ApplyRecipeSettings();
            return;
        }

        var source = scene.m_prefabs.Find(prefab => prefab != null && prefab.name == StuWardArea.BasePrefabName);
        if (source == null) throw new InvalidOperationException("Valheim guard_stone prefab is unavailable.");
        _prefabRoot = new GameObject("STUWard Prefabs");
        _prefabRoot.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(_prefabRoot);
        var prefab = UnityEngine.Object.Instantiate(source, _prefabRoot.transform, false);
        prefab.name = StuWardArea.PrefabName;
        prefab.SetActive(true);
        var piece = prefab.GetComponent<Piece>();
        var area = prefab.GetComponent<PrivateArea>();
        if (piece == null || area == null)
        {
            Shutdown();
            throw new InvalidOperationException("guard_stone is missing Piece or PrivateArea.");
        }
        prefab.AddComponent<StuWardArea>();
        prefab.AddComponent<StuWardPlacedHook>();

        // Preserve the defaults formerly applied by PieceConfig.
        piece.m_enabled = true;
        piece.m_allowedInDungeons = false;
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

        scene.m_prefabs.Add(prefab);
        ApplyRecipeSettings();
        Plugin.Log.LogInfo("Registered STUWard prefab without Jotunn.");
    }

    internal static void Shutdown()
    {
        if (_hammerTable != null)
        {
            _hammerTable.m_pieces.Remove(_stuWardPrefab);
            if (_vanillaGuardStonePrefab != null && !_hammerTable.m_pieces.Contains(_vanillaGuardStonePrefab))
                _hammerTable.m_pieces.Insert(Mathf.Clamp(_vanillaGuardStoneIndex, 0, _hammerTable.m_pieces.Count), _vanillaGuardStonePrefab);
        }
        _hammerTable = null;
        _stuWardPrefab = null;
        _vanillaGuardStonePrefab = null;
        _vanillaGuardStoneIndex = -1;
        _defaultStuWardRequirements = null;
        if (_prefabRoot != null) UnityEngine.Object.Destroy(_prefabRoot);
        _prefabRoot = null;
    }

    internal static void ApplyRecipeSettings()
    {
        var table = GetHammerPieceTable();
        if (table != null && _stuWardPrefab != null)
        {
            _hammerTable = table;
            if (!table.m_pieces.Contains(_stuWardPrefab)) table.m_pieces.Add(_stuWardPrefab);
        }
        ApplyVanillaGuardStoneRecipeSetting();
        ApplyStuWardRecipeSetting();
    }

    internal static Sprite? GetPieceIcon()
    {
        var piece = GetStuWardPiece();
        if (piece != null && piece.m_icon != null)
        {
            return piece.m_icon;
        }

        return FindPrefab(StuWardArea.BasePrefabName)?.GetComponent<Piece>()?.m_icon;
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

        return null;
    }

    private static void ApplyVanillaGuardStoneRecipeSetting()
    {
        var pieceTable = GetHammerPieceTable();
        var pieces = pieceTable?.m_pieces;
        var guardStonePrefab = FindPrefab(StuWardArea.BasePrefabName);
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
        var hammerPrefab = FindPrefab("Hammer");
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

        return FindPrefab(prefabName);
    }

    private static GameObject? FindPrefab(string name)
    {
        if (name == StuWardArea.PrefabName && _stuWardPrefab != null) return _stuWardPrefab;
        return ObjectDB.instance?.GetItemPrefab(name) ?? ZNetScene.instance?.GetPrefab(name);
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

[HarmonyPatch(typeof(ObjectDB), "Awake")]
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

[HarmonyPatch(typeof(ZNetScene), "Awake")]
internal static class StuWardRegisterPrefabPatch
{
    private static void Prefix(ZNetScene __instance) => StuWardPrefab.Register(__instance);
    private static void Postfix() => StuWardPrefab.ApplyRecipeSettings();
}

[HarmonyPatch(typeof(ZNetScene), "OnDestroy")]
internal static class StuWardReleasePrefabPatch
{
    private static void Postfix() => StuWardPrefab.Shutdown();
}
