using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DensityTaxBookingTests
    {
        private const int Income = 1000;

        [Fact]
        public void PositiveDensity_IsTaxIncome_NoSubsidy()
        {
            // Zone rate 10, density +3: 130 charged, 100 income tax + 30 density, all income.
            DensityTaxBooking.Split(Income, 13, 10, out int incomeTax, out int density);
            Assert.Equal(100, incomeTax);
            Assert.Equal(30, density);
            DensityTaxBooking.Book(incomeTax, density, out int income, out int subsidy);
            Assert.Equal(130, income);
            Assert.Equal(0, subsidy);
            Assert.NotEqual(100, income); // the density part is not dropped
        }

        [Fact]
        public void NegativeDensity_IsASubsidyOfItsSize()
        {
            // Zone rate 10, density -4: 60 charged, 100 income tax and -40 density.
            DensityTaxBooking.Split(Income, 6, 10, out int incomeTax, out int density);
            Assert.Equal(-40, density);
            DensityTaxBooking.Book(incomeTax, density, out int income, out int subsidy);
            Assert.Equal(100, income);
            Assert.Equal(40, subsidy);
            Assert.NotEqual(60, income); // not netted off the tax income
        }

        [Theory]
        [InlineData(10, 13)]
        [InlineData(10, 6)]
        [InlineData(10, 0)]
        [InlineData(-3, 0)]
        [InlineData(25, 30)]
        public void Balance_IsVanillasAtTheSameRate(int rateWithoutDensity, int chargedRate)
        {
            DensityTaxBooking.Split(Income, chargedRate, rateWithoutDensity, out int incomeTax, out int density);
            DensityTaxBooking.Book(incomeTax, density, out int income, out int subsidy);
            int vanilla = DensityTaxBooking.Tax(chargedRate, Income);
            Assert.Equal(vanilla, income - subsidy);
            Assert.NotEqual(vanilla + 1, income - subsidy);
        }

        [Theory]
        [InlineData(12, 777)]
        [InlineData(-2, 777)]
        [InlineData(0, 777)]
        [InlineData(12, -50)]
        public void ZeroDensity_IsBitIdenticalToVanilla(int rate, int untaxed)
        {
            int vanilla = DensityTaxBooking.Tax(rate, untaxed);
            DensityTaxBooking.Split(untaxed, rate, rate, out int incomeTax, out int density);
            Assert.Equal(vanilla, incomeTax);
            Assert.Equal(0, density);
            DensityTaxBooking.Book(incomeTax, density, out int income, out int subsidy);
            // Vanilla's own rule: a positive item is income, a negative one a subsidy.
            Assert.Equal(vanilla > 0 ? vanilla : 0, income);
            Assert.Equal(vanilla < 0 ? -vanilla : 0, subsidy);
        }

        [Fact]
        public void Tax_RoundsAsVanilla()
        {
            Assert.Equal(93, DensityTaxBooking.Tax(12, 777)); // 93.24
            Assert.Equal(-16, DensityTaxBooking.Tax(-2, 777)); // -15.54
            Assert.NotEqual(93, DensityTaxBooking.Tax(13, 777));
        }
    }
}
