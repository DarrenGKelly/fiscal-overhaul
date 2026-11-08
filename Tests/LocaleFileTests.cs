using System;
using System.Collections.Generic;
using System.IO;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class LocaleFileTests
    {
        private static string L10nDir => Path.Combine(AppContext.BaseDirectory, "l10n");

        private static Dictionary<string, string> EnUs() => LocaleFile.Parse(File.ReadAllText(Path.Combine(L10nDir, "en-US.json")));

        [Fact]
        public void Parse_ReadsAFlatObjectOfStrings()
        {
            var d = LocaleFile.Parse("{ \"a\": \"one\", \"b.c\" : \"two\" }");
            Assert.Equal(2, d.Count);
            Assert.Equal("one", d["a"]);
            Assert.Equal("two", d["b.c"]);
        }

        [Fact]
        public void Parse_DecodesEscapesAndNonAscii()
        {
            var d = LocaleFile.Parse("﻿{\"k\": \"\\\"q\\\" \\\\ \\n \\u00e9 é ¢\"}");
            Assert.Equal("\"q\" \\ \n é é ¢", d["k"]);
        }

        [Fact]
        public void Parse_AcceptsAnEmptyObject()
        {
            Assert.Empty(LocaleFile.Parse(" {} "));
        }

        [Theory]
        [InlineData("{\"a\": \"one\",}")] // trailing comma
        [InlineData("{\"a\": 1}")] // not a string
        [InlineData("{\"a\": \"one\"")] // unclosed
        [InlineData("{\"a\": \"one\"} x")] // text after
        [InlineData("{\"a\": \"one\", \"a\": \"two\"}")] // duplicate
        [InlineData("{\"a\": \"bad \\q escape\"}")]
        [InlineData("[\"a\"]")]
        [InlineData("")]
        public void Parse_RejectsAnythingElse(string json)
        {
            Assert.Throws<FormatException>(() => LocaleFile.Parse(json));
        }

        [Theory]
        [InlineData("FiscalOverhaul.l10n.en-US.json", "en-US")]
        [InlineData("FiscalOverhaul.l10n.zh-HANS.json", "zh-HANS")]
        public void LocaleId_IsTheFileName(string resource, string locale)
        {
            Assert.Equal(locale, LocaleFile.LocaleIdFromResourceName(resource, "FiscalOverhaul.l10n."));
        }

        [Theory]
        [InlineData("FiscalOverhaul.Thumbnail.png")]
        [InlineData("FiscalOverhaul.l10n.en-US.txt")]
        [InlineData("FiscalOverhaul.l10n..json")]
        [InlineData("Other.l10n.en-US.json")]
        [InlineData(null)]
        public void LocaleId_IsNullForOtherResources(string resource)
        {
            Assert.Null(LocaleFile.LocaleIdFromResourceName(resource, "FiscalOverhaul.l10n."));
        }

        [Theory]
        [InlineData("FiscalOverhaul.Options.Title", OptionTextKind.Title, null)]
        [InlineData("FiscalOverhaul.Options.Tab.Main", OptionTextKind.Tab, "Main")]
        [InlineData("FiscalOverhaul.Options.Group.DensityTax", OptionTextKind.Group, "DensityTax")]
        [InlineData("FiscalOverhaul.Options.Label.EnableBudgetChart", OptionTextKind.Label, "EnableBudgetChart")]
        [InlineData("FiscalOverhaul.Options.Desc.EnableBudgetChart", OptionTextKind.Desc, "EnableBudgetChart")]
        [InlineData("FiscalOverhaul.Options.Warning.ResetDensityTaxes", OptionTextKind.Warning, "ResetDensityTaxes")]
        public void OptionKey_NamesWhatTheOptionsPageAsksFor(string key, OptionTextKind kind, string name)
        {
            Assert.True(LocaleFile.TryParseOptionKey(key, out var k, out var n));
            Assert.Equal(kind, k);
            Assert.Equal(name, n);
        }

        [Theory]
        [InlineData("FiscalOverhaul.UI.ByDensity")]
        [InlineData("FiscalOverhaul.Options.Tooltip.EnableBudgetChart")] // unknown kind
        [InlineData("FiscalOverhaul.Options.label.EnableBudgetChart")] // wrong case
        [InlineData("FiscalOverhaul.Options.1.EnableBudgetChart")] // a number is not a kind
        [InlineData("FiscalOverhaul.Options.Label")] // no option name
        [InlineData("FiscalOverhaul.Options.Title.Extra")]
        [InlineData("Options.SECTION[FiscalOverhaul]")]
        [InlineData(null)]
        public void OptionKey_RejectsOtherKeys(string key)
        {
            Assert.False(LocaleFile.TryParseOptionKey(key, out _, out _));
        }

        [Fact]
        public void UiKeys_GoToTheGameAsTheyAre()
        {
            Assert.True(LocaleFile.IsUiKey("FiscalOverhaul.UI.ByDensity"));
            Assert.False(LocaleFile.IsUiKey("FiscalOverhaul.UI."));
            Assert.False(LocaleFile.IsUiKey("EconomyPanel.MONTHLY_BALANCE"));
            Assert.False(LocaleFile.IsUiKey("FiscalOverhaul.Options.Title"));
        }

        [Fact]
        public void EveryLocaleFile_ParsesAndHoldsOnlyKeysTheModRegisters()
        {
            var files = Directory.GetFiles(L10nDir, "*.json");
            Assert.Contains(files, f => Path.GetFileName(f) == "en-US.json");
            foreach (var file in files)
            {
                var entries = LocaleFile.Parse(File.ReadAllText(file));
                foreach (var key in entries.Keys)
                    Assert.True(LocaleFile.IsUiKey(key) || LocaleFile.TryParseOptionKey(key, out _, out _), $"{Path.GetFileName(file)}: {key}");
            }
        }

        [Fact]
        public void EnglishOptions_ReadAsBefore()
        {
            // The text LocaleEN (Mod/Setting.cs) registered before the strings moved to l10n/en-US.json.
            var en = EnUs();
            Assert.Equal("Fiscal Overhaul", en["FiscalOverhaul.Options.Title"]);
            Assert.Equal("Main", en["FiscalOverhaul.Options.Tab.Main"]);
            Assert.Equal("Features", en["FiscalOverhaul.Options.Group.Features"]);
            Assert.Equal("Density taxes", en["FiscalOverhaul.Options.Group.DensityTax"]);
            Assert.Equal("Enable budget chart", en["FiscalOverhaul.Options.Label.EnableBudgetChart"]);
            Assert.Equal("Enable density taxes (BETA)", en["FiscalOverhaul.Options.Label.EnableDensityTaxes"]);
            Assert.Equal("Reset density tax offsets", en["FiscalOverhaul.Options.Label.ResetDensityTaxes"]);
            Assert.Equal("Reset all density tax offsets to 0?", en["FiscalOverhaul.Options.Warning.ResetDensityTaxes"]);
            Assert.Equal("Replaces the bar chart in Economy > Budget with a line chart of each revenue and expense over time. Off: the Budget tab is the vanilla one.",
                en["FiscalOverhaul.Options.Desc.EnableBudgetChart"]);
            Assert.Equal("A beta feature, off by default. Adds per-density tax offsets (set in Economy > Taxation) to what buildings pay; income only, demand and happiness are unchanged. Off: buildings pay the vanilla rates and the Taxation tab hides the density rows; your offsets are kept for when you switch it back on.",
                en["FiscalOverhaul.Options.Desc.EnableDensityTaxes"]);
            Assert.Equal("Sets every density offset (set in Economy > Taxation) back to 0, so buildings pay the vanilla rates again.",
                en["FiscalOverhaul.Options.Desc.ResetDensityTaxes"]);
            Assert.Equal("Record district budgets", en["FiscalOverhaul.Options.Label.EnableDistrictBudget"]);
            Assert.True(en.ContainsKey("FiscalOverhaul.Options.Desc.EnableDistrictBudget"));
            Assert.Equal("Simulation changes", en["FiscalOverhaul.Options.Label.EnableSimulationChanges"]);
            Assert.True(en.ContainsKey("FiscalOverhaul.Options.Desc.EnableSimulationChanges"));
            Assert.Equal("District service budgets (BETA)", en["FiscalOverhaul.Options.Label.EnableDistrictServiceBudget"]);
            Assert.True(en.ContainsKey("FiscalOverhaul.Options.Desc.EnableDistrictServiceBudget"));
            Assert.Equal("Reset district service budgets", en["FiscalOverhaul.Options.Label.ResetDistrictServiceBudgets"]);
            Assert.True(en.ContainsKey("FiscalOverhaul.Options.Desc.ResetDistrictServiceBudgets"));
            Assert.True(en.ContainsKey("FiscalOverhaul.Options.Warning.ResetDistrictServiceBudgets"));
            Assert.Equal(20, CountOptionKeys(en));
        }

        private static int CountOptionKeys(Dictionary<string, string> entries)
        {
            var n = 0;
            foreach (var key in entries.Keys)
                if (LocaleFile.TryParseOptionKey(key, out _, out _)) n++;
            return n;
        }
    }
}
