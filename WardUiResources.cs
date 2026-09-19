using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.IO;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.U2D;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace STUWard;

// Only STUWard's visual resources and widgets. Screen state and RPCs stay in the controller.
internal sealed class WardUiResources
{
    internal static bool IsHeadless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
    internal static bool InputBlocked { get; private set; }
    internal static void BlockInput(bool visible) => InputBlocked = visible;
    internal Font AveriaSerif { get; private set; } = null!;
    internal Font AveriaSerifBold { get; private set; } = null!;
    // Retained visual constants from Jotunn's MIT-licensed UI helpers; see THIRD_PARTY_NOTICES.txt.
    internal readonly Color ValheimBeige = new(0.8529f, 0.725f, 0.5331f, 1f);
    internal readonly Color ValheimOrange = new(1f, 0.631f, 0.235f, 1f);
    internal readonly Color ValheimYellow = new(1f, 0.889f, 0f, 1f);
    internal ColorBlock ValheimScrollbarHandleColorBlock => new()
    {
        normalColor = new Color(0.926f, 0.645f, 0.34f), highlightedColor = new Color(1f, 0.786f, 0.088f),
        pressedColor = new Color(0.838f, 0.647f, 0.03f), selectedColor = new Color(1f, 0.786f, 0.088f),
        disabledColor = new Color(0.784f, 0.784f, 0.784f, 0.502f), colorMultiplier = 1f, fadeDuration = 0.1f
    };
    internal GameObject? CanvasRoot { get; private set; }
    private DefaultControls.Resources _controls;
    private Sprite? _panelSprite;
    private Material? _panelMaterial;
    private readonly List<Sprite> _ownedSprites = new();
    private readonly List<SoftReference<Object>> _assetHandles = new();

    internal static void PrepareAssetCatalog()
    {
        // This enables the extended manifest, not eager loading of every asset.
        // Run before localization/UI can cause the first SoftReference load.
        var loader = AccessTools.DeclaredField(typeof(SoftReferenceableAssets.Runtime), "s_assetLoader");
        if (loader.GetValue(null) == null) SoftReferenceableAssets.Runtime.MakeAllAssetsLoadable();
    }

