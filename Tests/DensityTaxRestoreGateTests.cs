using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DensityTaxRestoreGateTests
    {
        [Theory]
        [InlineData(false, false)] // density taxes off (the default)
        [InlineData(true, false)]  // on, every offset 0
        [InlineData(true, true)]   // on with an offset, but no payers this frame: apply scheduled nothing
        [InlineData(false, true)]
        public void NoRestore_WhenNothingWasApplied(bool enabled, bool anyNonzeroOffset)
        {
            Assert.False(DensityTaxRestoreGate.RestoreNeeded(enabled, anyNonzeroOffset, appliedThisTick: false));
        }

        [Fact]
        public void Restores_WhenApplyRan()
        {
            Assert.True(DensityTaxRestoreGate.RestoreNeeded(enabled: true, anyNonzeroOffset: true, appliedThisTick: true));
        }

        [Theory]
        [InlineData(false, true)]  // switched off after the apply
        [InlineData(true, false)]  // offsets reset to 0 after the apply
        [InlineData(false, false)]
        public void StillRestores_WhenTheSwitchOrOffsetsChangeAfterAnApply(bool enabled, bool anyNonzeroOffset)
        {
            Assert.True(DensityTaxRestoreGate.RestoreNeeded(enabled, anyNonzeroOffset, appliedThisTick: true));
        }

        [Fact]
        public void TheDefaultSwitch_SchedulesNoRestore()
        {
            // Density taxes off by default: the apply system returns before scheduling, so the restore must not run.
            bool applied = new DensityTaxTable().IsCharged(FeatureGates.DensityTaxesDefault);
            Assert.False(DensityTaxRestoreGate.RestoreNeeded(FeatureGates.DensityTaxesDefault, anyNonzeroOffset: false, applied));
        }
    }
}
