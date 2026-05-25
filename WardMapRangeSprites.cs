using System;
using UnityEngine;

namespace STUWard;

internal static class WardMapRangeSprites
{
    private const int TextureSize = 512;
    private const int FixedDashCount = 24;
    private const int FixedStrokePixels = 12;
    private const float DashFillFraction = 0.55f;
    private const float RadialFeatherPixels = 1.25f;

    private static Sprite? _cachedSprite;
    private static Texture2D? _createdTexture;

    internal static void Reset()
    {
        if (_cachedSprite != null)
        {
            UnityEngine.Object.Destroy(_cachedSprite);
            _cachedSprite = null;
        }

        if (_createdTexture != null)
        {
            UnityEngine.Object.Destroy(_createdTexture);
            _createdTexture = null;
        }
    }

    internal static Sprite? GetRangeSprite(float radius)
    {
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f)
        {
            return null;
        }

        if (_cachedSprite != null)
        {
            return _cachedSprite;
        }

        try
        {
            _cachedSprite = CreateSprite();
            return _cachedSprite;
        }
        catch (Exception exception)
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Range",
                $"Failed to build dashed ward range sprite. dashCount={FixedDashCount}, strokePixels={FixedStrokePixels}, error={exception.Message}");
            return null;
        }
    }

    private static Sprite CreateSprite()
    {
        var dashCount = FixedDashCount;
        var strokePixels = FixedStrokePixels;
        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.ARGB32, false)
        {
            name = $"STUWard_RangeRing_Dashed_{dashCount}_{strokePixels}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color32[TextureSize * TextureSize];
        var center = (TextureSize - 1) * 0.5f;
        var outerRadius = center - 2f;
        var innerRadius = Mathf.Max(0f, outerRadius - strokePixels);
        var circumferencePixels = Mathf.Max(1f, 2f * Mathf.PI * outerRadius);
        var dashFeatherFraction = Mathf.Clamp(dashCount / circumferencePixels, 0.0025f, 0.08f);

        for (var y = 0; y < TextureSize; y++)
        {
            var dy = y - center;
            for (var x = 0; x < TextureSize; x++)
            {
                var dx = x - center;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                var radialAlpha = GetRadialAlpha(distance, innerRadius, outerRadius);
                if (radialAlpha <= 0f)
                {
                    continue;
                }

                var dashAlpha = GetDashAlpha(Mathf.Atan2(dy, dx), dashCount, dashFeatherFraction);
                if (dashAlpha <= 0f)
                {
                    continue;
                }

                var alpha = Mathf.Clamp01(radialAlpha * dashAlpha);
                pixels[(y * TextureSize) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        _createdTexture = texture;

        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, TextureSize, TextureSize),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect);
        sprite.name = texture.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static float GetRadialAlpha(float distance, float innerRadius, float outerRadius)
    {
        if (distance <= innerRadius - RadialFeatherPixels || distance >= outerRadius + RadialFeatherPixels)
        {
            return 0f;
        }

        if (distance < innerRadius + RadialFeatherPixels)
        {
            return Mathf.Clamp01((distance - (innerRadius - RadialFeatherPixels)) / (RadialFeatherPixels * 2f));
        }

        if (distance > outerRadius - RadialFeatherPixels)
        {
            return Mathf.Clamp01(((outerRadius + RadialFeatherPixels) - distance) / (RadialFeatherPixels * 2f));
        }

        return 1f;
    }

    private static float GetDashAlpha(float angle, int dashCount, float dashFeatherFraction)
    {
        if (dashCount <= 0)
        {
            return 1f;
        }

        var normalizedAngle = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f);
        var dashPhase = normalizedAngle * dashCount;
        var dashFraction = dashPhase - Mathf.Floor(dashPhase);
        if (dashFraction >= DashFillFraction)
        {
            return 0f;
        }

        if (dashFeatherFraction <= 0f)
        {
            return 1f;
        }

        var startAlpha = Mathf.Clamp01(dashFraction / dashFeatherFraction);
        var endAlpha = Mathf.Clamp01((DashFillFraction - dashFraction) / dashFeatherFraction);
        return Mathf.Min(startAlpha, endAlpha);
    }
}
