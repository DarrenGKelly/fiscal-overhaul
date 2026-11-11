using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxEngineGuardTests
    {
        private static readonly string[] Known = { "1.6.0f1", "1.6.3f1" }; // a fixed list: later versions stand in for unknown ones below

        [Fact]
        public void KnownVersion_StructureOk_RunsTheEngine()
        {
            Assert.Equal(TaxEngineMode.Engine, TaxEngineGuardLogic.Decide("1.6.0f1", Known, structureOk: true, forceUntested: false, masterOn: true));
            Assert.Equal(TaxEngineWarning.None, TaxEngineGuardLogic.WarningFor(true, true, false, true));
        }

        [Fact]
        public void GameVersion1_6_3f1_RunsTheEngine()
        {
            Assert.Equal(TaxEngineMode.Engine, TaxEngineGuardLogic.Decide("1.6.3f1", Known, structureOk: true, forceUntested: false, masterOn: true));
            Assert.Equal(TaxEngineMode.Engine, TaxEngineGuardLogic.Decide("1.6.3f1", TaxEngineGuardLogic.KnownGameVersions, structureOk: true, forceUntested: false, masterOn: true));
            Assert.Equal(TaxEngineWarning.None, TaxEngineGuardLogic.WarningFor(true, true, false, true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GameVersion1_6_5f1_IsUnknown_BlockedUnlessForced(bool force)
        {
            Assert.Equal(TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("1.6.5f1", Known, structureOk: true, forceUntested: false, masterOn: true));
            Assert.Equal(force ? TaxEngineMode.Engine : TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("1.6.5f1", TaxEngineGuardLogic.KnownGameVersions, structureOk: true, forceUntested: force, masterOn: true));
            Assert.Equal(force ? TaxEngineMode.Engine : TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("1.6.5f1", Known, structureOk: true, forceUntested: force, masterOn: true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StructureBad_IsVanilla_EvenForced(bool force)
        {
            Assert.Equal(TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("1.6.0f1", Known, structureOk: false, forceUntested: force, masterOn: true));
            Assert.Equal(TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("9.9.9f9", Known, structureOk: false, forceUntested: force, masterOn: true));
            Assert.Equal(TaxEngineWarning.Incompatible, TaxEngineGuardLogic.WarningFor(true, false, force, true));
        }

        [Fact]
        public void UnknownVersion_IsVanilla_UnlessForced()
        {
            Assert.Equal(TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("1.7.0f1", Known, structureOk: true, forceUntested: false, masterOn: true));
            Assert.Equal(TaxEngineMode.Engine, TaxEngineGuardLogic.Decide("1.7.0f1", Known, structureOk: true, forceUntested: true, masterOn: true));
            Assert.Equal(TaxEngineWarning.UntestedVersion, TaxEngineGuardLogic.WarningFor(false, true, false, true));
        }

        [Theory]
        [InlineData(false, false, true, true)]   // a new game version, option off: the notice shows
        [InlineData(false, true, true, false)]   // ticked Run on untested game version: hidden
        [InlineData(true, false, true, false)]   // a known version: hidden
        [InlineData(false, false, false, false)] // Simulation changes off: taxes are vanilla by choice, hidden
        public void OptionsNotice_ShowsOnlyOnAnUntestedVersion_WithTheOptionOff(bool known, bool force, bool master, bool shows)
        {
            Assert.Equal(shows, TaxEngineGuardLogic.ShowsUntestedVersionNotice(known, force, master));
        }

        [Fact]
        public void UnknownVersion_ForcedOn_ShowsNoWarning()
        {
            // The player turned Run on untested game version on, so the Taxation tab doesn't nag about it.
            Assert.Equal(TaxEngineWarning.None, TaxEngineGuardLogic.WarningFor(versionKnown: false, structureOk: true, forceUntested: true, masterOn: true));
            // Not forced: the untested warning stays.
            Assert.Equal(TaxEngineWarning.UntestedVersion, TaxEngineGuardLogic.WarningFor(versionKnown: false, structureOk: true, forceUntested: false, masterOn: true));
            // Forced but the structure check failed: still incompatible.
            Assert.Equal(TaxEngineWarning.Incompatible, TaxEngineGuardLogic.WarningFor(versionKnown: false, structureOk: false, forceUntested: true, masterOn: true));
        }

        [Theory]
        [InlineData("1.6.0f1", true)]
        [InlineData("9.9.9f9", true)]
        [InlineData("1.6.0f1", false)]
        public void MasterOff_IsVanilla_WithNoWarning(string version, bool structureOk)
        {
            Assert.Equal(TaxEngineMode.Vanilla, TaxEngineGuardLogic.Decide(version, Known, structureOk, forceUntested: true, masterOn: false));
            Assert.Equal(TaxEngineWarning.None, TaxEngineGuardLogic.WarningFor(version == "1.6.0f1", structureOk, true, false));
        }

        [Theory]
        [InlineData("1.6.0f1", true)]
        [InlineData("1.6.3f1", true)]
        [InlineData("1.6.4f1", false)]
        [InlineData(" 1.6.0f1 ", true)]
        [InlineData("1.6.0f1 (419.d6c6) [6216.19404]", true)]
        [InlineData("1.6.0f10", false)]
        [InlineData("1.6.1f1", false)]
        [InlineData("1.6.0", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsKnownVersion_MatchesTheFirstWordExactly(string version, bool known)
        {
            Assert.Equal(known, TaxEngineGuardLogic.IsKnownVersion(version, Known));
        }

        [Fact]
        public void TheKnownList_HasTheVersionTheCopyCameFrom()
        {
            Assert.Contains("1.6.0f1", TaxEngineGuardLogic.KnownGameVersions);
            Assert.Contains("1.6.3f1", TaxEngineGuardLogic.KnownGameVersions);
            Assert.DoesNotContain("1.6.5f1", TaxEngineGuardLogic.KnownGameVersions);
        }

        [Fact]
        public void TheKnownList_Has1_6_4f1_TheNovember2026Update()
        {
            // 1.6.4f1's TaxSystem, CitizenHappinessSystem, HouseholdFindPropertySystem and the PropertyUtils parts the
            // copies use are unchanged from 1.6.3f1, so the engine runs on it without the untested warning.
            Assert.True(TaxEngineGuardLogic.IsKnownVersion("1.6.4f1", TaxEngineGuardLogic.KnownGameVersions));
            Assert.Equal(TaxEngineMode.Engine, TaxEngineGuardLogic.Decide("1.6.4f1", TaxEngineGuardLogic.KnownGameVersions, structureOk: true, forceUntested: false, masterOn: true));
            Assert.False(TaxEngineGuardLogic.IsKnownVersion("1.6.4f2", TaxEngineGuardLogic.KnownGameVersions));
        }

        [Fact]
        public void Defaults_SimulationChangesOn_UntestedVersionOff()
        {
            Assert.True(FeatureGates.SimulationChangesDefault);
            Assert.False(FeatureGates.RunOnUntestedGameVersionDefault);
            // With the defaults, an unknown version stays vanilla.
            Assert.Equal(TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("2.0.0f1", Known, true, FeatureGates.RunOnUntestedGameVersionDefault, FeatureGates.SimulationChangesDefault));
        }

        [Fact]
        public void Happiness_RunsOnlyWithTheEngine()
        {
            Assert.True(TaxEngineGuardLogic.RunsHappiness(TaxEngineMode.Engine, happinessStructureOk: true, foreignDisabled: false));
            Assert.False(TaxEngineGuardLogic.RunsHappiness(TaxEngineMode.Vanilla, true, false));
            Assert.False(TaxEngineGuardLogic.RunsHappiness(TaxEngineMode.Blocked, true, false));
        }

        [Fact]
        public void Happiness_StructureBad_IsVanilla()
        {
            Assert.False(TaxEngineGuardLogic.RunsHappiness(TaxEngineMode.Engine, happinessStructureOk: false, foreignDisabled: false));
        }

        [Fact]
        public void Happiness_AnotherModDisabledVanilla_BacksOff()
        {
            Assert.False(TaxEngineGuardLogic.RunsHappiness(TaxEngineMode.Engine, happinessStructureOk: true, foreignDisabled: true));
        }

        [Fact]
        public void HomeChoice_RunsExactlyWhileTheEngineCharges()
        {
            Assert.True(TaxEngineGuardLogic.RunsHomeChoice(TaxEngineMode.Engine, foreignDisabled: false));
            Assert.False(TaxEngineGuardLogic.RunsHomeChoice(TaxEngineMode.Vanilla, false));
            Assert.False(TaxEngineGuardLogic.RunsHomeChoice(TaxEngineMode.Blocked, false));
        }

        [Fact]
        public void HomeChoice_AnotherModDisabledVanilla_BacksOff()
        {
            Assert.False(TaxEngineGuardLogic.RunsHomeChoice(TaxEngineMode.Engine, foreignDisabled: true));
            Assert.True(TaxEngineGuardLogic.RunsHomeChoice(TaxEngineMode.Engine, foreignDisabled: false));
        }
    }
}
