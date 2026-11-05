using Colossal;
using FiscalOverhaul.Logic;
using Game.SceneFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace FiscalOverhaul
{
    /// <summary>
    /// Registers the mod's text with the game: every <c>l10n/&lt;locale&gt;.json</c> (embedded in the DLL by the csproj)
    /// becomes a locale source for that locale. en-US is the game's fallback locale, so its keys show in any language
    /// that has no file of its own, or lacks a key. The UI reads its keys through translate() with the same English as
    /// fallback (<c>UI/src/l10n.ts</c>). How to add a language: knowledge/cs2-ui-modding.md § Localization.
    /// </summary>
    public static class LocaleRegistry
    {
        /// <summary>The embedded resources' names: this plus "&lt;locale&gt;.json" (the csproj's LogicalName).</summary>
        public const string ResourcePrefix = "FiscalOverhaul.l10n.";

        private static readonly List<(string locale, LocaleSource source)> s_Sources = new List<(string, LocaleSource)>();

        public static void Register(Setting setting)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var manager = GameManager.instance.localizationManager;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                var locale = LocaleFile.LocaleIdFromResourceName(name, ResourcePrefix);
                if (locale == null) continue;
                try
                {
                    Dictionary<string, string> entries;
                    using (var stream = assembly.GetManifestResourceStream(name))
                    using (var reader = new StreamReader(stream))
                        entries = LocaleFile.Parse(reader.ReadToEnd());
                    var source = new LocaleSource(setting, entries);
                    manager.AddSource(locale, source);
                    s_Sources.Add((locale, source));
                    Mod.log.Info($"Locale {locale}: {entries.Count} strings");
                }
                catch (Exception e)
                {
                    Mod.log.Error(e, $"Locale file {name} not loaded");
                }
            }
        }

        public static void Unregister()
        {
            var manager = GameManager.instance?.localizationManager;
            if (manager != null)
                foreach (var (locale, source) in s_Sources)
                    manager.RemoveSource(locale, source);
            s_Sources.Clear();
        }
    }

    /// <summary>
    /// One locale file's text: UI keys as they are, Options keys mapped to the ids the Options page asks for. A key
    /// that is neither is skipped (with a warning) rather than overriding a game string.
    /// </summary>
    public class LocaleSource : IDictionarySource
    {
        private readonly Setting m_Setting;
        private readonly Dictionary<string, string> m_Entries;

        public LocaleSource(Setting setting, Dictionary<string, string> entries)
        {
            m_Setting = setting;
            m_Entries = entries;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            foreach (var entry in m_Entries)
            {
                if (LocaleFile.IsUiKey(entry.Key))
                    yield return entry;
                else if (LocaleFile.TryParseOptionKey(entry.Key, out var kind, out var name))
                    yield return new KeyValuePair<string, string>(OptionId(kind, name), entry.Value);
                else
                    Mod.log.Warn($"Locale key {entry.Key} is neither FiscalOverhaul.UI.* nor a known FiscalOverhaul.Options.* key; skipped");
            }
        }

        private string OptionId(OptionTextKind kind, string name)
        {
            switch (kind)
            {
                case OptionTextKind.Title: return m_Setting.GetSettingsLocaleID();
                case OptionTextKind.Tab: return m_Setting.GetOptionTabLocaleID(name);
                case OptionTextKind.Group: return m_Setting.GetOptionGroupLocaleID(name);
                case OptionTextKind.Label: return m_Setting.GetOptionLabelLocaleID(name);
                case OptionTextKind.Desc: return m_Setting.GetOptionDescLocaleID(name);
                case OptionTextKind.Warning: return m_Setting.GetOptionWarningLocaleID(name);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public void Unload()
        {
        }
    }
}
