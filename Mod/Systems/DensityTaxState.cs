using FiscalOverhaul.Logic;

namespace FiscalOverhaul
{
    /// <summary>
    /// The live density tax offsets, shared by the UI system and the Options page (write) and the simulation systems
    /// (read). They are the loaded city's: <see cref="TaxRatesSystem"/> keeps them in the save and points this state at
    /// that save's <see cref="TaxRateTable"/> (<see cref="Use"/>) on every load. Main thread only: every caller (binding triggers, the Options page, system OnUpdate) runs there.
    ///
    /// Jobs never see this state directly. <see cref="TaxEngineSystem"/> takes a <see cref="Snapshot"/> each update and
    /// hands its jobs a TempJob copy of it, so a change made here while a charge job is still running can't tear what
    /// that job reads. (A shared persistent NativeArray would need every writer here to first complete the job, and
    /// freeing it on mod unload could pull it out from under a running job.)
    /// </summary>
    public static class DensityTaxState
    {
        private static DensityTaxTable s_Table = new DensityTaxTable();
        private static bool s_Enabled = FeatureGates.DensityTaxesDefault;

        /// <summary>The stored offsets, whether or not density taxes are enabled.</summary>
        public static DensityTaxTable Table => s_Table;

        /// <summary>The Options switch. Off charges nothing but keeps <see cref="Table"/> as it is.</summary>
        public static bool Enabled => s_Enabled;

        /// <summary>Whether any building pays a density offset right now.</summary>
        public static bool IsCharged => s_Table.IsCharged(s_Enabled);

        /// <summary>Bumped on every change so UI bindings know to refresh.</summary>
        public static int Version { get; private set; }

        /// <summary>
        /// A fresh copy of the offsets the tax systems charge (index = <see cref="DensityTaxTable.Index"/>): the stored
        /// ones while enabled, all 0 while not. Later changes don't touch an array already handed out.
        /// </summary>
        public static int[] Snapshot() => s_Table.EffectiveOffsets(s_Enabled);

        /// <summary>Reads and writes <paramref name="rates"/>' density entries from now on (the loaded city's table).</summary>
        public static void Use(TaxRateTable rates)
        {
            s_Table = new DensityTaxTable(rates);
            Version++;
        }

        /// <summary>Clears the loaded city's density tax rates, city and district overrides (Options > reset).</summary>
        public static void ClearAll()
        {
            s_Table.Rates.Clear(TaxType.Density);
            Version++;
        }

        public static void SetEnabled(bool enabled)
        {
            if (enabled == s_Enabled) return;
            s_Enabled = enabled;
            Version++;
        }

        /// <summary>
        /// Sets a district's value from what its slider shows (<see cref="TaxRateModel.SetDistrictValue"/>: clamped to
        /// vanilla's limits; the value it would have without the override removes it). Returns false for a bad name.
        /// </summary>
        public static bool SetDistrictValue(TaxType type, TaxZone zone, int key, int area, int areaVersion, int value, int[] cityRates, TaxLimits limits)
        {
            bool ok = TaxRateModel.SetDistrictValue(s_Table.Rates, type, zone, key, area, areaVersion, value, cityRates, limits);
            if (ok) Version++;
            return ok;
        }

        /// <summary>Removes every override of one district ("Use city rates"). Returns how many.</summary>
        public static int ClearDistrict(int area, int areaVersion)
        {
            int removed = s_Table.Rates.ClearArea(area, areaVersion);
            Version++;
            return removed;
        }
        public static int Set(TaxZone zone, Density density, int offset)
        {
            int stored = s_Table.Set(zone, density, offset);
            Version++;
            return stored;
        }
    }
}
