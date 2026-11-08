using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxHappinessMathTests
    {
        // The game's CitizenHappinessPrefab defaults (1.6.0f1): uneducated .. highly educated.
        private const float U = -0.25f, P = -0.5f, E = -1f, W = -1.5f, H = -2f;

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(4)]
        public void NeutralRate_IsNoBonus(int level)
        {
            Assert.Equal(0, TaxHappinessMath.Bonus(10, level, U, P, E, W, H, 0f, 0f));
            Assert.NotEqual(0, TaxHappinessMath.Bonus(14, level, U, P, E, W, H, 0f, 0f));
        }

        [Theory]
        // (10 - 15) * -multiplier, rounded to even as Mathf.RoundToInt.
        [InlineData(0, -1)]  // -1.25
        [InlineData(1, -2)]  // -2.5 → -2
        [InlineData(2, -5)]
        [InlineData(3, -8)]  // -7.5 → -8
        [InlineData(4, -10)]
        public void Rate15_IsTheMatchingNegativeBonus(int level, int expected)
        {
            Assert.Equal(expected, TaxHappinessMath.Bonus(15, level, U, P, E, W, H, 0f, 0f));
            Assert.NotEqual(expected, TaxHappinessMath.Bonus(10, level, U, P, E, W, H, 0f, 0f));
        }

        [Fact]
        public void LowerRate_IsAPositiveBonus()
        {
            Assert.Equal(10, TaxHappinessMath.Bonus(5, 4, U, P, E, W, H, 0f, 0f));
            Assert.True(TaxHappinessMath.Bonus(5, 2, U, P, E, W, H, 0f, 0f) > 0);
        }

        [Theory]
        [InlineData(0, 1f)]
        [InlineData(1, 2f)]
        [InlineData(2, 3f)]
        [InlineData(3, 4f)]
        [InlineData(4, 5f)]
        public void EachLevel_PicksItsMultiplier(int level, float expected)
        {
            Assert.Equal(expected, TaxHappinessMath.Multiplier(level, 1f, 2f, 3f, 4f, 5f));
            Assert.NotEqual(expected, TaxHappinessMath.Multiplier((level + 1) % 5, 1f, 2f, 3f, 4f, 5f));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        public void WrongLevel_IsNoBonus(int level)
        {
            Assert.Equal(0f, TaxHappinessMath.Multiplier(level, 1f, 2f, 3f, 4f, 5f));
            Assert.Equal(0, TaxHappinessMath.Bonus(20, level, U, P, E, W, H, 0f, 0f));
            Assert.NotEqual(0, TaxHappinessMath.Bonus(20, 2, U, P, E, W, H, 0f, 0f));
        }

        [Fact]
        public void CityModifier_IsAppliedBeforeTheMultiplier()
        {
            // value = 10 - 15 = -5; + 1 flat = -4; + (-4 * 0.5) = -6; * -(-1) = -6.
            Assert.Equal(-6, TaxHappinessMath.Bonus(15, E, 1f, 0.5f));
            Assert.NotEqual(TaxHappinessMath.Bonus(15, E, 0f, 0f), TaxHappinessMath.Bonus(15, E, 1f, 0.5f));
            // A flat modifier moves even the neutral rate.
            Assert.Equal(-2, TaxHappinessMath.Bonus(10, E, -2f, 0f));
        }

        [Fact]
        public void ResidentRate_AddsTheHomesAdjustments_InsideTheLimits()
        {
            Assert.Equal(12, TaxHappinessMath.ResidentRate(12, 0, 0, 0, 30));
            Assert.Equal(17, TaxHappinessMath.ResidentRate(12, 3, 2, 0, 30));
            Assert.Equal(30, TaxHappinessMath.ResidentRate(28, 5, 0, 0, 30));
            Assert.Equal(0, TaxHappinessMath.ResidentRate(2, -5, 0, 0, 30));
            Assert.NotEqual(12, TaxHappinessMath.ResidentRate(12, 0, 5, 0, 30));
        }
    }
}
