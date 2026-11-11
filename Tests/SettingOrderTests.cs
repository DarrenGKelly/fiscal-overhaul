using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FiscalOverhaul.Tests
{
    /// <summary>
    /// Setting.cs targets net48 and needs the game's DLLs, so it can't be compiled here. The game lists each group's
    /// options in property declaration order, so this reads the source text and checks the Options layout: the core
    /// Features, then Simulation changes (the master, and every switch under it greyed out while it is off), then Reset.
    /// </summary>
    public class SettingOrderTests
    {
        private static string SettingSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mod", "Setting.cs")))
            {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, "Mod", "Setting.cs"));
        }

        /// <summary>One shown option: its name, its group constant and whether it greys out with the master.</summary>
        private sealed class Option
        {
            public string Name;
            public string Group;
            public bool DisabledByMaster;
        }

        /// <summary>
        /// The options as the game lists them: every <c>public bool</c> property, in declaration order, with the
        /// attributes written between it and the previous member's declaration.
        /// </summary>
        private static List<Option> Options(string src)
        {
            var options = new List<Option>();
            var props = Regex.Matches(src, @"public bool (\w+)\s*(\{|$)", RegexOptions.Multiline);
            int from = 0;
            foreach (Match m in props)
            {
                string attrs = src.Substring(from, m.Index - from);
                from = m.Index + m.Length;
                // The last section attribute before the property is its own (a non-bool option, such as the notice,
                // can sit in between).
                var sections = Regex.Matches(attrs, @"SettingsUISection\(kSection, (\w+)\)");
                if (sections.Count == 0) continue; // not shown (or a method)
                var section = sections[sections.Count - 1];
                options.Add(new Option
                {
                    Name = m.Groups[1].Value,
                    Group = section.Groups[1].Value,
                    DisabledByMaster = attrs.Contains("[SettingsUIDisableByCondition(typeof(Setting), nameof(SimulationSwitchesDisabled))]"),
                });
            }
            return options;
        }

        private static string[] Names(List<Option> options, string group)
            => options.Where(o => o.Group == group).Select(o => o.Name).ToArray();

        [Fact]
        public void TheGroups_AreTheNotice_ThenFeatures_ThenSimulationChanges_ThenReset()
        {
            var src = SettingSource();
            Assert.Contains("[SettingsUIGroupOrder(kNoticeGroup, kFeaturesGroup, kSimulationGroup, kResetGroup)]", src);
            Assert.Contains("[SettingsUIShowGroupName(kFeaturesGroup, kSimulationGroup, kResetGroup)]", src);
        }

        [Fact]
        public void Features_HoldsOnlyTheCore_BudgetChartThenDistrictBudgetThenServicesTable()
        {
            var options = Options(SettingSource());
            Assert.Equal(new[] { "EnableBudgetChart", "EnableDistrictBudget", "EnableServicesTable" }, Names(options, "kFeaturesGroup"));
            Assert.DoesNotContain(options, o => o.Group == "kFeaturesGroup" && o.DisabledByMaster);
        }

        [Fact]
        public void SimulationChanges_HasTheMasterFirst_ThenEverySwitchThatChangesTheSimulation()
        {
            var options = Options(SettingSource());
            Assert.Equal(new[] { "EnableSimulationChanges", "EnableDensityTaxes", "EnableDistrictServiceBudget", "RunOnUntestedGameVersion" },
                Names(options, "kSimulationGroup"));
        }

        [Fact]
        public void EverySwitchUnderTheMaster_GreysOutWithIt_AndTheMasterItselfDoesNot()
        {
            var sim = Options(SettingSource()).Where(o => o.Group == "kSimulationGroup").ToList();
            Assert.False(sim[0].DisabledByMaster, "The master must stay clickable");
            foreach (var o in sim.Skip(1))
                Assert.True(o.DisabledByMaster, $"{o.Name} must carry SettingsUIDisableByCondition(SimulationSwitchesDisabled)");
        }

        [Fact]
        public void TheResets_SitInTheResetGroup_AlwaysAvailable()
        {
            var options = Options(SettingSource());
            Assert.Equal(new[] { "ResetDistrictServiceBudgets", "ResetDensityTaxes" }, Names(options, "kResetGroup"));
            Assert.DoesNotContain(options, o => o.Group == "kResetGroup" && o.DisabledByMaster);
        }

        [Fact]
        public void TheUntestedVersionNotice_TopsThePage_AsAWarningText_HiddenUnlessItApplies()
        {
            var src = SettingSource();
            // Its own group, first in the order (checked above), with no group name shown.
            Assert.DoesNotContain("SettingsUIShowGroupName(kNoticeGroup", src);
            int notice = src.IndexOf("public string UntestedVersionNotice", StringComparison.Ordinal);
            int firstSwitch = src.IndexOf("public bool EnableBudgetChart", StringComparison.Ordinal);
            Assert.True(notice > 0 && notice < firstSwitch, "The notice must be declared before every switch");
            int start = src.LastIndexOf("// ---- Notice", notice, StringComparison.Ordinal);
            string attrs = src.Substring(start, notice - start);
            Assert.Contains("[SettingsUISection(kSection, kNoticeGroup)]", attrs);
            Assert.Contains("[SettingsUIMultilineText(\"Media/Misc/Warning.svg\")]", attrs);
            Assert.Contains("[SettingsUIHideByCondition(typeof(Setting), nameof(HidesUntestedVersionNotice))]", attrs);
            // Read-only, so it isn't written to the settings file; the condition is a method, so it isn't an option.
            Assert.Contains("public string UntestedVersionNotice => string.Empty;", src);
            Assert.Contains("public bool HidesUntestedVersionNotice()", src);
            Assert.Contains("!TaxEngineGuardLogic.ShowsUntestedVersionNotice(GameVersionKnown(), RunOnUntestedGameVersion, EnableSimulationChanges)", src);
            // The parser doesn't take the notice for a switch, nor put the first switch in its group.
            Assert.DoesNotContain(Options(src), o => o.Group == "kNoticeGroup");
        }

        [Fact]
        public void TheConditionMethod_IsAMethodReadingTheMaster()
        {
            // A method (not a property), so the game doesn't list it as an option.
            Assert.Contains("public bool SimulationSwitchesDisabled() => FeatureGates.DisablesSimulationSwitches(EnableSimulationChanges);", SettingSource());
        }

        [Fact]
        public void Options_ReadsGroupsAndConditions_FromASampleSource()
        {
            // The parser itself: a pass (shown, greyed) and a fail (unlisted, not greyed) case.
            const string sample = @"
        [SettingsUISection(kSection, kA)]
        public bool One { get; set; }
        [SettingsUISection(kSection, kB)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(SimulationSwitchesDisabled))]
        public bool Two { get; set; }
        public bool Hidden { get; set; }
        public bool Check() => true;
        [SettingsUISection(kSection, kN)]
        [SettingsUIMultilineText]
        public string Note => string.Empty;
        [SettingsUISection(kSection, kC)]
        public bool Three { get; set; }";
            var options = Options(sample);
            Assert.Equal(new[] { "One", "Two", "Three" }, options.Select(o => o.Name).ToArray());
            Assert.Equal("kC", options[2].Group); // its own section, not the text option's before it
            Assert.False(options[0].DisabledByMaster);
            Assert.True(options[1].DisabledByMaster);
            Assert.Equal("kB", options[1].Group);
        }
    }
}
