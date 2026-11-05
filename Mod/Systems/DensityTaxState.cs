using FiscalOverhaul.Logic;
using Unity.Collections;

namespace FiscalOverhaul
{
    /// <summary>
    /// The live density tax offsets, shared by the UI system (writes) and the simulation systems (read).
    /// Kept as a managed table plus a persistent native copy of the effective offsets the Burst jobs read:
    /// the stored ones while density taxes are enabled (<see cref="Setting.EnableDensityTaxes"/>), all 0 while not.
    /// </summary>
    public static class DensityTaxState
    {
        private static DensityTaxTable s_Table = new DensityTaxTable();
        private static NativeArray<int> s_Native;
        private static bool s_Enabled = true;

        /// <summary>The stored offsets, whether or not density taxes are enabled.</summary>
        public static DensityTaxTable Table => s_Table;

        /// <summary>The Options switch. Off charges nothing but keeps <see cref="Table"/> as it is.</summary>
        public static bool Enabled => s_Enabled;

        /// <summary>Whether any building pays a density offset right now.</summary>
        public static bool IsCharged => s_Table.IsCharged(s_Enabled);

        /// <summary>Bumped on every change so UI bindings know to refresh.</summary>
        public static int Version { get; private set; }

        public static void Load(string serialized)
        {
            s_Table = DensityTaxTable.Parse(serialized);
            Sync();
        }

        public static void SetEnabled(bool enabled)
        {
            if (enabled == s_Enabled) return;
            s_Enabled = enabled;
            Sync();
        }

        public static int Set(TaxZone zone, Density density, int offset)
        {
            int stored = s_Table.Set(zone, density, offset);
            Sync();
            return stored;
        }

        /// <summary>The effective offsets as a native array (index = <see cref="DensityTaxTable.Index"/>). Valid until <see cref="Dispose"/>.</summary>
        public static NativeArray<int> Native
        {
            get
            {
                if (!s_Native.IsCreated) Sync();
                return s_Native;
            }
        }

        public static void Dispose()
        {
            if (s_Native.IsCreated) s_Native.Dispose();
        }

        private static void Sync()
        {
            int[] values = s_Table.EffectiveOffsets(s_Enabled);
            if (!s_Native.IsCreated)
            {
                s_Native = new NativeArray<int>(values.Length, Allocator.Persistent);
            }
            s_Native.CopyFrom(values);
            Version++;
        }
    }
}
