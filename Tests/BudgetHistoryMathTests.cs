using System.Linq;
using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class BudgetHistoryMathTests
    {
        [Fact]
        public void MonthlyRates_SumsTheTrailingWindow()
        {
            // 40 samples of 10 each: the rate builds up to a full month (320) and then holds.
            var deltas = Enumerable.Repeat(10.0, 40).ToArray();
            var rates = BudgetHistoryMath.MonthlyRates(deltas);

            Assert.Equal(10, rates[0]);
            Assert.Equal(320, rates[31]);
            Assert.Equal(320, rates[39]);
        }

        [Fact]
        public void MonthlyRates_DropsSamplesOlderThanTheWindow()
        {
            // A one-off spike leaves the rate once it is older than the window, and not before.
            var deltas = new double[5];
            deltas[0] = 100;
            var rates = BudgetHistoryMath.MonthlyRates(deltas, window: 3);

            Assert.Equal(new double[] { 100, 100, 100, 0, 0 }, rates);
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
