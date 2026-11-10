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

        /// <summary>Default of the Services table switch (the Services tab's UI changes): on. Off: the vanilla Services tab.</summary>
        public const bool ServicesTableDefault = true;

        /// <summary>Default of "Run on untested game version": off, so an unknown game version gets vanilla simulation.</summary>
        public const bool RunOnUntestedGameVersionDefault = false;

        /// <summary>
        /// Default of the master "Simulation changes" switch: on. Off, every feature that changes how the vanilla
        /// simulation behaves (demand, happiness, service efficiency and upkeep, taxes charged: density taxes, district
        /// service budgets and the tax engine) runs vanilla; their stored values are kept. Every such feature's
        /// gate takes this switch first.
        /// </summary>
        public const bool SimulationChangesDefault = true;

        /// <summary>Default of the per-district service budget switch: off, it is a BETA feature.</summary>
        public const bool DistrictServiceBudgetDefault = false;

        /// <summary>
        /// Whether Options greys out the switches under the master (density taxes, district service budgets, run on
        /// untested game version): while simulation changes are off, when none of them has any effect.
        /// </summary>
        public static bool DisablesSimulationSwitches(bool simulationChanges) => !simulationChanges;

        /// <summary>Whether density tax offsets are charged: the feature on and simulation changes allowed.</summary>
        public static bool AppliesDensityTaxes(bool simulationChanges, bool densityTaxesEnabled) => simulationChanges && densityTaxesEnabled;

        /// <summary>
        /// Whether per-district service budget overrides change efficiency and upkeep: simulation changes allowed, the
        /// feature on, and at least one override stored. Otherwise every building runs at the city slider (vanilla).
        /// </summary>
        public static bool AppliesDistrictServiceBudgets(bool simulationChanges, bool featureEnabled, bool hasAnyOverride)
            => simulationChanges && featureEnabled && hasAnyOverride;

        /// <summary>
        /// Whether the Services tab shows the district selector and per-district budget sliders: simulation changes
        /// allowed and the feature on. Off, the Services table is city-only (the overrides don't apply either).
        /// </summary>
        public static bool ShowsDistrictServiceBudgets(bool simulationChanges, bool featureEnabled) => simulationChanges && featureEnabled;

        /// <summary>
        /// Whether the Services table's data (vanilla's default fees) is built: the table on (Options > Enable services
        /// table) and the Economy panel open, District service budgets or not. Off, the Services tab is vanilla's.
        /// </summary>
        public static bool BuildsServiceTable(bool servicesTableEnabled, bool economyPanelOpen) => servicesTableEnabled && economyPanelOpen;

        /// <summary>
        /// Whether the Services table's district fee shares are built: the table built (<see cref="BuildsServiceTable"/>)
        /// and the district view offered (<see cref="ShowsDistrictServiceBudgets"/>). Otherwise none are needed.
        /// </summary>
        public static bool BuildsDistrictServiceFees(bool simulationChanges, bool featureEnabled, bool servicesTableEnabled, bool economyPanelOpen)
            => BuildsServiceTable(servicesTableEnabled, economyPanelOpen) && ShowsDistrictServiceBudgets(simulationChanges, featureEnabled);

        /// <summary>
        /// Whether the district list (the selector's choices) is published: any tab that shows the selector needs it —
        /// district budget recording (the Budget tab), district service budgets (the Services tab) or the tax engine
        /// charging (the Taxation tab's per-district rates).
        /// </summary>
        public static bool PublishesDistrictList(bool recordsDistrictBudget, bool showsDistrictServiceBudgets, bool taxEngineCharges)
            => recordsDistrictBudget || showsDistrictServiceBudgets || taxEngineCharges;

        /// <summary>
        /// Whether DistrictBudgetSystem records a sample: the per-district view is part of the budget chart, so it
        /// records only while both switches are on, whether or not the panel is open.
        /// </summary>
        public static bool RecordsDistrictBudget(bool budgetChartEnabled, bool districtBudgetEnabled) => budgetChartEnabled && districtBudgetEnabled;

        /// <summary>
        /// Whether ServiceUpkeepSystem records the city's per-service upkeep split: it is part of the budget chart, so
        /// it records only while that switch is on (as district recording), whether or not the panel is open.
        /// </summary>
        public static bool RecordsServiceUpkeep(bool budgetChartEnabled) => budgetChartEnabled;

        /// <summary>
        /// Whether a UI system builds its Economy panel data this frame: only while its feature is enabled and the
        /// panel is open. A disabled feature's tab is vanilla, so there's nothing to build for it.
        /// </summary>
        public static bool BuildsPanelData(bool featureEnabled, bool economyPanelOpen) => featureEnabled && economyPanelOpen;
    }
}
