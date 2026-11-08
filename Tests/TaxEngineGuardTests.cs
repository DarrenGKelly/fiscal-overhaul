using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxEngineGuardTests
    {
        private static readonly string[] Known = { "1.6.0f1", "1.6.3f1" };

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
        public void GameVersion1_6_4f1_IsUnknown_BlockedUnlessForced(bool force)
        {
            Assert.Equal(TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("1.6.4f1", Known, structureOk: true, forceUntested: false, masterOn: true));
            Assert.Equal(force ? TaxEngineMode.Engine : TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("1.6.4f1", TaxEngineGuardLogic.KnownGameVersions, structureOk: true, forceUntested: force, masterOn: true));
            Assert.Equal(force ? TaxEngineMode.Engine : TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("1.6.4f1", Known, structureOk: true, forceUntested: force, masterOn: true));
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
            Assert.Equal(TaxEngineWarning.UntestedVersionForced, TaxEngineGuardLogic.WarningFor(false, true, true, true));
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
            Assert.DoesNotContain("1.6.4f1", TaxEngineGuardLogic.KnownGameVersions);
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
    }
}
