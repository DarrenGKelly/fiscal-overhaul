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
    }
}
