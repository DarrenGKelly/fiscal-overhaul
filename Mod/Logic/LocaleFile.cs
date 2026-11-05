using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FiscalOverhaul.Logic
{
    /// <summary>What an Options key in a locale file names (<c>FiscalOverhaul.Options.&lt;Kind&gt;[.&lt;Name&gt;]</c>).</summary>
    public enum OptionTextKind
    {
        /// <summary>The mod's name on the Options page (<c>FiscalOverhaul.Options.Title</c>).</summary>
        Title,
        Tab,
        Group,
        Label,
        Desc,
        Warning,
    }

    /// <summary>
    /// The mod's locale files (<c>l10n/&lt;locale&gt;.json</c>, one flat JSON object of key to text per language): parsing
    /// them, and which key is what. UI keys (<c>FiscalOverhaul.UI.*</c>) go to the game as they are; Options keys are
    /// mapped to the ids the game's Options page asks for (<c>Mod/LocaleRegistry.cs</c>). No game types, so it is tested.
    /// </summary>
    public static class LocaleFile
    {
        public const string UiPrefix = "FiscalOverhaul.UI.";
        public const string OptionsPrefix = "FiscalOverhaul.Options.";

        /// <summary>The locale a file is for, from its name: "FiscalOverhaul.l10n.en-US.json" gives "en-US"; null if it isn't one.</summary>
        public static string LocaleIdFromResourceName(string resourceName, string resourcePrefix)
        {
            if (resourceName == null || !resourceName.StartsWith(resourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return null;
            var id = resourceName.Substring(resourcePrefix.Length, resourceName.Length - resourcePrefix.Length - ".json".Length);
            return id.Length > 0 && id.IndexOf('.') < 0 ? id : null;
        }

        /// <summary>
        /// Splits an Options key: "FiscalOverhaul.Options.Label.EnableBudgetChart" is (Label, "EnableBudgetChart"),
        /// "FiscalOverhaul.Options.Title" is (Title, null). False for any other key, a kind it doesn't know, a Title with
        /// a name or another kind without one.
        /// </summary>
        public static bool TryParseOptionKey(string key, out OptionTextKind kind, out string name)
        {
            kind = OptionTextKind.Title;
            name = null;
            if (key == null || !key.StartsWith(OptionsPrefix, StringComparison.Ordinal)) return false;
            var rest = key.Substring(OptionsPrefix.Length);
            var dot = rest.IndexOf('.');
            var kindText = dot < 0 ? rest : rest.Substring(0, dot);
            name = dot < 0 ? null : rest.Substring(dot + 1);
            if (!Enum.TryParse(kindText, false, out kind) || !Enum.IsDefined(typeof(OptionTextKind), kind) || kindText != kind.ToString())
                return false;
            if (kind == OptionTextKind.Title) return name == null;
            return !string.IsNullOrEmpty(name);
        }

        /// <summary>Whether a key is one the game should get as it is: a UI string.</summary>
        public static bool IsUiKey(string key) => key != null && key.Length > UiPrefix.Length && key.StartsWith(UiPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Parses a locale file: one JSON object whose values are all strings. Throws <see cref="FormatException"/> on
        /// anything else (a translator's stray comma should fail loudly in the log, not drop the whole language quietly).
        /// </summary>
        public static Dictionary<string, string> Parse(string json)
        {
            if (json == null) throw new FormatException("No locale text.");
            var p = new Parser(json);
            var result = p.ReadObject();
            p.SkipWhitespace();
            if (!p.AtEnd) throw p.Error("text after the closing brace");
            return result;
        }

        private sealed class Parser
        {
            private readonly string m_Text;
            private int m_Pos;

            public Parser(string text)
            {
                m_Text = text;
                // A UTF-8 byte-order mark read as text.
                if (m_Text.Length > 0 && m_Text[0] == '﻿') m_Pos = 1;
            }

            public bool AtEnd => m_Pos >= m_Text.Length;

            public FormatException Error(string what) => new FormatException($"Locale file: {what} at character {m_Pos}.");

            public void SkipWhitespace()
            {
                while (!AtEnd && (m_Text[m_Pos] == ' ' || m_Text[m_Pos] == '\t' || m_Text[m_Pos] == '\r' || m_Text[m_Pos] == '\n')) m_Pos++;
            }

            private void Expect(char c)
            {
                SkipWhitespace();
                if (AtEnd || m_Text[m_Pos] != c) throw Error($"expected '{c}'");
                m_Pos++;
            }

            public Dictionary<string, string> ReadObject()
            {
                var result = new Dictionary<string, string>();
                Expect('{');
                SkipWhitespace();
                if (!AtEnd && m_Text[m_Pos] == '}')
                {
                    m_Pos++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    var key = ReadString();
                    Expect(':');
                    SkipWhitespace();
                    var value = ReadString();
                    if (result.ContainsKey(key)) throw Error($"duplicate key \"{key}\"");
                    result[key] = value;
                    SkipWhitespace();
                    if (AtEnd) throw Error("missing '}'");
                    var c = m_Text[m_Pos++];
                    if (c == '}') return result;
                    if (c != ',') throw Error("expected ',' or '}'");
                }
            }

            private string ReadString()
            {
                if (AtEnd || m_Text[m_Pos] != '"') throw Error("expected a string");
                m_Pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    var c = m_Text[m_Pos++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ') throw Error("control character in a string");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("unterminated escape");
                    var e = m_Text[m_Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (m_Pos + 4 > m_Text.Length ||
                                !int.TryParse(m_Text.Substring(m_Pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                                throw Error("bad \\u escape");
                            sb.Append((char)code);
                            m_Pos += 4;
                            break;
                        default: throw Error($"bad escape \\{e}");
                    }
                }
            }
        }
    }
}
