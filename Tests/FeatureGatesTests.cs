using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class FeatureGatesTests
    {
        [Fact]
        public void DensityTaxes_AreOffByDefault_AsABetaFeature()
        {
            Assert.False(FeatureGates.DensityTaxesDefault);
        }

        [Fact]
        public void BudgetChart_IsOnByDefault()
        {
            Assert.True(FeatureGates.BudgetChartDefault);
        }

        [Fact]
        public void DefaultsDecideWhetherPanelDataIsBuilt()
        {
            Assert.False(FeatureGates.BuildsPanelData(FeatureGates.DensityTaxesDefault, economyPanelOpen: true));
            Assert.True(FeatureGates.BuildsPanelData(FeatureGates.BudgetChartDefault, economyPanelOpen: true));
        }

        [Fact]
        public void BuildsPanelData_WhenEnabledAndThePanelIsOpen()
        {
            Assert.True(FeatureGates.BuildsPanelData(featureEnabled: true, economyPanelOpen: true));
        }

        [Theory]
        [InlineData(false, true)]  // switched off in Options: the tab is vanilla
        [InlineData(true, false)]  // panel closed
        [InlineData(false, false)]
        public void BuildsNothing_WhenDisabledOrThePanelIsClosed(bool featureEnabled, bool economyPanelOpen)
        {
            Assert.False(FeatureGates.BuildsPanelData(featureEnabled, economyPanelOpen));
        }
    }
}
