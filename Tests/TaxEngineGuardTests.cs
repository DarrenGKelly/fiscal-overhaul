using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxEngineGuardTests
    {
        private static readonly string[] Known = { "1.6.0f1" };

        [Fact]
        public void KnownVersion_StructureOk_RunsTheEngine()
        {
            Assert.Equal(TaxEngineMode.Engine, TaxEngineGuardLogic.Decide("1.6.0f1", Known, structureOk: true, forceUntested: false, masterOn: true));
            Assert.Equal(TaxEngineWarning.None, TaxEngineGuardLogic.WarningFor(true, true, false, true));
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
        }

        [Fact]
        public void Defaults_SimulationChangesOn_UntestedVersionOff()
        {
            Assert.True(FeatureGates.SimulationChangesDefault);
            Assert.False(FeatureGates.RunOnUntestedGameVersionDefault);
            // With the defaults, an unknown version stays vanilla.
            Assert.Equal(TaxEngineMode.Blocked, TaxEngineGuardLogic.Decide("2.0.0f1", Known, true, FeatureGates.RunOnUntestedGameVersionDefault, FeatureGates.SimulationChangesDefault));
        }
    }
}
