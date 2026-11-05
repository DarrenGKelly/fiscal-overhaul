using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DensityTaxTableTests
    {
        [Fact]
        public void Set_ClampsToTheCap()
        {
            var table = new DensityTaxTable();

            Assert.Equal(DensityTaxTable.MaxOffset, table.Set(TaxZone.Residential, Density.High, 50));
            Assert.Equal(-DensityTaxTable.MaxOffset, table.Set(TaxZone.Residential, Density.Low, -50));
            Assert.Equal(3, table.Set(TaxZone.Residential, Density.Medium, 3));
        }

        [Fact]
        public void Set_IgnoresDensitiesTheZoneDoesNotHave()
        {
            var table = new DensityTaxTable();

            Assert.Equal(0, table.Set(TaxZone.Commercial, Density.Medium, 4));
            Assert.Equal(0, table.Get(TaxZone.Commercial, Density.Medium));
            Assert.Equal(4, table.Set(TaxZone.Commercial, Density.High, 4));
        }

        [Fact]
        public void IsSupported_MatchesTheGamesDensities()
        {
            Assert.True(DensityTaxTable.IsSupported(TaxZone.Residential, Density.Medium));
            Assert.True(DensityTaxTable.IsSupported(TaxZone.Office, Density.High));
            Assert.False(DensityTaxTable.IsSupported(TaxZone.Office, Density.Medium));
            Assert.False(DensityTaxTable.IsSupported(TaxZone.Industrial, Density.High));
        }

        [Fact]
        public void AppliedDelta_NeverTakesTheRateBelowZero()
        {
            Assert.Equal(3, DensityTaxTable.AppliedDelta(10, 3));
            Assert.Equal(-2, DensityTaxTable.AppliedDelta(2, -5));
            Assert.Equal(0, DensityTaxTable.AppliedDelta(0, -5));
        }

        [Fact]
        public void EstimateExtraRevenue_ScalesTheTaxableBase()
        {
            // Paying 1,000/month at 10% means a 10,000 base; +2 points is +200, -2 is -200.
            Assert.Equal(200, DensityTaxTable.EstimateExtraRevenue(1000, 10, 2));
            Assert.Equal(-200, DensityTaxTable.EstimateExtraRevenue(1000, 10, -2));
            Assert.Equal(0, DensityTaxTable.EstimateExtraRevenue(1000, 10, 0));
            Assert.Equal(0, DensityTaxTable.EstimateExtraRevenue(1000, 0, 2));
        }

        [Fact]
        public void SerializeParse_RoundTrips()
        {
            var table = new DensityTaxTable();
            table.Set(TaxZone.Residential, Density.Low, -2);
            table.Set(TaxZone.Office, Density.High, 5);

            var copy = DensityTaxTable.Parse(table.Serialize());

            Assert.Equal(-2, copy.Get(TaxZone.Residential, Density.Low));
            Assert.Equal(5, copy.Get(TaxZone.Office, Density.High));
            Assert.False(copy.IsEmpty);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("1,2,3")]
        public void Parse_BadInputGivesAnEmptyTable(string text)
        {
            Assert.True(DensityTaxTable.Parse(text).IsEmpty);
        }

        [Fact]
        public void Parse_ClampsOutOfRangeValues()
        {
            var table = DensityTaxTable.Parse("99,0,0,0,0,0,0,0,0,0,0,0");
            Assert.Equal(DensityTaxTable.MaxOffset, table.Get(TaxZone.Residential, Density.Low));
        }

        [Fact]
        public void EffectiveOffsets_AreTheStoredOnesWhileEnabled()
        {
            var table = new DensityTaxTable();
            table.Set(TaxZone.Residential, Density.High, 3);
            table.Set(TaxZone.Commercial, Density.Low, -2);

            int[] effective = table.EffectiveOffsets(enabled: true);

            Assert.Equal(table.ToArray(), effective);
            Assert.Equal(3, effective[DensityTaxTable.Index(TaxZone.Residential, Density.High)]);
            Assert.Equal(-2, effective[DensityTaxTable.Index(TaxZone.Commercial, Density.Low)]);
        }

        [Fact]
        public void EffectiveOffsets_AreAllZeroWhileDisabled()
        {
            var table = new DensityTaxTable();
            table.Set(TaxZone.Residential, Density.High, 3);
            table.Set(TaxZone.Commercial, Density.Low, -2);

            int[] effective = table.EffectiveOffsets(enabled: false);

            Assert.Equal(DensityTaxTable.ZoneCount * DensityTaxTable.DensityCount, effective.Length);
            Assert.All(effective, offset => Assert.Equal(0, offset));
        }

        [Fact]
        public void DisablingAndEnabling_KeepsTheStoredOffsets()
        {
            var table = new DensityTaxTable();
            table.Set(TaxZone.Office, Density.High, 4);
            string stored = table.Serialize();

            table.EffectiveOffsets(enabled: false);
            Assert.Equal(4, table.Get(TaxZone.Office, Density.High));
            Assert.Equal(stored, table.Serialize());

            Assert.Equal(4, table.EffectiveOffsets(enabled: true)[DensityTaxTable.Index(TaxZone.Office, Density.High)]);
        }

        [Fact]
        public void EffectiveOffsets_IsACopy()
        {
            var table = new DensityTaxTable();
            table.Set(TaxZone.Residential, Density.Low, 2);

            table.EffectiveOffsets(enabled: true)[DensityTaxTable.Index(TaxZone.Residential, Density.Low)] = 5;

            Assert.Equal(2, table.Get(TaxZone.Residential, Density.Low));
        }

        [Fact]
        public void IsCharged_OnlyWhenEnabledWithAnOffsetSet()
        {
            var table = new DensityTaxTable();
            Assert.False(table.IsCharged(enabled: true));  // nothing set
            Assert.False(table.IsCharged(enabled: false));

            table.Set(TaxZone.Industrial, Density.Low, 1);
            Assert.True(table.IsCharged(enabled: true));
            Assert.False(table.IsCharged(enabled: false)); // set, but switched off
        }
    }
}
