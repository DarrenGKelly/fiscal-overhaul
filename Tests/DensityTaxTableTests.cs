using System.Globalization;
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
        public void EstimateExtraRevenue_ScalesTheTaxableBase()
        {
            // A 10,000 base at a stored 10%: +2 points charges 12%, so 1,200 paid, of which +200 is the offset;
            // -2 points charges 8%, 800 paid, -200.
            Assert.Equal(200, DensityTaxTable.EstimateExtraRevenue(1200, 10, 2));
            Assert.Equal(-200, DensityTaxTable.EstimateExtraRevenue(800, 10, -2));
            Assert.Equal(0, DensityTaxTable.EstimateExtraRevenue(1000, 10, 0));
            Assert.Equal(0, DensityTaxTable.EstimateExtraRevenue(1000, 0, 2));
        }

        [Fact]
        public void EstimateExtraRevenue_TheTaxPaidAlreadyIncludesTheOffset()
        {
            // Regression: the paid amount was divided by the stored rate alone. 1,500 paid at 10% + 5 is a
            // 10,000 base, so the offset brings 500, not the 750 a 15,000 base would give.
            Assert.Equal(500, DensityTaxTable.EstimateExtraRevenue(1500, 10, 5));
            Assert.NotEqual(750, DensityTaxTable.EstimateExtraRevenue(1500, 10, 5));
        }

        [Fact]
        public void EstimateExtraRevenue_IsZeroWhenTheOffsetTakesTheRateToZero()
        {
            // 3% - 5 charges nothing (EffectiveTaxRate stops at 0), so there's no paid amount to scale back from.
            Assert.Equal(0, DensityTaxTable.EstimateExtraRevenue(100, 3, -5));
            Assert.Equal(-500, DensityTaxTable.EstimateExtraRevenue(100, 6, -5)); // 1% charged: base 10,000
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
        public void EffectiveOffsets_HandedOutDoNotChangeWithTheTable()
        {
            // The apply job reads a snapshot while the main thread may change the table (a slider, the Options
            // switch): a snapshot taken before a change must keep the old values.
            var table = new DensityTaxTable();
            table.Set(TaxZone.Residential, Density.Low, 2);
            int[] snapshot = table.EffectiveOffsets(enabled: true);

            table.Set(TaxZone.Residential, Density.Low, -4);

            Assert.Equal(2, snapshot[DensityTaxTable.Index(TaxZone.Residential, Density.Low)]);
            Assert.Equal(-4, table.EffectiveOffsets(enabled: true)[DensityTaxTable.Index(TaxZone.Residential, Density.Low)]);
        }

        [Theory]
        [InlineData("sv-SE")] // negative sign is U+2212, not '-'
        [InlineData("de-DE")]
        [InlineData("ar-SA")]
        public void SerializeParse_DoNotDependOnTheCurrentCulture(string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var table = new DensityTaxTable();
                table.Set(TaxZone.Residential, Density.Low, -2);
                table.Set(TaxZone.Office, Density.High, 5);

                string text = table.Serialize();

                Assert.Equal("-2,0,0,0,0,0,0,0,0,0,0,5", text);
                Assert.Equal(-2, DensityTaxTable.Parse(text).Get(TaxZone.Residential, Density.Low));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
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
