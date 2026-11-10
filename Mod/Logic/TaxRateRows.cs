using System.Collections.Generic;
using System.Globalization;

namespace FiscalOverhaul.Logic
{
    /// <summary>One district override as the Taxation tab gets it: the district's <see cref="DistrictKey"/>, type, zone, key and stored value.</summary>
    public readonly struct DistrictRateRow
    {
        public readonly string Area;
        public readonly TaxType Type;
        public readonly int Zone;
        public readonly int Key;
        /// <summary>As stored: a full zone rate, an offset from the zone rate (level/resource), a density tax rate.</summary>
        public readonly int Value;

        public DistrictRateRow(string area, TaxType type, int zone, int key, int value)
        {
            Area = area;
            Type = type;
            Zone = zone;
            Key = key;
            Value = value;
        }
    }

    /// <summary>
    /// The pure half of <see cref="FiscalOverhaul.TaxRatesUISystem"/>: the rows its <c>taxRates</c> binding sends, and
    /// what a <c>setTaxRate(type, zone, key, area, value)</c> call names.
    /// </summary>
    public static class TaxRateRows
    {
        /// <summary>Every stored district override of a real zone, keyed for the UI (<see cref="AreaKey"/>: "index:version", or "outside"), in table order.</summary>
        public static List<DistrictRateRow> DistrictRows(TaxRateTable table)
        {
            var rows = new List<DistrictRateRow>();
            if (table == null) return rows;
            foreach (TaxRateEntry e in table.Entries())
            {
                if (e.IsCity || e.Zone < 0 || e.Zone >= DensityTaxTable.ZoneCount) continue;
                rows.Add(new DistrictRateRow(AreaKey(e.Area, e.AreaVersion), e.Type, e.Zone, e.Key, e.Value));
            }
            return rows;
        }

        /// <summary>
        /// What a setTaxRate call names: a type, a zone, a key ("0".."2" density, "0".."4" education level, a resource
        /// index; ignored for a zone rate) and an area: "" for the city (density only: the city's zone and resource
        /// rates are vanilla's own sliders), a district key ("index:version") or "outside" (Outside Districts). False for anything else.
        /// </summary>
        public static bool TryResolve(int type, int zone, string key, string area, out TaxType taxType, out int keyIndex, out int areaIndex, out int areaVersion)
        {
            taxType = (TaxType)type;
            keyIndex = 0;
            areaIndex = TaxRateTable.CityArea;
            areaVersion = 0;
            if (zone < 0 || zone >= DensityTaxTable.ZoneCount) return false;
            bool city = string.IsNullOrEmpty(area);
            if (!city && !TryArea(area, out areaIndex, out areaVersion)) return false;
            switch (taxType)
            {
                case TaxType.Density:
                    if (!TryKey(key, out keyIndex) || keyIndex >= DensityTaxTable.DensityCount) return false;
                    return DensityTaxTable.IsSupported((TaxZone)zone, (Density)keyIndex);
                case TaxType.ZoneRate:
                    return !city;
                case TaxType.ResourceRate:
                    return !city && TryKey(key, out keyIndex) && TaxRateModel.IsValidKey((TaxZone)zone, keyIndex);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Splits setTaxRate's target, "key@area" (a trigger takes at most four arguments): "2@604018:1" is key "2" of
        /// that district, "2" (or "2@") key "2" of the city. Null gives empty strings.
        /// </summary>
        public static void SplitTarget(string target, out string key, out string area)
        {
            target = target ?? "";
            int at = target.IndexOf('@');
            key = at < 0 ? target : target.Substring(0, at);
            area = at < 0 ? "" : target.Substring(at + 1);
        }

        /// <summary>The UI's key for a table area: a district's <see cref="DistrictKey.Format"/>, or <see cref="DistrictKey.Outside"/>.</summary>
        public static string AreaKey(int area, int areaVersion)
            => TaxRateTable.IsOutside(area, areaVersion) ? DistrictKey.Outside : DistrictKey.Format(area, areaVersion);

        /// <summary>
        /// A UI area key as a table area: "outside" is <see cref="TaxRateTable.OutsideArea"/>, "index:version" a district.
        /// False for the city ("") or anything else.
        /// </summary>
        public static bool TryArea(string area, out int areaIndex, out int areaVersion)
        {
            if (area == DistrictKey.Outside)
            {
                areaIndex = TaxRateTable.OutsideArea;
                areaVersion = TaxRateTable.OutsideVersion;
                return true;
            }
            return DistrictKey.TryParse(area, out areaIndex, out areaVersion);
        }

        private static bool TryKey(string key, out int value)
            => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
