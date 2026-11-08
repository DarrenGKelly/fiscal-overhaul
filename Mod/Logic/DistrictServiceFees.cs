using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The Services tab's district fee income (ServiceTableUISystem's <c>districtServiceFees</c>): each service's fee
    /// income per resource (vanilla serviceDetails' <c>incomeInternal</c>) split over the areas by usage, with the
    /// Budget chart's weights (<see cref="DistrictBudgetMath"/>: an area's live value for the fee's income source over
    /// every area's). Keys are the UI's strings, so this stays free of game types.
    /// </summary>
    public static class DistrictServiceFees
    {
        /// <summary>One fee of one service, city-wide.</summary>
        public struct Fee
        {
            /// <summary>The service prefab entity as "index:version".</summary>
            public string Service;
            /// <summary>Game.City.PlayerResource.</summary>
            public int Resource;
            /// <summary>Game.City.IncomeSource the fee is booked under (EconomyUtils.GetIncomeSource).</summary>
            public int IncomeSource;
            /// <summary>The city's fee income estimate for the resource (vanilla's incomeInternal).</summary>
            public double Income;
        }

        public struct Row
        {
            public string Service;
            public string District;
            public int Resource;
            /// <summary>The area's share of the fee income, rounded.</summary>
            public int Income;
        }

        /// <summary>
        /// The slot of an income source in a <see cref="DistrictBudgetMath.SourceCount"/>-wide sample, or -1 for a
        /// city-only source (nothing a district can be weighed by).
        /// </summary>
        public static int SlotOf(int incomeSource) => Array.IndexOf(DistrictBudgetMath.DistrictIncomeSources, incomeSource);

        /// <summary>
        /// Splits each fee's income over the areas by their live values for its income source
        /// (<see cref="DistrictBudgetMath.Apportion"/>: negative counts as 0, no usage anywhere gives every area 0).
        /// <paramref name="areaValues"/>: per area key (DistrictKey or "outside"), its live sample (SourceCount wide,
        /// <c>DistrictBudgetSystem.LiveValues</c>). A row only for a non-zero share; sorted by service, district,
        /// resource, so the binding's text only changes when a value does.
        /// </summary>
        public static List<Row> Build(IEnumerable<Fee> fees, IReadOnlyList<KeyValuePair<string, float[]>> areaValues)
        {
            var rows = new List<Row>();
            if (fees == null || areaValues == null || areaValues.Count == 0) return rows;
            foreach (Fee fee in fees)
            {
                if (string.IsNullOrEmpty(fee.Service) || fee.Income == 0) continue;
                int slot = SlotOf(fee.IncomeSource);
                if (slot < 0) continue;
                var weights = new double[areaValues.Count];
                for (int a = 0; a < areaValues.Count; a++)
                {
                    float[] values = areaValues[a].Value;
                    weights[a] = values != null && slot < values.Length ? values[slot] : 0;
                }
                double[] shares = DistrictBudgetMath.Apportion(fee.Income, weights);
                for (int a = 0; a < shares.Length; a++)
                {
                    int income = (int)Math.Round(shares[a], MidpointRounding.AwayFromZero);
                    if (income == 0 || areaValues[a].Key == null) continue;
                    rows.Add(new Row { Service = fee.Service, District = areaValues[a].Key, Resource = fee.Resource, Income = income });
                }
            }
            rows.Sort((x, y) =>
            {
                int c = string.CompareOrdinal(x.Service, y.Service);
                if (c != 0) return c;
                c = string.CompareOrdinal(x.District, y.District);
                return c != 0 ? c : x.Resource.CompareTo(y.Resource);
            });
            return rows;
        }
    }
}
