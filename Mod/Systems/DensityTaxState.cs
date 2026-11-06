using FiscalOverhaul.Logic;

namespace FiscalOverhaul
{
    /// <summary>
    /// The live density tax offsets, shared by the UI system and the Options page (write) and the simulation systems
    /// (read). Main thread only: every caller (binding triggers, the Options page, system OnUpdate) runs there.
    ///
    /// Jobs never see this state directly. <see cref="DensityTaxApplySystem"/> takes a <see cref="Snapshot"/> each
    /// update and hands its job a TempJob copy of it, so a change made here while an apply job is still running can't
    /// tear what that job reads. (A shared persistent NativeArray would need every writer here to first complete the
    /// apply job, and freeing it on mod unload could pull it out from under a running job.)
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

        public static void Load(string serialized)
        {
            s_Table = DensityTaxTable.Parse(serialized);
            Version++;
        }

        public static void SetEnabled(bool enabled)
        {
            if (enabled == s_Enabled) return;
            s_Enabled = enabled;
            Version++;
        }

        public static int Set(TaxZone zone, Density density, int offset)
        {
            int stored = s_Table.Set(zone, density, offset);
            Version++;
            return stored;
        }
    }
}
