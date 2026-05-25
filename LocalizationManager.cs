using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using Jotunn.Entities;
using Jotunn.Managers;
using YamlDotNet.Serialization;

namespace LocalizationManager;

public static class Localizer
{
    private static readonly string[] FileExtensions = [".json", ".yml"];
    private static readonly Dictionary<string, Dictionary<string, string>> CachedTranslations = new(StringComparer.OrdinalIgnoreCase);

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreFields()
        .Build();

    private static BaseUnityPlugin? _plugin;
    private static bool _registeredWithJotunn;
    private static bool _hookedJotunn;
    private static string? _lastLoggedAppliedLanguage;
    private static int _lastLoggedAppliedCount = -1;

    public static event Action? OnLocalizationComplete;

    private static BaseUnityPlugin Plugin
    {
        get
        {
            if (_plugin != null)
            {
                return _plugin;
            }

            IEnumerable<TypeInfo> types;
            try
            {
                types = Assembly.GetExecutingAssembly().DefinedTypes.ToList();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type != null).Select(type => type!.GetTypeInfo());
            }

            _plugin = (BaseUnityPlugin)Chainloader.ManagerObject.GetComponent(
                types.First(type => type.IsClass && typeof(BaseUnityPlugin).IsAssignableFrom(type)));

            return _plugin;
        }
    }

    private static CustomLocalization CustomLocalization => Jotunn.Managers.LocalizationManager.Instance.GetLocalization();

    public static void Load()
    {
        _ = Plugin;
        LoadTranslations();
        RegisterWithJotunn();
        HookJotunn();
        ReloadCurrentLanguageIfAvailable();
        SafeCallLocalizeComplete();
    }

    public static void Unload()
    {
        if (_hookedJotunn)
        {
            Jotunn.Managers.LocalizationManager.OnLocalizationAdded -= ReloadCurrentLanguageIfAvailable;
            _hookedJotunn = false;
        }

        _plugin = null;
        _registeredWithJotunn = false;
        _lastLoggedAppliedLanguage = null;
        _lastLoggedAppliedCount = -1;
    }

    public static void ReloadCurrentLanguageIfAvailable()
    {
        if (Localization.instance == null)
        {
            return;
        }

        LoadTranslations();
        RegisterWithJotunn();
        ApplyCurrentLanguage(Localization.instance);
    }

    public static void LoadLocalizationLater() => ReloadCurrentLanguageIfAvailable();

    public static void SafeCallLocalizeComplete() => OnLocalizationComplete?.Invoke();

    public static void AddText(string key, string text)
    {
        if (!CachedTranslations.TryGetValue("English", out var english))
        {
            english = new Dictionary<string, string>(StringComparer.Ordinal);
            CachedTranslations["English"] = english;
        }

        english[key] = text;
        var language = "English";
        var token = key;
        CustomLocalization.ClearToken(language, token);
        CustomLocalization.AddTranslation(language, token, text);

        if (Localization.instance != null)
        {
            Localization.instance.AddWord(key, text);
        }
    }

    private static void LoadTranslations()
    {
        if (CachedTranslations.Count > 0)
        {
            return;
        }

        var availableLanguages = GetAvailableLanguages();
        var english = ReadMergedLanguage("English", null);
        if (english == null || english.Count == 0)
        {
            throw new InvalidOperationException(
                $"Found no English localizations in mod {Plugin.Info.Metadata.Name}. Expected translations/English.json or translations/English.yml.");
        }

        CachedTranslations["English"] = english;

        foreach (var language in availableLanguages)
        {
            if (language.Equals("English", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var merged = ReadMergedLanguage(language, english);
            if (merged != null && merged.Count > 0)
            {
                CachedTranslations[language] = merged;
            }
        }

        global::STUWard.Plugin.Log?.LogInfo(
            $"Loaded STUWard localizations: {string.Join(", ", CachedTranslations.Select(kv => $"{kv.Key}={kv.Value.Count}"))}");
    }

    private static void RegisterWithJotunn()
    {
        if (_registeredWithJotunn)
        {
            return;
        }

        _registeredWithJotunn = true;
        foreach (var (language, translations) in CachedTranslations)
        {
            var translationCopy = new Dictionary<string, string>(translations, StringComparer.Ordinal);
            CustomLocalization.AddTranslation(language, translationCopy);
        }

        global::STUWard.Plugin.Log?.LogInfo(
            $"Registered STUWard localizations with Jotunn: {string.Join(", ", CachedTranslations.Keys)}");
    }

    private static void HookJotunn()
    {
        if (_hookedJotunn)
        {
            return;
        }

        Jotunn.Managers.LocalizationManager.OnLocalizationAdded += ReloadCurrentLanguageIfAvailable;
        _hookedJotunn = true;
    }

    private static void ApplyCurrentLanguage(Localization localization)
    {
        var selectedLanguage = localization.GetSelectedLanguage();
        var translations = GetTranslationsForLanguage(selectedLanguage);

        foreach (var (key, value) in translations)
        {
            localization.AddWord(key, value);
        }

        if (!string.Equals(_lastLoggedAppliedLanguage, selectedLanguage, StringComparison.Ordinal) ||
            _lastLoggedAppliedCount != translations.Count)
        {
            _lastLoggedAppliedLanguage = selectedLanguage;
            _lastLoggedAppliedCount = translations.Count;
            global::STUWard.Plugin.Log?.LogInfo(
                $"Applied STUWard localization for language '{selectedLanguage}' with {translations.Count} entries.");
        }
    }

    private static Dictionary<string, string> GetTranslationsForLanguage(string language)
    {
        if (CachedTranslations.TryGetValue(language, out var translations))
        {
            return translations;
        }

        return CachedTranslations["English"];
    }

    private static HashSet<string> GetAvailableLanguages()
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "English"
        };

        foreach (var resourceName in typeof(Localizer).Assembly.GetManifestResourceNames())
        {
            foreach (var extension in FileExtensions)
            {
                var suffix = $"translations.{extension}";
                if (!resourceName.Contains(".translations.", StringComparison.OrdinalIgnoreCase) ||
                    !resourceName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var markerIndex = resourceName.LastIndexOf(".translations.", StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                {
                    continue;
                }

                var start = markerIndex + ".translations.".Length;
                var languageLength = resourceName.Length - start - extension.Length;
                if (languageLength <= 0)
                {
                    continue;
                }

                languages.Add(resourceName.Substring(start, languageLength));
            }
        }

        foreach (var filePath in Directory
                     .GetFiles(Paths.PluginPath, $"{Plugin.Info.Metadata.Name}.*", SearchOption.AllDirectories)
                     .Where(path => FileExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            var parts = Path.GetFileNameWithoutExtension(filePath).Split('.');
            if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                languages.Add(parts[1]);
            }
        }

        return languages;
    }

    private static Dictionary<string, string>? ReadMergedLanguage(
        string language,
        IReadOnlyDictionary<string, string>? englishFallback)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (englishFallback != null)
        {
            MergeInto(merged, englishFallback);
        }

        var embedded = ReadEmbeddedLanguage(language);
        if (embedded != null)
        {
            MergeInto(merged, embedded);
        }

        var external = ReadExternalLanguage(language);
        if (external != null)
        {
            MergeInto(merged, external);
        }

        return merged.Count == 0 ? null : merged;
    }

    private static Dictionary<string, string>? ReadEmbeddedLanguage(string language)
    {
        foreach (var extension in FileExtensions)
        {
            if (ReadEmbeddedFileBytes($"translations.{language}{extension}", typeof(Localizer).Assembly) is not { } data)
            {
                continue;
            }

            return DeserializeTranslations(Encoding.UTF8.GetString(data));
        }

        return null;
    }

    private static Dictionary<string, string>? ReadExternalLanguage(string language)
    {
        var filePath = FindExternalLanguageFile(language);
        return filePath == null ? null : DeserializeTranslations(File.ReadAllText(filePath, Encoding.UTF8));
    }

    private static string? FindExternalLanguageFile(string language)
    {
        foreach (var extension in FileExtensions)
        {
            var expectedFileName = $"{Plugin.Info.Metadata.Name}.{language}{extension}";
            var filePath = Directory
                .GetFiles(Paths.PluginPath, expectedFileName, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                return filePath;
            }
        }

        return null;
    }

    private static Dictionary<string, string> DeserializeTranslations(string rawText)
    {
        return Deserializer.Deserialize<Dictionary<string, string>>(rawText) ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static void MergeInto(IDictionary<string, string> target, IReadOnlyDictionary<string, string> source)
    {
        foreach (var (key, value) in source)
        {
            target[key] = value;
        }
    }

    public static byte[]? ReadEmbeddedFileBytes(string resourceFileName, Assembly? containingAssembly = null)
    {
        using var stream = new MemoryStream();
        containingAssembly ??= Assembly.GetCallingAssembly();
        if (containingAssembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(resourceFileName, StringComparison.OrdinalIgnoreCase)) is not { } resourceName)
        {
            return null;
        }

        containingAssembly.GetManifestResourceStream(resourceName)?.CopyTo(stream);
        return stream.Length == 0 ? null : stream.ToArray();
    }
}

public static class LocalizationManagerVersion
{
    public const string Version = "1.4.1";
}
