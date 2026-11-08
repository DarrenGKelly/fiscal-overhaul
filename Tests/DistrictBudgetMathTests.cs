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
        public void Spread_PutsEachSlotAtItsSourceIndex()
        {
            var slots = new double[DistrictBudgetMath.SourceCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = i + 1;
            double[] income = DistrictBudgetMath.Spread(slots, true, 14, 0.0);
            double[] expense = DistrictBudgetMath.Spread(slots, false, 15, 0.0);
            Assert.Equal(14, income.Length);
            Assert.Equal(15, expense.Length);
            Assert.Equal(1, income[0]);   // slot 0 = TaxResidential (0)
            Assert.Equal(4, income[11]);  // slot 3 = TaxOffice (11)
            Assert.Equal(12, expense[0]); // slot 11 = SubsidyResidential (0)
            Assert.Equal(16, expense[5]); // slot 15 = ServiceUpkeep (5)
        }

        [Fact]
        public void Spread_FillsCityOnlySources()
        {
            var slots = new double[DistrictBudgetMath.SourceCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = 9;
            double[] income = DistrictBudgetMath.Spread(slots, true, 14, 0.0);
            for (int source = 0; source < income.Length; source++)
                Assert.Equal(DistrictBudgetMath.IsCityOnly(true, source) ? 0 : 9, income[source]);
            Assert.Throws<ArgumentException>(() => DistrictBudgetMath.Spread(new double[3], true, 14, 0.0));
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
    
        [Fact]
        public void TaxRowOf_MapsTaxAndSubsidySlots_AndNothingElse()
        {
            for (int slot = 0; slot < 4; slot++) Assert.Equal(slot, DistrictBudgetMath.TaxRowOf(slot));
            for (int k = 0; k < 4; k++) Assert.Equal(4 + k, DistrictBudgetMath.TaxRowOf(11 + k));
            foreach (int slot in new[] { 4, 5, 6, 7, 8, 9, 10, 15, -1, 16 }) Assert.Equal(-1, DistrictBudgetMath.TaxRowOf(slot));
        }

        private static double[] Weights(params double[][] areas)
        {
            var w = new double[areas.Length * DistrictBudgetMath.SourceCount];
            for (int a = 0; a < areas.Length; a++)
                for (int s = 0; s < areas[a].Length; s++) w[a * DistrictBudgetMath.SourceCount + s] = areas[a][s];
            return w;
        }

        private static double[] Row(params (int slot, double value)[] entries)
        {
            var r = new double[DistrictBudgetMath.SourceCount];
            foreach (var (slot, value) in entries) r[slot] = value;
            return r;
        }

        [Fact]
        public void Samples_FeesAndUpkeep_SumToTheVanillaRow()
        {
            // Two districts and outside; electricity (4) 1:3:0, upkeep (15) 2:1:1.
            double[] weights = Weights(Row((4, 1), (15, 2)), Row((4, 3), (15, 1)), Row((15, 1)));
            double[] vanilla = Row((4, 400), (15, 1000));
            float[][] samples = DistrictBudgetMath.Samples(weights, 3, vanilla, new double[DistrictBudgetMath.TaxRowCount]);
            Assert.Equal(100f, samples[0][4], 3);
            Assert.Equal(300f, samples[1][4], 3);
            Assert.Equal(0f, samples[2][4], 3);
            Assert.Equal(500f, samples[0][15], 3);
            Assert.Equal(1000.0, samples[0][15] + samples[1][15] + samples[2][15], 2);
            // A source nobody weighs gets nothing, not a share.
            Assert.Equal(0f, samples[0][5]);
        }

        [Fact]
        public void Samples_Taxes_ArePaidTimesTheScale_SoADiscountStaysInItsDistrict()
        {
            // Residential (slot 0): district 0 paid 100 at the city rate; district 1 paid 50 at half the rate (a
            // discount), 100 at the city rate. Vanilla row 400 against 200 at city rate: scale 2.
            double[] weights = Weights(Row((0, 100)), Row((0, 50)));
            double[] atCity = new double[DistrictBudgetMath.TaxRowCount];
            atCity[0] = 200;
            float[][] samples = DistrictBudgetMath.Samples(weights, 2, Row((0, 400)), atCity);
            Assert.Equal(200f, samples[0][0], 3);
            Assert.Equal(100f, samples[1][0], 3); // not 200: the discount shows
        }

        [Fact]
        public void AtCityRate_WithoutAPolicy_IsExactlyWhatWasPaid()
        {
            // #30: the restatement is by the district's rate, so a district without a tax policy restates 1:1, exactly,
            // even for sums where paid * rate / rate would not round-trip in floating point.
            foreach (double paid in new[] { 16526552.0, 0.1 + 0.2, -4321.5, 1e15 + 1 })
                foreach (double rate in new[] { 3.0, 7.0, 13.0, -2.0 })
                    Assert.Equal(paid, DistrictBudgetMath.AtCityRate(paid, rate, rate));
            // Fail case: a Low Commercial Tax district (rate 8 against the city's 10) restates upward.
            Assert.Equal(125, DistrictBudgetMath.AtCityRate(100, 8, 10));
            Assert.NotEqual(100, DistrictBudgetMath.AtCityRate(100, 8, 10));
        }

        // One sample from payers (area, paid, district rate, city rate, stored, without density, charged), weighed by
        // PayerTaxWeights on the residential row (tax slot 0, subsidy slot 11).
        private static float[][] TaxSample(int areas, double[] vanilla, params (int area, double paid, int dRate, int cRate, int stored, int noDensity, int charged)[] payers)
        {
            var weights = new double[areas * DistrictBudgetMath.SourceCount];
            var atCity = new double[DistrictBudgetMath.TaxRowCount];
            foreach (var p in payers)
            {
                DistrictBudgetMath.PayerTaxWeights(p.paid, p.dRate, p.cRate, p.stored, p.noDensity, p.charged,
                    out double tax, out double taxAt, out double sub, out double subAt);
                weights[p.area * DistrictBudgetMath.SourceCount + 0] += tax;
                weights[p.area * DistrictBudgetMath.SourceCount + 11] += sub;
                atCity[0] += taxAt;
                atCity[4] += subAt;
            }
            return DistrictBudgetMath.Samples(weights, areas, vanilla, atCity);
        }

        [Fact]
        public void PayerCounts_WhileTheEngineCharges_OnlyWithACurrentRecord()
        {
            // Vanilla charges: every payer weighs, as before.
            Assert.True(DistrictBudgetMath.PayerCounts(false, false, 0, 100));
            // Engine: a payer with paid > 0 but no record (never charged), or an old one (stopped earning), weighs 0.
            Assert.False(DistrictBudgetMath.PayerCounts(true, false, 0, 100));
            Assert.False(DistrictBudgetMath.PayerCounts(true, true, 80, 100));
            Assert.True(DistrictBudgetMath.PayerCounts(true, true, 90, 100));
        }

        [Fact]
        public void StalePayer_WeighsNothing_AndTheSharesStillSumToTheRow()
        {
            // District 0 has a current payer (paid 1000); district 1 only a stale one (paid 500, no current record).
            // The city books 1000 (vanilla's row from the current payer).
            var payers = new[] { (area: 0, paid: 1000.0, current: true), (area: 1, paid: 500.0, current: false) };
            var weights = new double[2 * DistrictBudgetMath.SourceCount];
            var atCity = new double[DistrictBudgetMath.TaxRowCount];
            foreach (var p in payers)
            {
                if (!DistrictBudgetMath.PayerCounts(true, p.current, p.current ? 100 : 50, 100)) continue;
                DistrictBudgetMath.PayerTaxWeights(p.paid, 10, 10, 10, 10, 10, out double tax, out double taxAt, out _, out _);
                weights[p.area * DistrictBudgetMath.SourceCount] += tax;
                atCity[0] += taxAt;
            }
            float[][] s = DistrictBudgetMath.Samples(weights, 2, Row((0, 1000)), atCity);
            Assert.Equal(1000f, s[0][0], 1);
            Assert.Equal(0f, s[1][0]);
            Assert.Equal(1000.0, s[0][0] + s[1][0], 1);
            // Fail case: weighing the stale payer gives its district a share the city never receives.
            float[][] old = DistrictBudgetMath.Samples(Weights(Row((0, 1000)), Row((0, 500))), 2, Row((0, 1000)), new double[] { 1500, 0, 0, 0, 0, 0, 0, 0 });
            Assert.NotEqual(0f, old[1][0]);
        }

        [Fact]
        public void PayerTaxWeights_PositiveDensity_DistrictsSumToTheBookedRow()
        {
            // District 0 at density +10 (stored 10, charged 20): paid 2000. District 1 at vanilla: paid 1000. The city
            // books vanilla's 2000 + the density part 1000 = 3000 (TaxBookingSystem).
            float[][] s = TaxSample(2, Row((0, 3000)), (0, 2000, 10, 10, 10, 10, 20), (1, 1000, 10, 10, 10, 10, 10));
            Assert.Equal(3000.0, s[0][0] + s[1][0], 1);
            Assert.Equal(2000f, s[0][0], 1);
            Assert.Equal(0f, s[0][11] + s[1][11]);
            Assert.NotEqual(1500f, s[0][0], 1); // not split as if density were vanilla tax
        }

        [Fact]
        public void PayerTaxWeights_NegativeDensity_GoesToTheSubsidySlot()
        {
            // District 0 at density -5 (stored 10, charged 5): paid 500 of a 1000 vanilla charge. The city books 2000 tax
            // income (vanilla's, both payers) and 500 Residential subsidy.
            float[][] s = TaxSample(2, Row((0, 2000), (11, 500)), (0, 500, 10, 10, 10, 10, 5), (1, 1000, 10, 10, 10, 10, 10));
            Assert.Equal(1000f, s[0][0], 1);
            Assert.Equal(1000f, s[1][0], 1);
            Assert.Equal(500f, s[0][11], 1);
            Assert.Equal(0f, s[1][11]);
            Assert.Equal(500f, s[0][0] - s[0][11], 1); // the district's net is what it paid
            // Fail case: weighing the paid amount alone puts district 0's lower tax in the tax slot and no subsidy.
            DistrictBudgetMath.PayerTaxWeights(500, 10, 10, 10, 10, 10, out double tax, out _, out double sub, out _);
            Assert.Equal(500, tax);
            Assert.Equal(0, sub);
        }

        [Fact]
        public void PayerTaxWeights_DistrictOverride_IsTaxIncome()
        {
            // A district rate of 14 against the city's 10 (stored 10, no density): paid 1400, all tax.
            DistrictBudgetMath.PayerTaxWeights(1400, 10, 10, 10, 14, 14, out double tax, out double taxAt, out double sub, out _);
            Assert.Equal(1400, tax, 6);
            Assert.Equal(1400, taxAt, 6);
            Assert.Equal(0, sub);
            // A lower district rate is less tax, not a subsidy.
            DistrictBudgetMath.PayerTaxWeights(600, 10, 10, 10, 6, 6, out tax, out _, out sub, out _);
            Assert.Equal(600, tax, 6);
            Assert.Equal(0, sub);
        }

        [Theory]
        [InlineData(1000, 10, 10)]
        [InlineData(800, 8, 10)]   // Low Commercial Tax: restated
        [InlineData(-300, 10, 10)] // a vanilla subsidy
        public void PayerTaxWeights_AllZero_IsTodays(double paid, int districtRate, int cityRate)
        {
            DistrictBudgetMath.PayerTaxWeights(paid, districtRate, cityRate, 12, 12, 12, out double tax, out double taxAt, out double sub, out double subAt);
            double atCity = DistrictBudgetMath.AtCityRate(paid, districtRate, cityRate);
            Assert.Equal(paid > 0 ? paid : 0, tax);
            Assert.Equal(paid > 0 ? atCity : 0, taxAt);
            Assert.Equal(paid < 0 ? -paid : 0, sub);
            Assert.Equal(paid < 0 ? -atCity : 0, subAt);
            // Fail case: with a density part the split changes.
            DistrictBudgetMath.PayerTaxWeights(1000, 10, 10, 10, 10, 5, out tax, out _, out _, out _);
            Assert.NotEqual(1000, tax);
        }

        [Fact]
        public void Samples_Taxes_WithoutAPolicy_SumToTheVanillaRow_WithOne_FallShort()
        {
            // #30: payers restated by their district's rate. Residential (0): no policy anywhere, so atCity = paid and the
            // districts sum to the vanilla row whatever the payers' own stored rates were. Commercial (1): district 1 has
            // Low Commercial Tax (8 against the city's 10), so the row falls short by exactly its discount.
            double[] weights = Weights(Row((0, 300), (1, 100)), Row((0, 700), (1, 80)), Row((0, 1)));
            double[] atCity = new double[DistrictBudgetMath.TaxRowCount];
            atCity[0] = DistrictBudgetMath.AtCityRate(300, 10, 10) + DistrictBudgetMath.AtCityRate(700, 12, 12) + DistrictBudgetMath.AtCityRate(1, 9, 9);
            atCity[1] = DistrictBudgetMath.AtCityRate(100, 10, 10) + DistrictBudgetMath.AtCityRate(80, 8, 10);
            double[] vanilla = Row((0, 5005), (1, 600));
            float[][] samples = DistrictBudgetMath.Samples(weights, 3, vanilla, atCity);
            Assert.Equal(5005.0, samples[0][0] + samples[1][0] + samples[2][0], 1);
            double commercial = samples[0][1] + samples[1][1] + samples[2][1];
            Assert.Equal(600.0 * 180 / 200, commercial, 2);
            Assert.True(commercial < 600.0);
        }

        [Fact]
        public void Samples_Subsidies_UseTheirOwnTaxRow()
        {
            double[] weights = Weights(Row((11, 30)), Row((11, 10)));
            double[] atCity = new double[DistrictBudgetMath.TaxRowCount];
            atCity[4] = 40;   // residential subsidies
            atCity[0] = 1000; // residential tax income, must not be used for the subsidy slot
            float[][] samples = DistrictBudgetMath.Samples(weights, 2, Row((11, 80)), atCity);
            Assert.Equal(60f, samples[0][11], 3);
            Assert.Equal(20f, samples[1][11], 3);
        }

        [Fact]
        public void Samples_RejectsMismatchedSizes()
        {
            var atCity = new double[DistrictBudgetMath.TaxRowCount];
            Assert.Throws<ArgumentException>(() => DistrictBudgetMath.Samples(new double[DistrictBudgetMath.SourceCount], 2, Row(), atCity));
            Assert.Throws<ArgumentException>(() => DistrictBudgetMath.Samples(Weights(Row()), 1, new double[3], atCity));
            Assert.Throws<ArgumentException>(() => DistrictBudgetMath.Samples(Weights(Row()), 1, Row(), new double[2]));
        }
    }
}