    internal bool EnsureReady()
    {
        if (IsHeadless || Hud.instance == null) return false;
        if (CanvasRoot != null) return true;
        Transform? parent = null;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == "GuiRoot") parent = root.transform.Find("GUI");
            if (root.name == "_GameMain") parent = root.transform.Find("LoadingGUI");
            if (parent != null) break;
        }
        if (parent == null) return false;
        // Called only on scene readiness or explicit window creation, never in Update.
        var assetPaths = SoftReferenceableAssets.Runtime.GetAllAssetPathsInBundleMappedToAssetID();
        T? LoadAsset<T>(string name) where T : Object
        {
            foreach (var pair in assetPaths)
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(pair.Key), name, StringComparison.OrdinalIgnoreCase)) continue;
                var reference = new SoftReference<Object>(pair.Value);
                if (!reference.IsValid) continue;
                reference.Load();
                if (reference.Asset is T asset) { _assetHandles.Add(reference); return asset; }
                reference.Release();
            }
            return null;
        }
        AveriaSerif = LoadAsset<Font>("AveriaSerifLibre-Regular")!;
        AveriaSerifBold = LoadAsset<Font>("AveriaSerifLibre-Bold")!;
        foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
        {
            if (AveriaSerif == null && font.name == "AveriaSerifLibre-Regular") AveriaSerif = font;
            if (AveriaSerifBold == null && font.name == "AveriaSerifLibre-Bold") AveriaSerifBold = font;
        }
        if (AveriaSerif == null || AveriaSerifBold == null)
        {
            Plugin.Log.LogWarning("STUWard UI fonts are not available yet.");
            Release();
            return false;
        }
        var uiAtlas = LoadAsset<SpriteAtlas>("UIAtlas");
        var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
        var atlases = Resources.FindObjectsOfTypeAll<SpriteAtlas>();
        Sprite? Find(string name)
        {
            if (uiAtlas != null)
            {
                var sprite = uiAtlas.GetSprite(name);
                if (sprite != null) { _ownedSprites.Add(sprite); return sprite; }
            }
            foreach (var sprite in sprites) if (sprite.name == name) return sprite;
            foreach (var atlas in atlases)
            {
                if (atlas.name != "UIAtlas") continue;
                var sprite = atlas.GetSprite(name);
                if (sprite != null) { _ownedSprites.Add(sprite); return sprite; }
            }
            return null;
        }
        _controls.standard = Find("button");
        _controls.background = _controls.inputField = Find("text_field");
        _controls.knob = _controls.checkmark = Find("checkbox_marker");
        _panelSprite = Find("woodpanel_trophys");
        _panelMaterial = LoadAsset<Material>("litpanel");
        if (_panelMaterial == null) foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
            if (material.name == "litpanel") { _panelMaterial = material; break; }
        CanvasRoot = new GameObject("STUWardCanvas", typeof(RectTransform), typeof(GuiPixelFix), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        CanvasRoot.layer = 5;
        CanvasRoot.transform.SetParent(parent, false);
        var rect = CanvasRoot.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var canvas = CanvasRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        canvas.sortingOrder = 2000;
        var scaler = CanvasRoot.GetComponent<CanvasScaler>();
        scaler.referencePixelsPerUnit = 50f;
        return true;
    }

    internal void Release()
    {
        BlockInput(false);
        if (CanvasRoot != null) { CanvasRoot.SetActive(false); Object.Destroy(CanvasRoot); }
        CanvasRoot = null;
        foreach (var sprite in _ownedSprites) if (sprite != null) Object.Destroy(sprite);
        _ownedSprites.Clear();
        foreach (var handle in _assetHandles) handle.Release();
        _assetHandles.Clear();
        _controls = default;
        _panelSprite = null;
        _panelMaterial = null;
        AveriaSerif = AveriaSerifBold = null!;
    }

    private static void Place(GameObject go, Transform parent, Vector2 min, Vector2 max, Vector2 position, float width, float height)
    {
        go.transform.SetParent(parent, false);
        go.layer = 5;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, height);
    }

    internal GameObject CreateWoodpanel(Transform parent, Vector2 min, Vector2 max, Vector2 position, float width, float height, bool draggable)
    {
        var go = DefaultControls.CreatePanel(_controls);
        Place(go, parent, min, max, position, width, height);
        var image = go.GetComponent<Image>();
        image.sprite = _panelSprite;
        image.type = Image.Type.Sliced;
        image.material = _panelMaterial;
        image.color = _panelSprite != null ? Color.white : new Color(0.14f, 0.10f, 0.07f, 1f);
        return go;
    }

    internal GameObject CreateButton(string text, Transform parent, Vector2 min, Vector2 max, Vector2 position, float width, float height)
    {
        var go = DefaultControls.CreateButton(_controls);
        Place(go, parent, min, max, position, width, height);
        var label = go.GetComponentInChildren<Text>();
        ApplyTextStyle(label, AveriaSerifBold, ValheimOrange, 16, true);
        go.GetComponent<Button>().colors = new ColorBlock
        {
            normalColor = new Color(0.824f, 0.824f, 0.824f), highlightedColor = new Color(1.3f, 1.3f, 1.3f),
            pressedColor = new Color(0.537f, 0.556f, 0.556f), selectedColor = new Color(0.824f, 0.824f, 0.824f),
            disabledColor = new Color(0.566f, 0.566f, 0.566f, 0.502f), colorMultiplier = 1f, fadeDuration = 0.1f
        };
        label.text = text;
        return go;
    }

    internal GameObject CreateText(string text, Transform parent, Vector2 min, Vector2 max, Vector2 position, Font font, int fontSize, Color color, bool outline, Color outlineColor, float width, float height, bool bestFit)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        Place(go, parent, min, max, position, width, height);
        var label = go.GetComponent<Text>();
        ApplyTextStyle(label, font, color, fontSize, outline);
        label.text = text;
        label.resizeTextForBestFit = bestFit;
        if (outline) go.GetComponent<Outline>().effectColor = outlineColor;
        return go;
    }

    internal void ApplyTextStyle(Text text, Font font, Color color, int size, bool outline)
    {
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        if (outline && text.GetComponent<Outline>() == null) text.gameObject.AddComponent<Outline>().effectColor = Color.black;
    }

    internal GameObject CreateInputField(Transform parent, Vector2 min, Vector2 max, Vector2 position, InputField.ContentType type, string placeholder, int size, float width, float height)
    {
        var go = DefaultControls.CreateInputField(_controls);
        Place(go, parent, min, max, position, width, height);
        var input = go.GetComponent<InputField>();
        input.contentType = type;
        ApplyTextStyle(input.textComponent, AveriaSerif, ValheimBeige, size, false);
        var hint = (Text)input.placeholder;
        ApplyTextStyle(hint, AveriaSerif, new Color(0.65f, 0.62f, 0.55f), size, false);
        hint.text = placeholder;
        return go;
    }

    internal void ApplySliderStyle(Slider slider)
    {
        if (slider.fillRect != null && slider.fillRect.TryGetComponent<Image>(out var fill)) fill.color = ValheimOrange;
        if (slider.handleRect != null && slider.handleRect.TryGetComponent<Image>(out var handle))
        {
            handle.sprite = _controls.knob;
            handle.color = ValheimBeige;
        }
    }

    internal GameObject CreateScrollView(Transform parent, bool horizontal, bool vertical, float handleSize, float distance, ColorBlock colors, Color background, float width, float height)
    {
        var root = new GameObject("ScrollRoot", typeof(RectTransform));
        Place(root, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, width, height);
        var go = DefaultControls.CreateScrollView(_controls);
        go.name = "Scroll View";
        Place(go, root.transform, Vector2.zero, Vector2.one, Vector2.zero, 0f, 0f);
        go.GetComponent<Image>().color = background;
        var scroll = go.GetComponent<ScrollRect>();
        scroll.horizontal = horizontal;
        scroll.vertical = vertical;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.horizontalScrollbar.gameObject.SetActive(horizontal);
        scroll.verticalScrollbar.gameObject.SetActive(vertical);
        scroll.verticalScrollbar.colors = colors;
        scroll.verticalScrollbar.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, handleSize);
        scroll.verticalScrollbarSpacing = distance;
        // RectMask2D avoids depending on Jotunn's bundled mask sprite.
        var mask = scroll.viewport.GetComponent<Mask>();
        if (mask != null) Object.DestroyImmediate(mask);
        scroll.viewport.gameObject.AddComponent<RectMask2D>();
        var viewportImage = scroll.viewport.GetComponent<Image>();
        viewportImage.enabled = false;
        var content = scroll.content;
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return root;
    }

    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    private static class TextInputPatch
    {
        private static void Postfix(ref bool __result) => __result |= InputBlocked;
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    private static class PlayerInputPatch
    {
        private static void Postfix(ref bool __result) { if (InputBlocked) __result = false; }
    }

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    private static class ControllerInputPatch
    {
        private static void Postfix(ref bool __result) { if (InputBlocked) __result = false; }
    }

    [HarmonyPatch]
    private static class MenuInputPatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(InventoryGui), "Update");
            yield return AccessTools.DeclaredMethod(typeof(GameCamera), "UpdateCamera");
        }
        private static bool IncludeWard(bool menuVisible) => menuVisible || InputBlocked;
        [HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var menu = AccessTools.DeclaredMethod(typeof(Menu), nameof(Menu.IsVisible));
            var include = AccessTools.DeclaredMethod(typeof(MenuInputPatch), nameof(IncludeWard));
            var found = false;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!instruction.Calls(menu)) continue;
                found = true;
                yield return new CodeInstruction(OpCodes.Call, include);
            }
            if (!found) throw new InvalidOperationException("STUWard input guard: expected Menu.IsVisible call was not found.");
        }
    }

    [HarmonyPatch]
    private static class AssetCatalogPatch
    {
        private static System.Reflection.MethodBase TargetMethod() => AccessTools.DeclaredMethod(
            typeof(SoftReferenceableAssets.Runtime).Assembly.GetType("SoftReferenceableAssets.AssetBundleLoader", true), "InitializeDataSide");
        // Also covers loaders recreated after a world/session change.
        private static void Prefix(ref bool allAssetsLoadable) => allAssetsLoadable = true;
    }

    [HarmonyPatch]
    private static class AssetPathsPatch
    {
        private static System.Reflection.MethodBase TargetMethod() => AccessTools.DeclaredMethod(
            typeof(SoftReferenceableAssets.Runtime).Assembly.GetType("SoftReferenceableAssets.AssetBundleLoader", true), "GetAllAssetPathsMappedToAssetID");
        private static void AddPath(Dictionary<string, AssetID> paths, string path, AssetID id)
        {
            // Preserve the first mapping, as the former Jotunn asset lookup did.
            if (path != null && !paths.ContainsKey(path)) paths.Add(path, id);
        }
        // Jotunn's lazily registered transpiler needs the original Add call.
        // It applies the same guard; let it run first whenever both are present.
        [HarmonyAfter("com.jotunn.jotunn")]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var add = AccessTools.DeclaredMethod(typeof(Dictionary<string, AssetID>), "Add");
            var replacement = AccessTools.DeclaredMethod(typeof(AssetPathsPatch), nameof(AddPath));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(add))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }
    }
}
