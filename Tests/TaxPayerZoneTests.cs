using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxPayerZoneTests
    {
        [Fact]
        public void Households_AreResidential()
        {
            Assert.Equal(TaxZone.Residential, TaxPayerZone.Of(isHousehold: true, isCommercial: false, weightlessOutput: false));
            Assert.Equal(TaxZone.Residential, TaxPayerZone.Of(isHousehold: true, isCommercial: true, weightlessOutput: true));
        }

        [Fact]
        public void ServiceCompanies_AreCommercial()
        {
            Assert.Equal(TaxZone.Commercial, TaxPayerZone.Of(false, true, false));
            Assert.Equal(TaxZone.Commercial, TaxPayerZone.Of(false, true, true));
        }

        [Fact]
        public void ProcessingCompanies_AreIndustrial_OrOfficeWhenTheOutputWeighsNothing()
        {
            Assert.Equal(TaxZone.Industrial, TaxPayerZone.Of(false, false, false));
            Assert.Equal(TaxZone.Office, TaxPayerZone.Of(false, false, true));
        }
    }
}
