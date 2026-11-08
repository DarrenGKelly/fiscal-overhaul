using System;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// A service's upkeep (the Budget tab's Service Upkeep row is the sum over services), vanilla's way and with
    /// per-district budget %s. Vanilla (CityServiceBudgetSystem.GetEstimatedServiceUpkeep):
    /// <c>baseCost + Σ non-money round(fullCost) + round(moneyFullCost × budget/100)</c>, where moneyFullCost is the sum
    /// over the service's buildings of each one's rounded money upkeep at 100% (wages included). With overrides the
    /// buildings are grouped by the % they run at, each group scaled by its own %, and the total rounded once — so with
    /// a single group (no overrides) it is exactly the vanilla number, float maths and all.
    /// Primitives only, so the game's jobs can call it.
    /// </summary>
    public static class ServiceUpkeepMath
    {
        /// <summary>As UnityEngine.Mathf.RoundToInt (round half to even).</summary>
        public static int Round(float value) => (int)Math.Round(value);

        /// <summary>The money upkeep of buildings costing <paramref name="moneyFullCost"/> at 100%, at <paramref name="budget"/>% (unrounded, vanilla's float maths).</summary>
        public static float MoneyAt(int moneyFullCost, int budget) => moneyFullCost * (budget / 100f);

        /// <summary>Vanilla's upkeep of one service: every building at the city's <paramref name="cityBudget"/>%.</summary>
        public static int VanillaServiceUpkeep(int baseCost, int nonMoneyFullCost, int moneyFullCost, int cityBudget)
            => baseCost + nonMoneyFullCost + Round(MoneyAt(moneyFullCost, cityBudget));

        /// <summary>
        /// One service's upkeep from its money groups: <paramref name="moneyAtBudgets"/> = Σ over the % groups of
        /// <see cref="MoneyAt"/>(group's money full cost, group's %).
        /// </summary>
        public static int ServiceUpkeep(int baseCost, int nonMoneyFullCost, float moneyAtBudgets)
            => baseCost + nonMoneyFullCost + Round(moneyAtBudgets);

        /// <summary>
        /// Convenience over parallel arrays (tests, logging): building i costs <paramref name="moneyFullCosts"/>[i] at
        /// 100% and runs at <paramref name="budgets"/>[i]%. Groups by %, then <see cref="ServiceUpkeep"/>.
        /// </summary>
        public static int ServiceUpkeep(int baseCost, int nonMoneyFullCost, int[] moneyFullCosts, int[] budgets)
        {
            if (moneyFullCosts.Length != budgets.Length) throw new ArgumentException("one budget per building");
            var groups = new System.Collections.Generic.SortedDictionary<int, int>();
            for (int i = 0; i < moneyFullCosts.Length; i++)
            {
                groups.TryGetValue(budgets[i], out int sum);
                groups[budgets[i]] = sum + moneyFullCosts[i];
            }
            float money = 0f;
            foreach (System.Collections.Generic.KeyValuePair<int, int> group in groups) money += MoneyAt(group.Value, group.Key);
            return ServiceUpkeep(baseCost, nonMoneyFullCost, money);
        }
    }
}
