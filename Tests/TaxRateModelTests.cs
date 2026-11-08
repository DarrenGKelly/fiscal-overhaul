using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxRateModelTests
    {
        private const int A = 42, AV = 1;  // district A
        private const int B = 43, BV = 1;  // district B
        private const int Untaxed = 1000;

        /// <summary>
        /// A vanilla rate array: base 10; Residential zone 10 with level offsets -2..+2 (levels pay 8..12); Commercial
        /// zone 12 (resource 3: +1); Industrial 10, Office 11 (shared resource offsets; resource 7: +2).
        /// </summary>
        private static int[] CityRates()
        {
            var r = new int[TaxRateModel.VanillaRateCount];
            r[0] = 10;
            r[1] = 0; r[2] = 2; r[3] = 0; r[4] = 1;
            for (int l = 0; l < 5; l++) r[5 + l] = l - 2;
            r[10 + 3] = 1;
            r[51 + 7] = 2;
            return r;
        }

        private static TaxLimits Limits()
        {
            var l = TaxLimits.Wide();
            l.JobLevelMin = 0; l.JobLevelMax = 30;
            l.ResourceMin = 0; l.ResourceMax = 30;
            for (int z = 0; z < 4; z++) { l.ZoneMin[z] = 0; l.ZoneMax[z] = 30; }
            return l;
        }

        private static int Delta(int[] deltas, TaxZone zone, int key) => deltas[TaxRateModel.DeltaIndex(zone, key)];

        [Fact]
        public void CityRates_MirrorVanillasGetters()
        {
            int[] r = CityRates();
            Assert.Equal(10, TaxRateModel.CityZoneRate(r, TaxZone.Residential));
            Assert.Equal(8, TaxRateModel.CityItemRate(r, TaxZone.Residential, 0));
            Assert.Equal(12, TaxRateModel.CityItemRate(r, TaxZone.Residential, 4));
            Assert.Equal(13, TaxRateModel.CityItemRate(r, TaxZone.Commercial, 3));
            Assert.Equal(12, TaxRateModel.CityItemRate(r, TaxZone.Industrial, 7));
            Assert.Equal(13, TaxRateModel.CityItemRate(r, TaxZone.Office, 7)); // the shared offset on Office's zone rate
            Assert.NotEqual(TaxRateModel.CityItemRate(r, TaxZone.Industrial, 7), TaxRateModel.CityItemRate(r, TaxZone.Office, 7));
        }

        [Fact]
        public void NoOverrides_EveryDeltaIs0_SoTheChargeIsVanillas()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.ZoneRate, 0, 0, B, BV, 20); // another district's override changes nothing for A
            int[] deltas = TaxRateModel.DistrictDeltas(table, A, AV, CityRates(), Limits());

            Assert.All(deltas, d => Assert.Equal(0, d));
            int household = TaxRateModel.HouseholdDelta(deltas, new[] { 0, 2, 4 }, new[] { 1000, 1200, 1500, 2000, 2500 });
            Assert.Equal(0, household);
            Assert.Equal(11, EffectiveTaxRate.Compute(11, 0, household, 0, 30, Untaxed));
            // Fail case: district B's own deltas are not 0.
            Assert.Contains(TaxRateModel.DistrictDeltas(table, B, BV, CityRates(), Limits()), d => d != 0);
        }

        [Fact]
        public void EducationOverrideOnOneLevel_MovesOnlyWorkersOfThatLevel()
        {
            var table = new TaxRateTable();
            int[] rates = CityRates();
            // District A: Highly Educated (level 4) at 20% instead of the city's 12%.
            Assert.True(TaxRateModel.SetDistrictValue(table, TaxType.ResourceRate, TaxZone.Residential, 4, A, AV, 20, rates, Limits()));
            int[] deltas = TaxRateModel.DistrictDeltas(table, A, AV, rates, Limits());

            Assert.Equal(8, Delta(deltas, TaxZone.Residential, 4));
            for (int level = 0; level < 4; level++) Assert.Equal(0, Delta(deltas, TaxZone.Residential, level));

            Assert.Equal(8, TaxRateModel.HouseholdDelta(deltas, new[] { 4, 4 }, null));
            Assert.Equal(0, TaxRateModel.HouseholdDelta(deltas, new[] { 1, 2 }, null));
            // Mixed household, equal wages: half the difference.
            Assert.Equal(4, TaxRateModel.HouseholdDelta(deltas, new[] { 4, 0 }, null));
        }

        [Fact]
        public void HouseholdDelta_IsWageWeighted()
        {
            var deltas = new int[TaxRateModel.BlockSize];
            deltas[TaxRateModel.DeltaIndex(TaxZone.Residential, 4)] = 8;
            // Level 4 earns 3000, level 0 earns 1000: (3000 * 8 + 1000 * 0) / 4000 = 6.
            Assert.Equal(6, TaxRateModel.HouseholdDelta(deltas, new[] { 4, 0 }, new[] { 1000, 0, 0, 0, 3000 }));
            Assert.NotEqual(4, TaxRateModel.HouseholdDelta(deltas, new[] { 4, 0 }, new[] { 1000, 0, 0, 0, 3000 }));
            // No worker: level 0's delta (vanilla taxes other income at level 0's rate).
            Assert.Equal(0, TaxRateModel.HouseholdDelta(deltas, new int[0], null));
        }

        [Fact]
        public void ZoneOverride_ShiftsEveryLevelLikeVanillasZoneSlider()
        {
            var table = new TaxRateTable();
            int[] rates = CityRates();
            TaxRateModel.SetDistrictValue(table, TaxType.ZoneRate, TaxZone.Residential, 0, A, AV, 13, rates, Limits());

            int[] deltas = TaxRateModel.DistrictDeltas(table, A, AV, rates, Limits());
            for (int level = 0; level < 5; level++) Assert.Equal(3, Delta(deltas, TaxZone.Residential, level));
            Assert.Equal(0, Delta(deltas, TaxZone.Commercial, 3));
            Assert.Equal(15, TaxRateModel.DistrictItemRate(table, A, AV, rates, Limits(), TaxZone.Residential, 4));
        }

        [Fact]
        public void LevelOverride_IsStoredAsAnOffset_AndFollowsTheDistrictsZoneRate()
        {
            var table = new TaxRateTable();
            int[] rates = CityRates();
            TaxRateModel.SetDistrictValue(table, TaxType.ResourceRate, TaxZone.Residential, 4, A, AV, 20, rates, Limits());
            Assert.Equal(10, table.Get(TaxType.ResourceRate, 0, 4, A, AV)); // 20 - zone 10

            TaxRateModel.SetDistrictValue(table, TaxType.ZoneRate, TaxZone.Residential, 0, A, AV, 12, rates, Limits());
            Assert.Equal(22, TaxRateModel.DistrictItemRate(table, A, AV, rates, Limits(), TaxZone.Residential, 4));
            Assert.Equal(12, TaxRateModel.DistrictItemRate(table, A, AV, rates, Limits(), TaxZone.Residential, 2)); // zone 12 + city offset 0
        }

        [Fact]
        public void CompanyResourceOverride_MovesOnlyThatResourcesCompanies()
        {
            var table = new TaxRateTable();
            int[] rates = CityRates();
            // Commercial resource 3: 13% in the city, 18% in A.
            TaxRateModel.SetDistrictValue(table, TaxType.ResourceRate, TaxZone.Commercial, 3, A, AV, 18, rates, Limits());
            int[] deltas = TaxRateModel.DistrictDeltas(table, A, AV, rates, Limits());

            Assert.Equal(5, Delta(deltas, TaxZone.Commercial, 3));
            Assert.Equal(0, Delta(deltas, TaxZone.Commercial, 4));
            Assert.Equal(0, Delta(deltas, TaxZone.Industrial, 3));
            // A company whose vanilla rate is 13 pays 18 in district A.
            Assert.Equal(18, EffectiveTaxRate.Compute(13, 0, Delta(deltas, TaxZone.Commercial, 3), 0, 30, Untaxed));
        }

        [Fact]
        public void DensityTax_IsItsOwnRate_DistrictOverridesTheCitys_AndStacks()
        {
            var table = new TaxRateTable();
            table.Set(TaxType.Density, (int)TaxZone.Residential, (int)Density.High, 2);
            TaxRateModel.SetDistrictValue(table, TaxType.Density, TaxZone.Residential, (int)Density.High, A, AV, 5, CityRates(), Limits());

            int high = DensityTaxTable.Index(TaxZone.Residential, Density.High);
            Assert.Equal(2, TaxRateModel.DistrictDensityRates(table, TaxRateTable.CityArea, 0, true)[high]);
            Assert.Equal(5, TaxRateModel.DistrictDensityRates(table, A, AV, true)[high]);
            Assert.Equal(2, TaxRateModel.DistrictDensityRates(table, B, BV, true)[high]); // B follows the city
            Assert.Equal(0, TaxRateModel.DistrictDensityRates(table, A, AV, false)[high]); // density taxes off

            // Stacks on income tax and the district's income difference: 12 + 8 + 5.
            Assert.Equal(25, EffectiveTaxRate.Compute(12, 5, 8, 0, 30, Untaxed));
        }

        [Fact]
        public void TheSumIsClampedLikeEffectiveTaxRate()
        {
            var table = new TaxRateTable();
            int[] rates = CityRates();
            // A full rate beyond the limits is clamped when set (vanilla's setters clamp too).
            TaxRateModel.SetDistrictValue(table, TaxType.ResourceRate, TaxZone.Residential, 4, A, AV, 99, rates, Limits());
            Assert.Equal(30, TaxRateModel.DistrictItemRate(table, A, AV, rates, Limits(), TaxZone.Residential, 4));

            int delta = Delta(TaxRateModel.DistrictDeltas(table, A, AV, rates, Limits()), TaxZone.Residential, 4);
            Assert.Equal(18, delta);
            Assert.Equal(30, EffectiveTaxRate.Compute(12, 10, delta, 0, 30, Untaxed));      // 40 clamped to 30
            Assert.Equal(12, EffectiveTaxRate.Compute(12, 10, delta, 0, 30, -Untaxed));     // a refund: vanilla rate
            Assert.Equal(0, EffectiveTaxRate.Compute(3, -5, -10, 0, 30, Untaxed));          // never below 0
        }

        [Fact]
        public void SettingTheCitysValueBack_FollowsTheCityAgain()
        {
            var table = new TaxRateTable();
            int[] rates = CityRates();
            TaxRateModel.SetDistrictValue(table, TaxType.ResourceRate, TaxZone.Residential, 4, A, AV, 20, rates, Limits());
            TaxRateModel.SetDistrictValue(table, TaxType.ZoneRate, TaxZone.Residential, 0, A, AV, 14, rates, Limits());
            table.Set(TaxType.Density, 0, 2, 3);
            TaxRateModel.SetDistrictValue(table, TaxType.Density, TaxZone.Residential, 2, A, AV, 6, rates, Limits());
            Assert.Equal(3, table.Count - 1);

            TaxRateModel.SetDistrictValue(table, TaxType.ResourceRate, TaxZone.Residential, 4, A, AV, 16, rates, Limits()); // 14 + city offset 2
            TaxRateModel.SetDistrictValue(table, TaxType.ZoneRate, TaxZone.Residential, 0, A, AV, 10, rates, Limits());
            TaxRateModel.SetDistrictValue(table, TaxType.Density, TaxZone.Residential, 2, A, AV, 3, rates, Limits());

            Assert.False(table.AnyForArea(A, AV));
        }

        [Fact]
        public void SetDistrictValue_RejectsTheCityAndBadKeys()
        {
            var table = new TaxRateTable();
            Assert.False(TaxRateModel.SetDistrictValue(table, TaxType.ZoneRate, TaxZone.Residential, 0, TaxRateTable.CityArea, 0, 12, CityRates(), Limits()));
            Assert.False(TaxRateModel.SetDistrictValue(table, TaxType.ResourceRate, TaxZone.Residential, 5, A, AV, 12, CityRates(), Limits()));
            Assert.False(TaxRateModel.SetDistrictValue(table, TaxType.Density, TaxZone.Industrial, 2, A, AV, 4, CityRates(), Limits()));
            Assert.Equal(0, table.Count);
        }

        [Theory]
        [InlineData(10, 4, 3)]   // 2.5 rounds away from zero
        [InlineData(-10, 4, -3)]
        [InlineData(9, 4, 2)]    // 2.25
        [InlineData(5, 0, 0)]    // no weight
        public void WeightedDelta_Rounds(long sum, long weight, int expected)
        {
            Assert.Equal(expected, TaxRateModel.WeightedDelta(sum, weight));
        }
    }
}
