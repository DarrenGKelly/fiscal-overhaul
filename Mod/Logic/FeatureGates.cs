namespace FiscalOverhaul.Logic
{
    /// <summary>When a feature's systems do their work, given its Options switch and the game's state.</summary>
    public static class FeatureGates
    {
        /// <summary>Default of the budget chart switch: on.</summary>
        public const bool BudgetChartDefault = true;

        /// <summary>Default of the density taxes switch: off, it is a BETA feature.</summary>
        public const bool DensityTaxesDefault = false;

        /// <summary>Default of the per-district budget switch: on (it only records while the budget chart is on too).</summary>
        public const bool DistrictBudgetDefault = true;

        /// <summary>Default of the "Simulation changes" master switch: on. Off turns every simulation change off (vanilla).</summary>
        public const bool SimulationChangesDefault = true;

        /// <summary>Default of "Run on untested game version": off, so an unknown game version gets vanilla simulation.</summary>
        public const bool RunOnUntestedGameVersionDefault = false;

        /// <summary>
        /// Whether DistrictBudgetSystem records a sample: the per-district view is part of the budget chart, so it
        /// records only while both switches are on, whether or not the panel is open.
        /// </summary>
        public static bool RecordsDistrictBudget(bool budgetChartEnabled, bool districtBudgetEnabled) => budgetChartEnabled && districtBudgetEnabled;

        /// <summary>
        /// Whether a UI system builds its Economy panel data this frame: only while its feature is enabled and the
        /// panel is open. A disabled feature's tab is vanilla, so there's nothing to build for it.
        /// </summary>
        public static bool BuildsPanelData(bool featureEnabled, bool economyPanelOpen) => featureEnabled && economyPanelOpen;
    }
}
