using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class BudgetHistoryPayloadTests
    {
        // Regression: the district view wrote nowIncome/nowExpense that Entire City didn't, under one type name,
        // and the game crashed in TypeEnd on the first switch to a district.
        [Fact]
        public void CityAndDistrictWriteTheSameProperties()
        {
            Assert.Equal(BudgetHistoryPayload.Properties(false), BudgetHistoryPayload.Properties(true));
        }

        [Fact]
        public void EveryViewCarriesTheLiveDistrictSums()
        {
            Assert.Contains("nowIncome", BudgetHistoryPayload.Properties(false));
            Assert.Contains("nowExpense", BudgetHistoryPayload.Properties(false));
            Assert.DoesNotContain("nowTotal", BudgetHistoryPayload.Properties(true));
        }

        [Fact]
        public void EveryViewCarriesTheServiceRows_NeverNull()
        {
            Assert.Contains("services", BudgetHistoryPayload.Properties(false));
            Assert.Contains("services", BudgetHistoryPayload.Properties(true));
            Assert.False(BudgetHistoryPayload.IsNull("services", false));
            Assert.False(BudgetHistoryPayload.IsNull("services", true));
            Assert.Equal(new[] { "name", "start", "values" }, BudgetHistoryPayload.ServiceProperties);
        }

        [Theory]
        [InlineData("nowIncome", false, true)]
        [InlineData("nowExpense", false, true)]
        [InlineData("nowIncome", true, false)]
        [InlineData("nowExpense", true, false)]
        [InlineData("district", false, false)]
        [InlineData("income", false, false)]
        public void OnlyCityLiveSumsAreNull(string property, bool districtView, bool expected)
        {
            Assert.Equal(expected, BudgetHistoryPayload.IsNull(property, districtView));
        }
    }
}
