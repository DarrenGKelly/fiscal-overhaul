using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class BudgetLoadTimingTests
    {
        [Fact]
        public void LogsTheFirstBuildsAfterASubscribe_ThenStops()
        {
            var timing = new BudgetLoadTiming();
            timing.Subscribed();
            for (int i = 1; i <= BudgetLoadTiming.BuildsPerOpen; i++)
            {
                Assert.True(timing.TakeBuild());
                Assert.Equal(i, timing.Build);
            }
            Assert.False(timing.TakeBuild());
        }

        [Fact]
        public void LogsNothingBeforeTheUiSubscribes()
        {
            Assert.False(new BudgetLoadTiming().TakeBuild());
        }

        [Fact]
        public void ANewSubscribeLogsAgainFromBuildOne()
        {
            var timing = new BudgetLoadTiming();
            timing.Subscribed();
            for (int i = 0; i < 5; i++) timing.TakeBuild();
            timing.Subscribed();
            Assert.True(timing.TakeBuild());
            Assert.Equal(1, timing.Build);
        }

        [Fact]
        public void LogEveryBuildLogsWithoutASubscribe()
        {
            var timing = new BudgetLoadTiming { LogEveryBuild = true };
            for (int i = 0; i < 10; i++) Assert.True(timing.TakeBuild());
        }

        [Fact]
        public void LineCarriesTheTimesAndSizes_InvariantCulture()
        {
            Assert.Equal("BudgetChart: build 1 compute 1.23 ms, write 0.50 ms; 3000 samples, 97 points, 29 series, 12 services, view city",
                BudgetLoadTiming.Line(1, 1.234, 0.5, 3000, 97, 29, 12, ""));
            Assert.EndsWith("view 12:1", BudgetLoadTiming.Line(2, 0, 0, 1, 1, 29, 0, "12:1"));
        }

        [Fact]
        public void UiLineIsOneBoundedLine()
        {
            Assert.Equal("BudgetChart UI: data +0 ms", BudgetLoadTiming.UiLine("data +0 ms"));
            Assert.Equal("BudgetChart UI: a b", BudgetLoadTiming.UiLine("a\nb"));
            Assert.Equal("BudgetChart UI: ", BudgetLoadTiming.UiLine(null));
            Assert.Equal("BudgetChart UI: ".Length + BudgetLoadTiming.MaxUiText, BudgetLoadTiming.UiLine(new string('x', 1000)).Length);
        }
    }
}
