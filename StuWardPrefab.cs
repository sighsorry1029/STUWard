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
    internal const string OutpostPrefabName = "piece_stuward_outpost";
    internal const string BasePrefabName = "guard_stone";
    internal const string DisplayName = WardLocalization.PieceNameToken;
    internal const string Description = WardLocalization.PieceDescriptionToken;
    private static readonly int RegularPrefabHash = StringExtensionMethods.GetStableHashCode(PrefabName);
    private static readonly int OutpostPrefabHash = StringExtensionMethods.GetStableHashCode(OutpostPrefabName);
    internal static readonly Color OutpostColor = new(85f / 255f, 206f / 255f, 214f / 255f);

    // Serialized so Unity also preserves the kind on a placement ghost without a ZDO.
    [SerializeField] internal bool IsOutpostWard;

    internal static bool IsManagedPrefab(int prefabHash) =>
        prefabHash == RegularPrefabHash || prefabHash == OutpostPrefabHash;

    internal static bool IsOutpost(ZDO? zdo) => zdo != null && zdo.GetPrefab() == OutpostPrefabHash;

    internal static bool IsOutpost(PrivateArea? area)
    {
        if (area == null) return false;
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        return zdo != null ? IsOutpost(zdo) : area.GetComponent<StuWardArea>()?.IsOutpostWard == true;
    }

    internal static void CollectWorldZdos(ZDOMan zdoMan, List<ZDO> destination)
    {
        destination.Clear();
        // The game's iterative prefab scan can repeat sector 0 and a batch's
        // boundary sector. This ID dictionary includes unloaded wards exactly once.
        foreach (var zdo in zdoMan.GetWorldObjects().Values)
            if (zdo != null && zdo.IsValid() && IsManagedPrefab(zdo.GetPrefab())) destination.Add(zdo);
    }

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
    private static GameObject? _outpostWardPrefab;
    private static readonly List<Material> OutpostMaterials = new();
    private static GameObject? _vanillaGuardStonePrefab;
    private static int _vanillaGuardStoneIndex = -1;
    private static Piece.Requirement[]? _defaultStuWardRequirements;
    private static Sprite? _outpostIconSource;
    private static Sprite? _outpostIcon;
    private static Texture2D? _outpostIconTexture;
    // Called before ZNetScene builds its name/hash lookup. Inactive parent prevents
    // Awake from allocating a ZDO or destroying ZNetView on the prefab clone.
    internal static void Register(ZNetScene scene)
    {
        var source = scene.m_prefabs.Find(prefab => prefab != null && prefab.name == StuWardArea.BasePrefabName);
        if (source == null) throw new InvalidOperationException("Valheim guard_stone prefab is unavailable.");
        if (_prefabRoot == null)
        {
            _prefabRoot = new GameObject("STUWard Prefabs");
            _prefabRoot.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_prefabRoot);
        }
        RegisterPiece(scene, source, ref _stuWardPrefab, StuWardArea.PrefabName, false);
        RegisterPiece(scene, source, ref _outpostWardPrefab, StuWardArea.OutpostPrefabName, true);
        ApplyRecipeSettings();
    }

    private static void RegisterPiece(ZNetScene scene, GameObject source, ref GameObject? registered, string name, bool outpost)
    {
        var existing = scene.m_prefabs.Find(prefab => prefab != null && prefab.name == name);
        if (existing != null && existing != registered)
            throw new InvalidOperationException("Another prefab is already registered as " + name);
        if (registered != null)
        {
            if (existing == null) scene.m_prefabs.Add(registered);
            return;
        }

        var prefab = UnityEngine.Object.Instantiate(source, _prefabRoot!.transform, false);
        prefab.name = name;
        prefab.SetActive(true);
        var piece = prefab.GetComponent<Piece>();
        var area = prefab.GetComponent<PrivateArea>();
        if (piece == null || area == null)
        {
            Shutdown();
            throw new InvalidOperationException("guard_stone is missing Piece or PrivateArea.");
        }
        prefab.AddComponent<StuWardArea>().IsOutpostWard = outpost;
        prefab.AddComponent<StuWardPlacedHook>();

        // Preserve the defaults formerly applied by PieceConfig.
        piece.m_enabled = true;
        piece.m_allowedInDungeons = false;
        piece.m_name = outpost ? WardLocalization.OutpostNameToken : StuWardArea.DisplayName;
        piece.m_description = outpost ? WardLocalization.OutpostDescriptionToken : StuWardArea.Description;
        piece.m_resources = CloneRequirements(piece.m_resources);
        area.m_name = piece.m_name;
        area.m_radius = WardSettings.MinRadius;
        if (area.m_areaMarker != null)
        {
            area.m_areaMarker.m_radius = WardSettings.MinRadius;
        }

        registered = prefab;
        if (!outpost) _defaultStuWardRequirements = CloneRequirements(piece.m_resources);
        if (outpost && !WardUiResources.IsHeadless)
        {
            ApplyOutpostColors(area);
            // The Hammer and map share this prefab's icon and its lifetime.
            piece.m_icon = GetOutpostIcon(piece.m_icon);
        }

        scene.m_prefabs.Add(prefab);
        Plugin.Log.LogInfo($"Registered {name} without Jotunn.");
    }

    private static void ApplyOutpostColors(PrivateArea area)
    {
        // Never modify shared vanilla materials. UpdateStatus only toggles the
        // emission keyword, so the cloned emission color survives enable/disable.
        if (area.m_model != null)
        {
            var materials = area.m_model.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null || !source.HasProperty("_EmissionColor")) continue;
                var original = source.GetColor("_EmissionColor");
                var intensity = Mathf.Max(original.r, Mathf.Max(original.g, original.b));
                if (intensity <= 0f) continue;
                var material = new Material(source);
                var color = StuWardArea.OutpostColor * intensity;
                color.a = original.a;
                material.SetColor("_EmissionColor", color);
                materials[i] = material;
                OutpostMaterials.Add(material);
            }
            area.m_model.sharedMaterials = materials;
        }
        if (area.m_areaMarker != null && area.m_areaMarker.m_prefab != null)
        {
            // A private segment prefab keeps placement highlighting/MaterialMan
            // resets from restoring the vanilla color. Never tint the shared asset.
            var segment = UnityEngine.Object.Instantiate(area.m_areaMarker.m_prefab, _prefabRoot!.transform, false);
            segment.name = "STUWard Outpost Boundary";
            foreach (var renderer in segment.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null) continue;
                    var material = new Material(source);
                    foreach (var property in WardSettings.AreaMarkerColorProperties)
                        if (material.HasProperty(property))
                            material.SetColor(property, TintOutpostColor(source.GetColor(property)));
                    materials[i] = material;
                    OutpostMaterials.Add(material);
                }
                renderer.sharedMaterials = materials;
            }
            area.m_areaMarker.m_prefab = segment;
        }
        var effect = area.m_enabledEffect;
        if (effect == null) return;
        foreach (var light in effect.GetComponentsInChildren<Light>(true)) light.color = StuWardArea.OutpostColor;
        foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = particles.main;
            var original = main.startColor;
            // guard_stone uses Color/TwoColors here. Leave unfamiliar gradients
            // intact instead of discarding an animation from another mod.
            if (original.mode == ParticleSystemGradientMode.Color)
                main.startColor = TintOutpostColor(original.color);
            else if (original.mode == ParticleSystemGradientMode.TwoColors)
                main.startColor = new ParticleSystem.MinMaxGradient(
                    TintOutpostColor(original.colorMin), TintOutpostColor(original.colorMax));
        }
    }

    private static Color TintOutpostColor(Color original)
    {
        var color = StuWardArea.OutpostColor * Mathf.Max(original.r, Mathf.Max(original.g, original.b));
        color.a = original.a;
        return color;
    }

    internal static void Shutdown()
    {
        if (_hammerTable != null)
        {
            _hammerTable.m_pieces.Remove(_stuWardPrefab);
            _hammerTable.m_pieces.Remove(_outpostWardPrefab);
            if (_vanillaGuardStonePrefab != null && !_hammerTable.m_pieces.Contains(_vanillaGuardStonePrefab))
                _hammerTable.m_pieces.Insert(Mathf.Clamp(_vanillaGuardStoneIndex, 0, _hammerTable.m_pieces.Count), _vanillaGuardStonePrefab);
        }
        _hammerTable = null;
        _stuWardPrefab = null;
        _outpostWardPrefab = null;
        _vanillaGuardStonePrefab = null;
        _vanillaGuardStoneIndex = -1;
        _defaultStuWardRequirements = null;
        if (_prefabRoot != null) UnityEngine.Object.Destroy(_prefabRoot);
        _prefabRoot = null;
        foreach (var material in OutpostMaterials) UnityEngine.Object.Destroy(material);
        OutpostMaterials.Clear();
        ClearOutpostIcon();
    }

    internal static void ApplyRecipeSettings()
    {
        var table = GetHammerPieceTable();
        var piece = GetStuWardPiece();
        if (table != null && _stuWardPrefab != null)
        {
            _hammerTable = table;
            if (!table.m_pieces.Contains(_stuWardPrefab)) table.m_pieces.Add(_stuWardPrefab);
            if (_outpostWardPrefab != null && !table.m_pieces.Contains(_outpostWardPrefab)) table.m_pieces.Add(_outpostWardPrefab);
        }

        var refreshForVanillaRecipe = ApplyVanillaGuardStoneRecipeSetting(table);
        var refreshForStuWardRecipe = ApplyStuWardRecipeSetting(piece, outpost: false);
        var refreshForOutpostRecipe = ApplyStuWardRecipeSetting(GetStuWardPiece(outpost: true), outpost: true);
        if (refreshForVanillaRecipe || refreshForStuWardRecipe || refreshForOutpostRecipe)
        {
            Player.m_localPlayer?.UpdateAvailablePiecesList();
        }
    }

    internal static Sprite? GetPieceIcon(bool outpost = false)
    {
        var piece = GetStuWardPiece(outpost);
        if (piece != null && piece.m_icon != null)
        {
            return piece.m_icon;
        }

        return FindPrefab(StuWardArea.BasePrefabName)?.GetComponent<Piece>()?.m_icon;
    }

    internal static Piece.Requirement[] GetCurrentWardRequirements(bool outpost)
    {
        return CloneRequirements(GetStuWardPiece(outpost)?.m_resources);
    }

    private static Sprite? GetOutpostIcon(Sprite? source)
    {
        if (WardUiResources.IsHeadless || source == null) return source;
        if (_outpostIconSource == source) return _outpostIcon != null ? _outpostIcon : source;
        ClearOutpostIcon();
        // Cache a failed attempt too; retry only after the source or scene changes.
        _outpostIconSource = source;
        RenderTexture? temporary = null;
        var previousActive = RenderTexture.active;
        var previousSrgbWrite = GL.sRGBWrite;
        try
        {
            // The original guardstone icon is a non-readable BC7 atlas sprite,
            // packed as an unrotated 64x64 rectangle. Never edit its shared atlas.
            if (source.packed && source.packingRotation != SpritePackingRotation.None)
                throw new InvalidOperationException("The ward icon has unsupported atlas rotation.");
            var rect = source.textureRect;
            var width = Mathf.RoundToInt(rect.width);
            var height = Mathf.RoundToInt(rect.height);
            temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(source.texture, temporary,
                new Vector2(rect.width / source.texture.width, rect.height / source.texture.height),
                new Vector2(rect.x / source.texture.width, rect.y / source.texture.height));
            RenderTexture.active = temporary;
            _outpostIconTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "STUWard_Outpost_Icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _outpostIconTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            var pixels = _outpostIconTexture.GetPixels();
            for (var index = 0; index < pixels.Length; index++) pixels[index] = TintOutpostColor(pixels[index]);
            _outpostIconTexture.SetPixels(pixels);
            _outpostIconTexture.Apply(false, true);
            _outpostIcon = Sprite.Create(_outpostIconTexture, new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.5f), source.pixelsPerUnit, 0u, SpriteMeshType.FullRect);
            _outpostIcon.name = _outpostIconTexture.name;
            _outpostIcon.hideFlags = HideFlags.HideAndDontSave;
            return _outpostIcon;
        }
        catch (Exception exception)
        {
            ClearOutpostIcon();
            _outpostIconSource = source;
            Plugin.Log.LogWarning($"Unable to create Outpost icon: {exception.Message}. Using the regular icon.");
            return source;
        }
        finally
        {
            RenderTexture.active = previousActive;
            GL.sRGBWrite = previousSrgbWrite;
            if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private static void ClearOutpostIcon()
    {
        if (_outpostIcon != null) UnityEngine.Object.Destroy(_outpostIcon);
        if (_outpostIconTexture != null) UnityEngine.Object.Destroy(_outpostIconTexture);
        _outpostIcon = null;
        _outpostIconTexture = null;
        _outpostIconSource = null;
    }

    private static Piece? GetStuWardPiece(bool outpost = false)
    {
        var prefab = outpost ? _outpostWardPrefab : _stuWardPrefab;
        var piece = prefab != null ? prefab.GetComponent<Piece>() : null;
        if (piece != null)
        {
            return piece;
        }

        return null;
    }

    private static bool ApplyVanillaGuardStoneRecipeSetting(PieceTable? pieceTable)
    {
        var pieces = pieceTable?.m_pieces;
        var guardStonePrefab = FindPrefab(StuWardArea.BasePrefabName);
        if (pieceTable == null || pieces == null || guardStonePrefab == null)
        {
            return false;
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

        return true;
    }

    private static bool ApplyStuWardRecipeSetting(Piece? piece, bool outpost)
    {
        if (piece == null)
        {
            return false;
        }

        var recipeOverride = (outpost ? Plugin.OutpostWardRecipe : Plugin.StuWardRecipe)?.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(recipeOverride))
        {
            if (_defaultStuWardRequirements != null)
            {
                piece.m_resources = CloneRequirements(_defaultStuWardRequirements);
                return true;
            }

            return false;
        }

        if (!TryParseRequirements(recipeOverride, out var requirements))
        {
            Plugin.Log.LogWarning($"Invalid {(outpost ? "Outpost Ward" : "STUWard")} recipe override '{recipeOverride}'. Keeping previous recipe.");
            return false;
        }

        piece.m_resources = requirements;
        return true;
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
