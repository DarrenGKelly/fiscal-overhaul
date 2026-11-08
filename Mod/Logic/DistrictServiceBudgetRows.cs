using System;
using System.Collections.Generic;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The rows of the <c>districtServiceBudgets</c> binding (DistrictListUISystem): one per (service, area) that has
    /// service buildings with a money upkeep or a stored override. Keys are the UI's strings (service entity
    /// "index:version", district key or "outside"), so this stays free of game types.
    /// </summary>
    public static class DistrictServiceBudgetRows
    {
        public struct Row
        {
            public string Service;
            public string District;
            /// <summary>The area's override %, or 0 when it follows the city slider.</summary>
            public int Budget;
            /// <summary>The area's money upkeep for the service at its effective %, rounded once (vanilla's rounding).</summary>
            public int Upkeep;
        }

        /// <summary>
        /// Merges each area's upkeep (<paramref name="moneyAtBudget"/>: Σ over its buildings of
        /// <see cref="ServiceUpkeepMath.MoneyAt"/> at the % each runs at) with the stored overrides. An override with no
        /// buildings still gets a row (upkeep 0), so the slider shows it. Sorted by service, then district, so the
        /// binding's text only changes when a value does.
        /// </summary>
        public static List<Row> Build(IEnumerable<KeyValuePair<(string Service, string District), float>> moneyAtBudget,
            IEnumerable<KeyValuePair<(string Service, string District), int>> overrides)
        {
            var rows = new Dictionary<(string, string), Row>();
            foreach (var pair in moneyAtBudget)
            {
                if (string.IsNullOrEmpty(pair.Key.Service) || pair.Key.District == null) continue;
                rows.TryGetValue(pair.Key, out Row row);
                row.Service = pair.Key.Service;
                row.District = pair.Key.District;
                row.Upkeep += ServiceUpkeepMath.Round(pair.Value);
                rows[pair.Key] = row;
            }
            foreach (var pair in overrides)
            {
                if (string.IsNullOrEmpty(pair.Key.Service) || pair.Key.District == null || pair.Value <= 0) continue;
                rows.TryGetValue(pair.Key, out Row row);
                row.Service = pair.Key.Service;
                row.District = pair.Key.District;
                row.Budget = pair.Value;
                rows[pair.Key] = row;
            }
            var list = new List<Row>(rows.Values);
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Service, b.Service);
                return c != 0 ? c : string.CompareOrdinal(a.District, b.District);
            });
            return list;
        }
    }
}
