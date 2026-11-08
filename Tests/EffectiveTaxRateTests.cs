using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class EffectiveTaxRateTests
    {
        // Vanilla's default job-level limits are about [0, 30]; the tests use those.
        private const int Min = 0;
        private const int Max = 30;
        private const int Income = 1000;

        [Fact]
        public void NoAdjustment_IsTheVanillaRate()
        {
            Assert.Equal(12, EffectiveTaxRate.Compute(12, 0, 0, Min, Max, Income));
            // Even a vanilla rate outside the limits stands untouched: all-zero adjustments are exactly vanilla.
            Assert.Equal(35, EffectiveTaxRate.Compute(35, 0, 0, Min, Max, Income));
            Assert.Equal(-2, EffectiveTaxRate.Compute(-2, 0, 0, Min, Max, Income));
        }

        [Fact]
        public void InsideTheLimits_IsTheSum()
        {
            Assert.Equal(15, EffectiveTaxRate.Compute(10, 5, 0, Min, Max, Income));
            Assert.Equal(7, EffectiveTaxRate.Compute(10, 0, -3, Min, Max, Income));
            Assert.NotEqual(10, EffectiveTaxRate.Compute(10, 5, 0, Min, Max, Income));
        }

        [Fact]
        public void BelowTheMinimum_IsClamped()
        {
            Assert.Equal(5, EffectiveTaxRate.Compute(8, -5, 0, 5, Max, Income));
            Assert.Equal(6, EffectiveTaxRate.Compute(8, -2, 0, 5, Max, Income)); // inside: not clamped
        }

        [Fact]
        public void AboveTheMaximum_IsClamped()
        {
            Assert.Equal(30, EffectiveTaxRate.Compute(28, 5, 0, Min, Max, Income));
            Assert.Equal(29, EffectiveTaxRate.Compute(28, 1, 0, Min, Max, Income));
        }

        [Fact]
        public void NeverBelowZero_EvenWhenTheLimitsAllowIt()
        {
            Assert.Equal(0, EffectiveTaxRate.Compute(2, -5, 0, -10, Max, Income));
            Assert.Equal(1, EffectiveTaxRate.Compute(2, -1, 0, -10, Max, Income));
        }

        [Fact]
        public void ABaseOutsideTheLimits_IsNotPushedFurther_NorPulledBackPastItself()
        {
            // Vanilla rate 35 above max 30: +5 can't raise it; -2 lowers it to 33 (moving towards the limits is fine).
            Assert.Equal(35, EffectiveTaxRate.Compute(35, 5, 0, Min, Max, Income));
            Assert.Equal(33, EffectiveTaxRate.Compute(35, -2, 0, Min, Max, Income));
        }

        [Fact]
        public void DensityAndDistrict_Stack()
        {
            Assert.Equal(18, EffectiveTaxRate.Compute(10, 5, 3, Min, Max, Income));
            Assert.Equal(12, EffectiveTaxRate.Compute(10, 5, -3, Min, Max, Income));
            // Opposite adjustments cancel to the vanilla rate.
            Assert.Equal(10, EffectiveTaxRate.Compute(10, 4, -4, Min, Max, Income));
            // The stacked sum is clamped once, not each part.
            Assert.Equal(30, EffectiveTaxRate.Compute(20, 5, 10, Min, Max, Income));
        }

        [Fact]
        public void ARefund_IsAtTheVanillaRate()
        {
            Assert.Equal(10, EffectiveTaxRate.Compute(10, 5, 3, Min, Max, -500));
            Assert.Equal(10, EffectiveTaxRate.Compute(10, 5, 3, Min, Max, 0));
            Assert.Equal(18, EffectiveTaxRate.Compute(10, 5, 3, Min, Max, 1));
        }
    }
}
