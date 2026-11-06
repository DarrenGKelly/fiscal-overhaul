using System;
using System.Linq;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DistrictBudgetMathTests
    {
        private static float[] Sample(float v)
        {
            var s = new float[DistrictBudgetMath.SourceCount];
            for (int i = 0; i < s.Length; i++) s[i] = v + i * 0.5f;
            return s;
        }

        [Fact]
        public void Apportion_SplitsByWeight()
        {
            Assert.Equal(new double[] { 25, 75 }, DistrictBudgetMath.Apportion(100, new[] { 1.0, 3.0 }));
            var zero = DistrictBudgetMath.Apportion(100, new[] { 0.0, 0.0 });
            Assert.Equal(new double[] { 0, 0 }, zero);
            Assert.All(zero, v => Assert.False(double.IsNaN(v)));
        }

        [Fact]
        public void Apportion_IgnoresNegativeWeights()
        {
            Assert.Equal(new double[] { 0, 100 }, DistrictBudgetMath.Apportion(100, new[] { -5.0, 5.0 }));
        }

        [Fact]
        public void Apportion_SumsToCityValue()
        {
            var rng = new Random(42);
            var weights = Enumerable.Range(0, 7).Select(_ => rng.NextDouble() * 100).ToArray();
            var parts = DistrictBudgetMath.Apportion(12345.678, weights);
            Assert.Equal(7, parts.Length);
            Assert.Equal(12345.678, parts.Sum(), 9);
        }

        [Fact]
        public void TaxScale_PolicyStaysInDistrict()
        {
            double a = DistrictBudgetMath.AtCityRate(50, 5, 10);
            double b = DistrictBudgetMath.AtCityRate(100, 10, 10);
            Assert.Equal(100, a);
            Assert.Equal(100, b);
            double scale = DistrictBudgetMath.TaxScale(200, a + b);
            Assert.Equal(1, scale);
            Assert.Equal(50, 50 * scale);
            Assert.Equal(100, 100 * scale);
            // A zero payer rate must not divide by zero.
            Assert.Equal(50, DistrictBudgetMath.AtCityRate(50, 0, 10));
            Assert.Equal(0, DistrictBudgetMath.TaxScale(200, 0));
        }

        [Fact]
        public void IsCityOnly_CityRowsOnly()
        {
            Assert.True(DistrictBudgetMath.IsCityOnly(false, 1));  // LoanInterest
            Assert.True(DistrictBudgetMath.IsCityOnly(true, 5));   // GovernmentSubsidy
            Assert.False(DistrictBudgetMath.IsCityOnly(true, 0));  // TaxResidential
            Assert.False(DistrictBudgetMath.IsCityOnly(false, 5)); // ServiceUpkeep
            Assert.Equal(DistrictBudgetMath.SourceCount,
                DistrictBudgetMath.DistrictIncomeSources.Length + DistrictBudgetMath.DistrictExpenseSources.Length);
        }

        [Fact]
        public void History_CapsAtMaxSamples()
        {
            var h = new DistrictHistory();
            for (int i = 0; i < DistrictHistory.MaxSamples + 5; i++) h.Add(Sample(i));
            Assert.Equal(DistrictHistory.MaxSamples, h.Count);
            var series = h.Series(0);
            Assert.Equal(5.0, series[0]);
            Assert.Equal(DistrictHistory.MaxSamples + 4.0, series[series.Length - 1]);
            Assert.Throws<ArgumentException>(() => h.Add(new float[3]));
        }

        [Fact]
        public void History_FlatRoundTrip()
        {
            var h = new DistrictHistory();
            for (int i = 0; i < 4; i++) h.Add(Sample(i));
            var flat = h.ToFlat();
            Assert.Equal(4 * DistrictBudgetMath.SourceCount, flat.Length);
            var back = DistrictHistory.FromFlat(4, flat);
            Assert.Equal(4, back.Count);
            for (int slot = 0; slot < DistrictBudgetMath.SourceCount; slot++)
                Assert.Equal(h.Series(slot), back.Series(slot));
            Assert.Throws<ArgumentException>(() => DistrictHistory.FromFlat(4, new float[10]));
        }
    }
}
