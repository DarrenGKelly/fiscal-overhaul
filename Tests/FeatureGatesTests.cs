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

        [Fact]
        public void DistrictBudget_IsOnByDefault()
        {
            Assert.True(FeatureGates.DistrictBudgetDefault);
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]  // district budget switched off
        [InlineData(false, true, false)]  // budget chart switched off: the district view is part of it
        [InlineData(false, false, false)]
        public void RecordsDistrictBudget_OnlyWithBothOn(bool budgetChart, bool districtBudget, bool expected)
        {
            Assert.Equal(expected, FeatureGates.RecordsDistrictBudget(budgetChart, districtBudget));
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
