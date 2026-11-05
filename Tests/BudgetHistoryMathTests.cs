using System.Linq;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class BudgetHistoryMathTests
    {
        [Fact]
        public void MonthlyRates_IsEachSampleTimesSamplesPerMonth()
        {
            // A sample holds 1/32 of the monthly rate at its time.
            var rates = BudgetHistoryMath.MonthlyRates(new[] { 10.0, 0.0, -2.5 });

            Assert.Equal(new double[] { 320, 0, -80 }, rates);
        }

        [Fact]
        public void MonthlyRates_NewestPointMatchesTheCurrentRateNotTheMonthAverage()
        {
            // Regression: a growing city (rate climbing from 0 to 630/month over two months). The vanilla panel shows
            // the current rate, 630; the old trailing-month sum gave the past month's average, about 475.
            var deltas = Enumerable.Range(0, 64).Select(i => i / 3.2).ToArray(); // last sample = 63/3.2 = 19.6875
            var rates = BudgetHistoryMath.MonthlyRates(deltas);

            Assert.Equal(630, rates[63], 6);
            Assert.NotEqual(deltas.Skip(32).Sum(), rates[63], 6);
            Assert.Equal(0, rates[0]);
        }

        [Fact]
        public void MonthlyRates_RejectsANonPositiveSamplesPerMonth()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => BudgetHistoryMath.MonthlyRates(new[] { 1.0 }, 0));
        }

        [Fact]
        public void WithLiveNow_EndsOnTheLiveValue()
        {
            // Regression: the chart's Now was the last saved sample (Expenses ¢268k) while the row showed the live ¢284,785.
            var series = BudgetHistoryMath.WithLiveNow(new double[] { 260000, 268000 }, 284785);

            Assert.Equal(new double[] { 260000, 268000, 284785 }, series);
            Assert.NotEqual(268000, series[series.Length - 1]);
        }

        [Fact]
        public void WithLiveNow_GivesASourceWithNoHistoryItsLiveValue()
        {
            Assert.Equal(new double[] { 52910 }, BudgetHistoryMath.WithLiveNow(new double[0], 52910));
        }

        [Fact]
        public void CompleteSamples_LeavesOutTheSampleStillFillingUp()
        {
            Assert.Equal(31, BudgetHistoryMath.CompleteSamples(32));
            Assert.Equal(1, BudgetHistoryMath.CompleteSamples(2));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        public void CompleteSamples_NoneInANewCitysFirstSample(int sampleCount)
        {
            // Regression: a lone sample was kept although it's the one still filling up, so a new city's lines
            // started at a partial value and jumped to the live one.
            Assert.Equal(0, BudgetHistoryMath.CompleteSamples(sampleCount));
        }

        [Fact]
        public void SamplesForSpan_ClampsToAvailableHistory()
        {
            Assert.Equal(33, BudgetHistoryMath.SamplesForSpan(1, 1000));
            Assert.Equal(50, BudgetHistoryMath.SamplesForSpan(12, 50));
        }

        [Fact]
        public void SamplesForSpan_AllHistoryAndEmpty()
        {
            Assert.Equal(1000, BudgetHistoryMath.SamplesForSpan(0, 1000));
            Assert.Equal(0, BudgetHistoryMath.SamplesForSpan(3, 0));
        }

        [Fact]
        public void PickIndices_ReturnsEverySampleWhenTheSpanFits()
        {
            Assert.Equal(new[] { 7, 8, 9 }, BudgetHistoryMath.PickIndices(10, 3, 100));
        }

        [Fact]
        public void PickIndices_DownsamplesLongSpansAndEndsOnTheNewestSample()
        {
            var picked = BudgetHistoryMath.PickIndices(10000, 5000, 200);

            Assert.Equal(200, picked.Length);
            Assert.Equal(5000, picked[0]);
            Assert.Equal(9999, picked[picked.Length - 1]);
            Assert.True(picked.Zip(picked.Skip(1), (a, b) => b > a).All(x => x), "indices must strictly increase");
        }

        [Fact]
        public void MonthsAgo_IsZeroForNewestAndNegativeBefore()
        {
            Assert.Equal(0, BudgetHistoryMath.MonthsAgo(99, 100));
            Assert.Equal(-1, BudgetHistoryMath.MonthsAgo(67, 100));
        }
    }
}
