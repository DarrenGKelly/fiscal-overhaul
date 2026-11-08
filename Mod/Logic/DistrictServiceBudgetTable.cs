using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// Per-district service budget overrides: an absolute budget % (<see cref="Min"/>-<see cref="Max"/>) per
    /// (service, area). A building whose (service, area) has no override runs at the city slider's %. Pure data:
    /// <c>DistrictServiceBudgetSystem</c> keeps one with <c>Entity</c> areas and copies it into a native map for its jobs.
    ///
    /// A service is its prefab name (stable across saves, unlike its entity). An area is a district, or the
    /// <c>outside</c> value given to the constructor (buildings in no district); <see cref="Entry.IsOutside"/> keeps the
    /// two apart in a save, so a district deleted before saving (which loads as the "null" district) is dropped rather
    /// than becoming an Outside Districts override.
    /// </summary>
    public sealed class DistrictServiceBudgetTable<TDistrict> where TDistrict : IEquatable<TDistrict>
    {
        /// <summary>The vanilla slider's range.</summary>
        public const int Min = 50;
        public const int Max = 150;

        /// <summary>The save format this build writes; any other version loads as an empty table.</summary>
        public const int FormatVersion = 1;

        /// <summary>One override as saved.</summary>
        public struct Entry
        {
            public string Service;
            public bool IsOutside;
            public TDistrict District;
            public int Budget;
        }

        private readonly Dictionary<(string, TDistrict), int> m_Budgets = new Dictionary<(string, TDistrict), int>();
        private readonly TDistrict m_Outside;

        public DistrictServiceBudgetTable(TDistrict outside)
        {
            m_Outside = outside;
        }

        /// <summary>The area value that means "in no district".</summary>
        public TDistrict Outside => m_Outside;

        /// <summary>
        /// Decides whether a service key names a real service; <see cref="Set"/> stores nothing for one that does not.
        /// Null (the default) accepts every key.
        /// </summary>
        public Func<string, bool> IsKnownService { get; set; }

        /// <summary>Bumped on every change, so the systems know to rebuild what they derived from it.</summary>
        public int Revision { get; private set; }

        public bool HasAny => m_Budgets.Count > 0;

        public int Count => m_Budgets.Count;

        /// <summary>Clamps a requested % to the slider's range; 0 (or less) means "clear the override".</summary>
        public static int Clamp(int budget) => budget <= 0 ? 0 : Math.Min(Max, Math.Max(Min, budget));

        /// <summary>Sets an override (clamped to <see cref="Min"/>-<see cref="Max"/>); 0 clears it. Returns what is stored (0 = none).</summary>
        public int Set(string service, TDistrict area, int budget)
        {
            if (string.IsNullOrEmpty(service)) return 0;
            if (IsKnownService != null && !IsKnownService(service)) return 0;
            int stored = Clamp(budget);
            var key = (service, area);
            if (stored == 0)
            {
                if (m_Budgets.Remove(key)) Revision++;
                return 0;
            }
            if (!m_Budgets.TryGetValue(key, out int old) || old != stored)
            {
                m_Budgets[key] = stored;
                Revision++;
            }
            return stored;
        }

        public bool TryGet(string service, TDistrict area, out int budget)
        {
            budget = 0;
            return !string.IsNullOrEmpty(service) && m_Budgets.TryGetValue((service, area), out budget);
        }

        /// <summary>The % a building of <paramref name="service"/> in <paramref name="area"/> runs at: its override, else the city's.</summary>
        public int Effective(string service, TDistrict area, int cityBudget) => TryGet(service, area, out int budget) ? budget : cityBudget;

        /// <summary>Drops the overrides of districts that no longer exist (Outside Districts always exists). Returns how many went.</summary>
        public int Prune(Func<TDistrict, bool> districtExists)
        {
            var gone = new List<(string, TDistrict)>();
            foreach (KeyValuePair<(string, TDistrict), int> pair in m_Budgets)
            {
                TDistrict area = pair.Key.Item2;
                if (!area.Equals(m_Outside) && !districtExists(area)) gone.Add(pair.Key);
            }
            foreach ((string, TDistrict) key in gone) m_Budgets.Remove(key);
            if (gone.Count > 0) Revision++;
            return gone.Count;
        }

        /// <summary>Drops the overrides whose service key is not a known service. Returns how many went.</summary>
        public int PruneServices(Func<string, bool> isKnown)
        {
            var gone = new List<(string, TDistrict)>();
            foreach (KeyValuePair<(string, TDistrict), int> pair in m_Budgets)
                if (!isKnown(pair.Key.Item1)) gone.Add(pair.Key);
            foreach ((string, TDistrict) key in gone) m_Budgets.Remove(key);
            if (gone.Count > 0) Revision++;
            return gone.Count;
        }

        public void Clear()
        {
            if (m_Budgets.Count == 0) return;
            m_Budgets.Clear();
            Revision++;
        }

        /// <summary>Every override, for a save or for building the native map.</summary>
        public List<Entry> ToEntries()
        {
            var entries = new List<Entry>(m_Budgets.Count);
            foreach (KeyValuePair<(string, TDistrict), int> pair in m_Budgets)
            {
                bool outside = pair.Key.Item2.Equals(m_Outside);
                entries.Add(new Entry { Service = pair.Key.Item1, IsOutside = outside, District = outside ? m_Outside : pair.Key.Item2, Budget = pair.Value });
            }
            return entries;
        }

        /// <summary>
        /// Replaces the table with saved entries of format <paramref name="version"/>. An unknown version loads empty.
        /// A district entry whose district came back as the outside value (deleted before the save) is dropped, and
        /// out-of-range budgets are clamped as <see cref="Set"/> would.
        /// </summary>
        public void Load(int version, IEnumerable<Entry> entries)
        {
            m_Budgets.Clear();
            Revision++;
            if (version != FormatVersion || entries == null) return;
            foreach (Entry entry in entries)
            {
                if (!entry.IsOutside && entry.District.Equals(m_Outside)) continue;
                Set(entry.Service, entry.IsOutside ? m_Outside : entry.District, entry.Budget);
            }
        }
    }
}
