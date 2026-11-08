using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The kinds of tax value a city stores. Stored as ints in the save, so values are never renumbered; new types
    /// (building level, land value, wealth) take the next free numbers and work the same way: a city value and an
    /// optional per-district override.
    /// </summary>
    public enum TaxType
    {
        /// <summary>The density tax: its own rate per zone and density (key = <see cref="Density"/>), on top of income tax. City and district.</summary>
        Density = 0,
        /// <summary>RETIRED: the old ±adjustment per district and zone. Never stored; dropped when an old save loads.</summary>
        District = 1,
        /// <summary>A district's own zone rate (the full rate, like vanilla's zone slider; key 0). District only: the city's is vanilla's.</summary>
        ZoneRate = 2,
        /// <summary>
        /// A district's own education-level (Residential, key = job level 0..4) or resource (other zones, key = resource
        /// index) rate, stored as vanilla stores it: an offset from the zone rate. District only.
        /// </summary>
        ResourceRate = 3,
    }

    /// <summary>One stored value: (type, zone, key, area) to percentage points. Area (0, 0) is the city; else a district entity's index and version.</summary>
    public readonly struct TaxRateEntry : IEquatable<TaxRateEntry>
    {
        public readonly TaxType Type;
        public readonly int Zone;
        public readonly int Key;
        /// <summary>The district entity's index; 0 (with <see cref="AreaVersion"/> 0) for the city.</summary>
        public readonly int Area;
        public readonly int AreaVersion;
        public readonly int Value;

        public TaxRateEntry(TaxType type, int zone, int key, int area, int areaVersion, int value)
        {
            Type = type;
            Zone = zone;
            Key = key;
            Area = area;
            AreaVersion = areaVersion;
            Value = value;
        }

        public bool IsCity => Area == 0 && AreaVersion == 0;

        public bool Equals(TaxRateEntry other) => Type == other.Type && Zone == other.Zone && Key == other.Key && Area == other.Area && AreaVersion == other.AreaVersion && Value == other.Value;
        public override bool Equals(object obj) => obj is TaxRateEntry e && Equals(e);
        public override int GetHashCode() => (((((int)Type * 31 + Zone) * 31 + Key) * 31 + Area) * 31 + AreaVersion) * 31 + Value;
        public override string ToString() => $"{Type}/{Zone}/{Key}@{Area}:{AreaVersion}={Value}";
    }

    /// <summary>
    /// A city's tax values beyond vanilla's own rate array, keyed by (type, zone, key, area): the city's density tax
    /// rates and each district's overrides (its own zone, education/resource and density rates). Pure data;
    /// <see cref="FiscalOverhaul.TaxRatesSystem"/> keeps one per save.
    ///
    /// City values are sparse with 0 as the default (a 0 city density rate is not stored). A district entry is an
    /// override and is stored whatever its value (0 can differ from the city's); removing it makes the district follow
    /// the city again.
    /// </summary>
    public sealed class TaxRateTable
    {
        /// <summary>
        /// The save and text format. 2 keys every entry by area (city or district entity). A version-1 block (the old
        /// ±adjustment model) loads its density entries and drops its district ones; any other version loads empty.
        /// </summary>
        public const int FormatVersion = 2;

        /// <summary>The density tax's own rate range: a tax break down to −5, a tax up to +10.</summary>
        public const int DensityMin = -5;
        public const int DensityMax = 10;

        /// <summary>Zone and resource rates: a wide guard only; <see cref="TaxRateModel"/> clamps to vanilla's limits when setting.</summary>
        public const int RateGuard = 100;

        public const int CityArea = 0;

        private readonly Dictionary<(TaxType, int, int, int, int), int> m_Values = new Dictionary<(TaxType, int, int, int, int), int>();

        /// <summary>The range a type's values are clamped to. Unknown and retired types get (0, 0), so nothing is stored.</summary>
        public static void RangeOf(TaxType type, out int min, out int max)
        {
            switch (type)
            {
                case TaxType.Density: min = DensityMin; max = DensityMax; return;
                case TaxType.ZoneRate:
                case TaxType.ResourceRate: min = -RateGuard; max = RateGuard; return;
                default: min = 0; max = 0; return;
            }
        }

        /// <summary>Whether a type can be stored for the city (density) or only for districts (zone and resource rates: vanilla's).</summary>
        public static bool HasCityValue(TaxType type) => type == TaxType.Density;

        public int Count => m_Values.Count;

        /// <summary>The stored value, or 0.</summary>
        public int Get(TaxType type, int zone, int key, int area = CityArea, int areaVersion = 0)
            => m_Values.TryGetValue((type, zone, key, area, areaVersion), out int value) ? value : 0;

        public bool TryGet(TaxType type, int zone, int key, int area, int areaVersion, out int value)
            => m_Values.TryGetValue((type, zone, key, area, areaVersion), out value);

        /// <summary>
        /// Stores a value, clamped to <see cref="RangeOf"/>. A city value of 0 removes it (the default); a district
        /// value is stored as an override even at 0. Retired types and city zone/resource rates store nothing.
        /// Returns the stored value.
        /// </summary>
        public int Set(TaxType type, int zone, int key, int area, int areaVersion, int value)
        {
            RangeOf(type, out int min, out int max);
            if (min == 0 && max == 0) return 0;
            bool city = area == CityArea && areaVersion == 0;
            if (city && !HasCityValue(type)) return 0;
            int clamped = Math.Max(min, Math.Min(max, value));
            var k = (type, zone, key, area, areaVersion);
            if (city && clamped == 0) m_Values.Remove(k);
            else m_Values[k] = clamped;
            return clamped;
        }

        /// <summary>Sets a city value.</summary>
        public int Set(TaxType type, int zone, int key, int value) => Set(type, zone, key, CityArea, 0, value);

        /// <summary>Removes one value (a district's override: it follows the city again). Returns whether one was stored.</summary>
        public bool Remove(TaxType type, int zone, int key, int area, int areaVersion) => m_Values.Remove((type, zone, key, area, areaVersion));

        /// <summary>Removes every value of one type, city and districts.</summary>
        public void Clear(TaxType type)
        {
            var remove = new List<(TaxType, int, int, int, int)>();
            foreach (var k in m_Values.Keys) if (k.Item1 == type) remove.Add(k);
            foreach (var k in remove) m_Values.Remove(k);
        }

        /// <summary>Removes every override of one district ("Use city rates"). Returns how many.</summary>
        public int ClearArea(int area, int areaVersion)
        {
            if (area == CityArea && areaVersion == 0) return 0;
            var remove = new List<(TaxType, int, int, int, int)>();
            foreach (var k in m_Values.Keys) if (k.Item4 == area && k.Item5 == areaVersion) remove.Add(k);
            foreach (var k in remove) m_Values.Remove(k);
            return remove.Count;
        }

        public void Clear() => m_Values.Clear();

        /// <summary>Whether any value of this type is stored for the city.</summary>
        public bool Any(TaxType type)
        {
            foreach (var k in m_Values.Keys) if (k.Item1 == type && k.Item4 == CityArea && k.Item5 == 0) return true;
            return false;
        }

        /// <summary>Whether the district has any override.</summary>
        public bool AnyForArea(int area, int areaVersion)
        {
            foreach (var k in m_Values.Keys) if (k.Item4 == area && k.Item5 == areaVersion) return true;
            return false;
        }

        /// <summary>Every stored entry, in a stable order (type, zone, key, area, area version).</summary>
        public List<TaxRateEntry> Entries()
        {
            var list = new List<TaxRateEntry>(m_Values.Count);
            foreach (var pair in m_Values)
                list.Add(new TaxRateEntry(pair.Key.Item1, pair.Key.Item2, pair.Key.Item3, pair.Key.Item4, pair.Key.Item5, pair.Value));
            list.Sort((a, b) =>
            {
                int c = a.Type.CompareTo(b.Type);
                if (c == 0) c = a.Zone.CompareTo(b.Zone);
                if (c == 0) c = a.Key.CompareTo(b.Key);
                if (c == 0) c = a.Area.CompareTo(b.Area);
                if (c == 0) c = a.AreaVersion.CompareTo(b.AreaVersion);
                return c;
            });
            return list;
        }

        /// <summary>
        /// Drops the district entries whose district no longer exists (<paramref name="exists"/>(index, version) false).
        /// City entries are never dropped. Returns how many were dropped.
        /// </summary>
        public int PruneDistricts(Func<int, int, bool> exists)
        {
            var remove = new List<(TaxType, int, int, int, int)>();
            foreach (var k in m_Values.Keys)
                if (!(k.Item4 == CityArea && k.Item5 == 0) && !exists(k.Item4, k.Item5)) remove.Add(k);
            foreach (var k in remove) m_Values.Remove(k);
            return remove.Count;
        }

        /// <summary>
        /// Seeds the city's density entries from the Options template (<see cref="DensityTaxTable.Serialize"/>'s 12 ints).
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

        /// <summary>Text form: "2;type,zone,key,area,areaVersion,value;...".</summary>
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
                  .Append(e.Area.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.AreaVersion.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(e.Value.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Parses <see cref="Serialize"/>'s output. Version 1 text ("1;type,zone,key,keyVersion,value;...") keeps its
        /// density entries and drops its district ones; an unknown version or bad input gives an empty table; bad
        /// entries are skipped.
        /// </summary>
        public static TaxRateTable Parse(string text)
        {
            var table = new TaxRateTable();
            if (string.IsNullOrWhiteSpace(text)) return table;
            string[] parts = text.Split(';');
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)) return table;
            int fields = version == FormatVersion ? 6 : version == 1 ? 5 : 0;
            if (fields == 0) return table;
            for (int i = 1; i < parts.Length; i++)
            {
                string[] f = parts[i].Split(',');
                if (f.Length != fields) continue;
                var v = new int[fields];
                bool ok = true;
                for (int j = 0; j < fields && ok; j++) ok = int.TryParse(f[j].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[j]);
                if (!ok || !Enum.IsDefined(typeof(TaxType), v[0])) continue;
                if (version == 1)
                    LoadVersion1(table, (TaxType)v[0], v[1], v[2], v[4]);
                else
                    table.Set((TaxType)v[0], v[1], v[2], v[3], v[4], v[5]);
            }
            return table;
        }

        /// <summary>
        /// One entry of a version-1 table (the ±adjustment model): a density entry is the city's density rate (the old
        /// ±5 offsets are valid rates); a district adjustment has no meaning in the override model and is dropped.
        /// </summary>
        public static void LoadVersion1(TaxRateTable table, TaxType type, int zone, int key, int value)
        {
            if (type == TaxType.Density) table.Set(TaxType.Density, zone, key, value);
        }
    }
}
