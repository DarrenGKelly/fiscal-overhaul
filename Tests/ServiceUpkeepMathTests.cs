using System.Linq;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class ServiceUpkeepMathTests
    {
        // As CityServiceBudgetSystem.GetEstimatedServiceUpkeep, with UnityEngine.Mathf.RoundToInt = Math.Round.
        private static int VanillaFormula(int baseCost, int nonMoney, int money, int budget)
            => baseCost + nonMoney + (int)System.Math.Round((float)money * ((float)budget / 100f));

        [Theory]
        [InlineData(100)]
        [InlineData(50)]
        [InlineData(73)]
        [InlineData(150)]
        public void NoOverrides_EqualsTheVanillaFormula(int cityBudget)
        {
            int[] costs = { 1234, 5677, 333, 9 };
            int[] budgets = { cityBudget, cityBudget, cityBudget, cityBudget };
            int expected = VanillaFormula(500, 40, 1234 + 5677 + 333 + 9, cityBudget);
            Assert.Equal(expected, ServiceUpkeepMath.ServiceUpkeep(500, 40, costs, budgets));
            Assert.Equal(expected, ServiceUpkeepMath.VanillaServiceUpkeep(500, 40, 1234 + 5677 + 333 + 9, cityBudget));
        }

        [Fact]
        public void OneDistrictAt150_RaisesOnlyItsShare()
        {
            // Two buildings of 1000 at 100%; one is in a district overridden to 150%.
            int vanilla = ServiceUpkeepMath.ServiceUpkeep(0, 0, new[] { 1000, 1000 }, new[] { 100, 100 });
            int withOverride = ServiceUpkeepMath.ServiceUpkeep(0, 0, new[] { 1000, 1000 }, new[] { 150, 100 });
            Assert.Equal(2000, vanilla);
            Assert.Equal(2500, withOverride); // +50% of that building's 1000 only, not of the service's 2000
        }

        [Fact]
        public void BaseAndNonMoneyCosts_AreNotScaledByThePercent()
        {
            int at50 = ServiceUpkeepMath.ServiceUpkeep(300, 70, new[] { 1000 }, new[] { 50 });
            Assert.Equal(300 + 70 + 500, at50);
            Assert.NotEqual((300 + 70 + 1000) / 2, at50);
        }

        [Fact]
        public void Round_IsHalfToEven_AsMathfRoundToInt()
        {
            Assert.Equal(2, ServiceUpkeepMath.Round(2.5f));
            Assert.Equal(4, ServiceUpkeepMath.Round(3.5f));
            Assert.Equal(3, ServiceUpkeepMath.Round(2.6f));
        }

        // --- District budgets follow the per-district service budget % ---

        [Fact]
        public void EffectivePercent_UsesTheOverrideOnlyWhenItApplies()
        {
            Assert.Equal(150, ServiceUpkeepMath.EffectivePercent(true, true, 150, 100));
            Assert.Equal(100, ServiceUpkeepMath.EffectivePercent(false, true, 150, 100)); // feature / master switch off
            Assert.Equal(100, ServiceUpkeepMath.EffectivePercent(true, false, 0, 100));   // no override for this district
        }

        // Weight of a district's buildings: money at 100% per building, each scaled by its effective %.
        private static double[] DistrictWeights(int[] moneyPerDistrict, int?[] overrides, int city, bool applies)
        {
            var w = new double[moneyPerDistrict.Length];
            for (int d = 0; d < w.Length; d++)
            {
                int pct = ServiceUpkeepMath.EffectivePercent(applies, overrides[d].HasValue, overrides[d] ?? 0, city);
                w[d] = ServiceUpkeepMath.MoneyAt(moneyPerDistrict[d], pct);
            }
            return w;
        }

        [Fact]
        public void Override150_GrowsThatDistrictsShare_AndShrinksTheOthers()
        {
            int[] money = { 1000, 1000, 1000 };
            double[] before = DistrictBudgetMath.Apportion(3000, DistrictWeights(money, new int?[3], 100, true));
            double[] after = DistrictBudgetMath.Apportion(3000, DistrictWeights(money, new int?[] { 150, null, null }, 100, true));
            Assert.True(after[0] > before[0]);
            Assert.True(after[1] < before[1]);
            Assert.True(after[2] < before[2]);
            Assert.Equal(3000, after.Sum(), 6);
        }

        [Fact]
        public void NoOverrides_ShareEqualsTheCityPercentResult()
        {
            int[] money = { 700, 300, 1200 };
            double[] cityOnly = DistrictBudgetMath.Apportion(2200, DistrictWeights(money, new int?[3], 80, false));
            double[] noneSet = DistrictBudgetMath.Apportion(2200, DistrictWeights(money, new int?[3], 80, true));
            double[] switchedOff = DistrictBudgetMath.Apportion(2200, DistrictWeights(money, new int?[] { 150, null, null }, 80, false));
            Assert.Equal(cityOnly, noneSet);
            Assert.Equal(cityOnly, switchedOff);
        }

        [Fact]
        public void SplitChildren_SumToTheDistrictSlot_WithOverrides()
        {
            // Two services in one district at 150% and 100%: children weigh with the same effective %s.
            double[] serviceWeights =
            {
                ServiceUpkeepMath.MoneyAt(400, 150), ServiceUpkeepMath.MoneyAt(600, 100),
            };
            long districtSlot = 1234;
            long[] children = ServiceUpkeepSplit.Split(districtSlot, serviceWeights);
            Assert.Equal(districtSlot, children.Sum());
            Assert.True(children[0] > ServiceUpkeepSplit.Split(districtSlot, new[] { 400.0, 600.0 })[0]);
        }
    }
}
