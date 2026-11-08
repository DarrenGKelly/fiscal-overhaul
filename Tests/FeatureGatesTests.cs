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
        [InlineData(true, true)]   // chart on: the city's per-service upkeep split records
        [InlineData(false, false)] // chart off: nothing beyond vanilla is recorded
        public void RecordsServiceUpkeep_OnlyWithTheChartOn(bool budgetChart, bool expected)
        {
            Assert.Equal(expected, FeatureGates.RecordsServiceUpkeep(budgetChart));
        }

        [Theory]
        [InlineData(false, true)]  // switched off in Options: the tab is vanilla
        [InlineData(true, false)]  // panel closed
        [InlineData(false, false)]
        public void BuildsNothing_WhenDisabledOrThePanelIsClosed(bool featureEnabled, bool economyPanelOpen)
        {
            Assert.False(FeatureGates.BuildsPanelData(featureEnabled, economyPanelOpen));
        }

        [Fact]
        public void SimulationChanges_IsOnByDefault_DistrictServiceBudgets_OffAsBeta()
        {
            Assert.True(FeatureGates.SimulationChangesDefault);
            Assert.False(FeatureGates.DistrictServiceBudgetDefault);
        }

        [Theory]
        [InlineData(true, true, true, true)]
        [InlineData(false, true, true, false)]  // master "Simulation changes" off: vanilla
        [InlineData(true, false, true, false)]  // feature off
        [InlineData(true, true, false, false)]  // no override stored: nothing to do
        [InlineData(false, false, false, false)]
        public void AppliesDistrictServiceBudgets_OnlyWithMasterFeatureAndAnOverride(bool master, bool feature, bool hasAny, bool expected)
        {
            Assert.Equal(expected, FeatureGates.AppliesDistrictServiceBudgets(master, feature, hasAny));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(false, true, false)]  // master off: density taxes run vanilla too
        [InlineData(true, false, false)]
        public void AppliesDensityTaxes_NeedsTheMasterSwitch(bool master, bool feature, bool expected)
        {
            Assert.Equal(expected, FeatureGates.AppliesDensityTaxes(master, feature));
        }

        [Fact]
        public void MasterOff_NothingApplies()
        {
            Assert.False(FeatureGates.AppliesDistrictServiceBudgets(simulationChanges: false, featureEnabled: true, hasAnyOverride: true));
            Assert.False(FeatureGates.AppliesDensityTaxes(simulationChanges: false, densityTaxesEnabled: true));
        }

        [Theory]
        [InlineData(true, false)]  // master on: the switches under it are live
        [InlineData(false, true)]  // master off: greyed out, they'd have no effect
        public void DisablesSimulationSwitches_ExactlyWhileTheMasterIsOff(bool master, bool expected)
        {
            Assert.Equal(expected, FeatureGates.DisablesSimulationSwitches(master));
        }

        // Every switch under the master: on with the master on, off with the master off (sub on in both).

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void Master_GatesDensityTaxes(bool master, bool expected)
        {
            Assert.Equal(expected, FeatureGates.AppliesDensityTaxes(master, densityTaxesEnabled: true));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void Master_GatesDistrictServiceBudgets_TheOverridesTheSelectorAndTheFees(bool master, bool expected)
        {
            Assert.Equal(expected, FeatureGates.AppliesDistrictServiceBudgets(master, featureEnabled: true, hasAnyOverride: true));
            Assert.Equal(expected, FeatureGates.ShowsDistrictServiceBudgets(master, featureEnabled: true));
            Assert.Equal(expected, FeatureGates.BuildsDistrictServiceFees(master, featureEnabled: true, economyPanelOpen: true));
        }

        [Theory]
        [InlineData(true, TaxEngineMode.Engine)]
        [InlineData(false, TaxEngineMode.Vanilla)]
        public void Master_GatesRunOnUntestedGameVersion(bool master, TaxEngineMode expected)
        {
            // An untested version with Run on untested on: the engine runs only under the master.
            Assert.Equal(expected, TaxEngineGuardLogic.Decide(versionKnown: false, structureOk: true, forceUntested: true, masterOn: master));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void Master_GatesTheTaxEngine_DistrictRatesHappinessHomeChoiceAndDemand(bool master, bool expected)
        {
            // District tax overrides charge only in Engine mode; happiness, home choice and demand follow that mode.
            var mode = TaxEngineGuardLogic.Decide(versionKnown: true, structureOk: true, forceUntested: false, masterOn: master);
            Assert.Equal(expected, mode == TaxEngineMode.Engine);
            Assert.Equal(expected, TaxEngineGuardLogic.RunsHappiness(mode, happinessStructureOk: true, foreignDisabled: false));
            Assert.Equal(expected, TaxEngineGuardLogic.RunsHomeChoice(mode, foreignDisabled: false));
            Assert.Equal(expected, DemandSwapLogic.UseView(structureOk: true, engineCharging: mode == TaxEngineMode.Engine, faulted: false));
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        public void TheCoreFeatures_DontTakeTheMaster_TheirOwnSwitchDecides(bool budgetChart, bool expected)
        {
            // Budget chart and district recording read the game only: no master parameter, their own switches decide.
            Assert.Equal(expected, FeatureGates.RecordsDistrictBudget(budgetChart, districtBudgetEnabled: true));
            Assert.Equal(expected, FeatureGates.RecordsServiceUpkeep(budgetChart));
            Assert.Equal(expected, FeatureGates.BuildsPanelData(budgetChart, economyPanelOpen: true));
        }

        [Fact]
        public void TheServicesTable_IsBuiltWheneverThePanelIsOpen_FeatureOrNot()
        {
            Assert.True(FeatureGates.BuildsServiceTable(economyPanelOpen: true));
            Assert.False(FeatureGates.BuildsServiceTable(economyPanelOpen: false));
        }

        [Theory]
        [InlineData(true, true, true, true)]    // district view offered: its fee shares are built
        [InlineData(true, false, true, false)]  // feature off: the table is city-only, no shares
        [InlineData(false, true, true, false)]  // master "Simulation changes" off: city-only too
        [InlineData(true, true, false, false)]  // panel closed
        public void DistrictServiceFees_AreBuiltOnlyForTheDistrictView(bool master, bool feature, bool open, bool expected)
        {
            Assert.Equal(expected, FeatureGates.BuildsDistrictServiceFees(master, feature, open));
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(false, true, false)]  // master "Simulation changes" off: the Services table, city-only
        [InlineData(true, false, false)]  // feature off: the Services table, city-only
        [InlineData(false, false, false)]
        public void ShowsDistrictServiceBudgets_NeedsMasterAndFeature(bool master, bool feature, bool expected)
        {
            Assert.Equal(expected, FeatureGates.ShowsDistrictServiceBudgets(master, feature));
        }

        [Theory]
        [InlineData(true, false, false, true)]   // Budget tab's district view only (the list used to be gated on this alone)
        [InlineData(false, true, false, true)]   // Services tab only: recording off, the list is still published
        [InlineData(false, false, true, true)]   // the tax engine only: the Taxation tab's district selector needs it
        [InlineData(true, true, true, true)]
        [InlineData(false, false, false, false)] // no tab shows a selector: nothing to publish
        public void PublishesDistrictList_WhenAnyDistrictFeatureIsOn(bool recording, bool services, bool taxEngine, bool expected)
        {
            Assert.Equal(expected, FeatureGates.PublishesDistrictList(recording, services, taxEngine));
        }
    }
}
