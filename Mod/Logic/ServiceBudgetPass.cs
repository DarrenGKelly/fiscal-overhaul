namespace FiscalOverhaul.Logic
{
    /// <summary>Which service buildings <c>DistrictServiceBudgetSystem</c> sets the ServiceBudget efficiency of this frame.</summary>
    public enum ServiceBudgetPassKind
    {
        /// <summary>Nothing: no overrides apply and none were left on any building.</summary>
        None = 0,
        /// <summary>Only the buildings vanilla just (re)wrote at the city % (Created / Updated).</summary>
        Changed = 1,
        /// <summary>Every service building.</summary>
        Full = 2,
    }

    /// <summary>
    /// Plans the efficiency pass. Vanilla's CityServiceEfficiencySystem writes the city % to every building when a
    /// slider moves and to new / updated buildings otherwise; the mod's pass runs right after and puts the district %
    /// back. A full pass is needed when anything else could have changed a building's % (an override, a district, a
    /// switch, a load), when no pass has run since overrides started applying, and as a slow safety net; once overrides
    /// stop applying, one full pass restores the city values and then the system idles.
    /// </summary>
    public static class ServiceBudgetPass
    {
        /// <param name="applies"><see cref="FeatureGates.AppliesDistrictServiceBudgets"/> now.</param>
        /// <param name="appliedBefore">Whether the buildings may still carry override values (a pass with overrides ran and no restore since).</param>
        /// <param name="fullTrigger">An override, the city budget, a district or a switch changed, or a city just loaded.</param>
        /// <param name="safetyDue">The periodic safety pass is due.</param>
        public static ServiceBudgetPassKind Plan(bool applies, bool appliedBefore, bool fullTrigger, bool safetyDue)
        {
            if (!applies) return appliedBefore ? ServiceBudgetPassKind.Full : ServiceBudgetPassKind.None;
            if (!appliedBefore || fullTrigger || safetyDue) return ServiceBudgetPassKind.Full;
            return ServiceBudgetPassKind.Changed;
        }
    }
}
