using System.Collections.Generic;
using System.Globalization;

namespace FiscalOverhaul.Logic
{
    /// <summary>One district adjustment as the Taxation tab gets it: the district's <see cref="DistrictKey"/>, zone, points.</summary>
    public readonly struct DistrictRateRow
    {
        public readonly string Key;
        public readonly int Zone;
        public readonly int Value;

        public DistrictRateRow(string key, int zone, int value)
        {
            Key = key;
            Zone = zone;
            Value = value;
        }
    }

    /// <summary>
    /// The pure half of <see cref="FiscalOverhaul.TaxRatesUISystem"/>: the rows its <c>taxRates</c> binding sends, and
    /// what a <c>setTaxRate(type, zone, key, value)</c> call names in the <see cref="TaxRateTable"/>.
    /// </summary>
    public static class TaxRateRows
    {
        /// <summary>Every stored district adjustment of a real zone, keyed for the UI (<see cref="DistrictKey.Format"/>), in table order.</summary>
        public static List<DistrictRateRow> DistrictRows(TaxRateTable table)
        {
            var rows = new List<DistrictRateRow>();
            if (table == null) return rows;
            foreach (TaxRateEntry e in table.Entries())
            {
                if (e.Type != TaxType.District || e.Zone < 0 || e.Zone >= DensityTaxTable.ZoneCount) continue;
                rows.Add(new DistrictRateRow(DistrictKey.Format(e.Key, e.KeyVersion), e.Zone, e.Value));
            }
            return rows;
        }

        /// <summary>
        /// What a setTaxRate call names: <see cref="TaxType.District"/> with a district key ("index:version"; not Entire
        /// City or Outside), or <see cref="TaxType.Density"/> with a supported density ("0".."2") of the zone. False for
        /// anything else (unknown type, zone out of range, bad key), which the trigger ignores.
        /// </summary>
        public static bool TryResolve(int type, int zone, string key, out TaxType taxType, out int keyIndex, out int keyVersion)
        {
            taxType = (TaxType)type;
            keyIndex = 0;
            keyVersion = 0;
            if (zone < 0 || zone >= DensityTaxTable.ZoneCount) return false;
            switch (taxType)
            {
                case TaxType.District:
                    return DistrictKey.TryParse(key, out keyIndex, out keyVersion);
                case TaxType.Density:
                    if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out keyIndex)) return false;
                    if (keyIndex < 0 || keyIndex >= DensityTaxTable.DensityCount) return false;
                    return DensityTaxTable.IsSupported((TaxZone)zone, (Density)keyIndex);
                default:
                    return false;
            }
        }
    }
}
