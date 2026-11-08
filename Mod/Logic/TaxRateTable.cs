using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The kinds of tax adjustment a city stores. Stored as ints in the save, so values are never renumbered; new types
    /// (building level, land value, wealth) take the next free numbers.
    /// </summary>
    public enum TaxType
    {
        /// <summary>Per zone and density; key = <see cref="Density"/>.</summary>
        Density = 0,
        /// <summary>Per zone and district; key = the district entity's index, key version = its version.</summary>
        District = 1,
    }

    /// <summary>One stored adjustment: (type, zone, key, key version) to percentage points.</summary>
    public readonly struct TaxRateEntry : IEquatable<TaxRateEntry>
    {
        public readonly TaxType Type;
        public readonly int Zone;
        public readonly int Key;
        /// <summary>The entity version for <see cref="TaxType.District"/>; 0 for the other types.</summary>
        public readonly int KeyVersion;
        public readonly int Value;

        public TaxRateEntry(TaxType type, int zone, int key, int keyVersion, int value)
        {
            Type = type;
            Zone = zone;
            Key = key;
            KeyVersion = keyVersion;
            Value = value;
        }

        public bool Equals(TaxRateEntry other) => Type == other.Type && Zone == other.Zone && Key == other.Key && KeyVersion == other.KeyVersion && Value == other.Value;
        public override bool Equals(object obj) => obj is TaxRateEntry e && Equals(e);
        public override int GetHashCode() => ((((int)Type * 31 + Zone) * 31 + Key) * 31 + KeyVersion) * 31 + Value;
        public override string ToString() => $"{Type}/{Zone}/{Key}:{KeyVersion}={Value}";
    }

    /// <summary>
    /// A city's tax adjustments, in percentage points on top of the vanilla rate, keyed by (type, zone, key). Sparse:
    /// a 0 adjustment is not stored. Pure data; <see cref="FiscalOverhaul.TaxRatesSystem"/> keeps one per save.
    /// </summary>
    public sealed class TaxRateTable
    {
        /// <summary>The save and text format. A block with any other version loads as an empty table.</summary>
        public const int FormatVersion = 1;

        /// <summary>Density adjustments: ±5, as before (they change revenue only, not demand or happiness).</summary>
        public const int DensityCap = 5;

        /// <summary>District adjustments. PROVISIONAL: ±10 until the district slice settles it.</summary>
        public const int DistrictCap = 10;

        private readonly Dictionary<(TaxType, int, int, int), int> m_Values = new Dictionary<(TaxType, int, int, int), int>();

        /// <summary>The cap for a type: values are clamped to ±this. Unknown types get 0, so nothing is stored.</summary>
        public static int CapOf(TaxType type)
        {
            switch (type)
            {
                case TaxType.Density: return DensityCap;
                case TaxType.District: return DistrictCap;
                default: return 0;
            }
        }

        public int Count => m_Values.Count;

        public int Get(TaxType type, int zone, int key, int keyVersion = 0)
            => m_Values.TryGetValue((type, zone, key, keyVersion), out int value) ? value : 0;

        /// <summary>Sets an adjustment, clamped to ±<see cref="CapOf"/>; 0 removes it. Returns the stored value.</summary>
        public int Set(TaxType type, int zone, int key, int keyVersion, int value)
        {
            int cap = CapOf(type);
            int clamped = Math.Max(-cap, Math.Min(cap, value));
            var k = (type, zone, key, keyVersion);
            if (clamped == 0) m_Values.Remove(k);
            else m_Values[k] = clamped;
            return clamped;
        }

        public int Set(TaxType type, int zone, int key, int value) => Set(type, zone, key, 0, value);

        /// <summary>Removes every adjustment of one type.</summary>
        public void Clear(TaxType type)
        {
            var remove = new List<(TaxType, int, int, int)>();
            foreach (var k in m_Values.Keys) if (k.Item1 == type) remove.Add(k);
            foreach (var k in remove) m_Values.Remove(k);
        }

        public void Clear() => m_Values.Clear();

        /// <summary>Whether any adjustment of this type is stored.</summary>
        public bool Any(TaxType type)
        {
            foreach (var k in m_Values.Keys) if (k.Item1 == type) return true;
            return false;
        }

        /// <summary>Every stored entry, in a stable order (type, zone, key, version).</summary>
        public List<TaxRateEntry> Entries()
        {
            var list = new List<TaxRateEntry>(m_Values.Count);
            foreach (var pair in m_Values)
                list.Add(new TaxRateEntry(pair.Key.Item1, pair.Key.Item2, pair.Key.Item3, pair.Key.Item4, pair.Value));
            list.Sort((a, b) =>
            {
                int c = a.Type.CompareTo(b.Type);
                if (c == 0) c = a.Zone.CompareTo(b.Zone);
                if (c == 0) c = a.Key.CompareTo(b.Key);
                if (c == 0) c = a.KeyVersion.CompareTo(b.KeyVersion);
                return c;
            });
            return list;
        }

        /// <summary>
        /// Drops the district entries whose district no longer exists (<paramref name="exists"/>(index, version) false).
        /// Returns how many were dropped.
        /// </summary>
        public int PruneDistricts(Func<int, int, bool> exists)
        {
            var remove = new List<(TaxType, int, int, int)>();
            foreach (var k in m_Values.Keys)
                if (k.Item1 == TaxType.District && !exists(k.Item3, k.Item4)) remove.Add(k);
            foreach (var k in remove) m_Values.Remove(k);
            return remove.Count;
        }

        /// <summary>
        /// Seeds the density entries from the Options template (<see cref="DensityTaxTable.Serialize"/>'s 12 ints).
        /// Empty or malformed text seeds nothing. Returns how many entries were set.
        /// </summary>
        public int SeedDensityFrom(string template)
        {
            DensityTaxTable parsed = DensityTaxTable.Parse(template);
            int seeded = 0;
            for (int z = 0; z < DensityTaxTable.ZoneCount; z++)
            {
                for (int d = 0; d < DensityTaxTable.DensityCount; d++)
                {
                    int value = parsed.Get((TaxZone)z, (Density)d);
                    if (value == 0) continue;
                    Set(TaxType.Density, z, d, value);
                    seeded++;
                }
            }
            return seeded;
        }

        /// <summary>Text form: "FormatVersion;type,zone,key,version,value;...".</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append(FormatVersion.ToString(CultureInfo.InvariantCulture));
            foreach (TaxRateEntry e in Entries())
            {
                sb.Append(';')
                  .Append(((int)e.Type).ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.Zone.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.Key.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.KeyVersion.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.Value.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>Parses <see cref="Serialize"/>'s output. An unknown version or bad input gives an empty table; bad entries are skipped.</summary>
        public static TaxRateTable Parse(string text)
        {
            var table = new TaxRateTable();
            if (string.IsNullOrWhiteSpace(text)) return table;
            string[] parts = text.Split(';');
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version) || version != FormatVersion)
                return table;
            for (int i = 1; i < parts.Length; i++)
            {
                string[] f = parts[i].Split(',');
                if (f.Length != 5) continue;
                var v = new int[5];
                bool ok = true;
                for (int j = 0; j < 5 && ok; j++) ok = int.TryParse(f[j].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[j]);
                if (!ok || !Enum.IsDefined(typeof(TaxType), v[0])) continue;
                table.Set((TaxType)v[0], v[1], v[2], v[3], v[4]);
            }
            return table;
        }
    }
}
