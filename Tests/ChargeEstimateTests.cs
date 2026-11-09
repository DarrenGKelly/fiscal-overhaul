using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    /// <summary>
    /// The Taxation estimates from the engine's charge records (<see cref="ChargeWindow"/>,
    /// <see cref="PayerTaxParts.FromCharge"/>): only payers the engine charged in the last 1/32 month count, from the
    /// income it charged, so a settled window's estimate is what the ledger books.
    /// </summary>
    public class ChargeEstimateTests
    {
        private const int Min = 0, Max = 30, Charges = 32;

        [Fact]
        public void ChargeWindow_IsTheLastSixteenTicks()
        {
            Assert.True(ChargeWindow.IsCurrent(100, 100));
            Assert.True(ChargeWindow.IsCurrent(85, 100));
            Assert.False(ChargeWindow.IsCurrent(84, 100)); // its group was charged again since, and it had no income
            Assert.False(ChargeWindow.IsCurrent(101, 100));
            Assert.True(ChargeWindow.IsExpired(68, 100));
            Assert.False(ChargeWindow.IsExpired(69, 100));
        }

        [Fact]
        public void APayerWithPaidButNoChargedIncome_AddsNothing()
        {
            // A household that stopped earning: m_AverageTaxPaid still 1200 from its last payment, but the engine skips
            // it (no untaxed income) and the city books nothing from it.
            PayerTaxParts fromCharge = PayerTaxParts.FromCharge(0, 10, 10, 0, Min, Max, Charges);
            Assert.Equal(0, fromCharge.Base);
            Assert.Equal(0, fromCharge.DensityPart);
            // Fail case: the paid-amount base counts it.
            PayerTaxParts fromPaid = PayerTaxParts.Of(1200, 0, 10, 10, 0, Min, Max, Charges);
            Assert.NotEqual(0, fromPaid.DensityPart);
            // A loss (negative untaxed income) books nothing either.
            Assert.Equal(0, PayerTaxParts.FromCharge(-500, 10, 10, 0, Min, Max, Charges).DensityPart);
        }

        [Fact]
        public void SettledWindow_EstimateEqualsTheBookedDensity()
        {
            // 16 frames of payers at stored rates 10 and 25 (25 + 10 clamps at 30), density +10, charged by the engine.
            int[] untaxed = { 1000, 2500, 400, 3200, 800, 1200, 600, 5000, 900, 1100, 700, 1500, 2000, 300, 500, 3000 };
            const int density = 10;
            var ledger = new TaxBookingLedger();
            double estimate = 0;
            for (int f = 0; f < untaxed.Length; f++)
            {
                int stored = f % 2 == 0 ? 10 : 25;
                int charged = EffectiveTaxRate.Compute(stored, density, 0, Min, Max, untaxed[f]);
                int withoutDensity = EffectiveTaxRate.Compute(stored, 0, 0, Min, Max, untaxed[f]);
                DensityTaxBooking.Parts(DensityTaxBooking.Tax(stored, untaxed[f]), DensityTaxBooking.Tax(withoutDensity, untaxed[f]),
                    DensityTaxBooking.Tax(charged, untaxed[f]), out int district, out int part);
                ledger.AddSums(f, TaxZone.Residential, district, part > 0 ? part : 0, part < 0 ? part : 0, 0);
                estimate += PayerTaxParts.FromCharge(untaxed[f], stored, density, 0, Min, Max, Charges).DensityPart;
            }
            ledger.Monthly(TaxZone.Residential, out _, out long booked, out _);
            Assert.Equal(booked, estimate, 6);
            // Fail case: the unclamped base x rate is more than is booked.
            long sum = 0;
            foreach (int u in untaxed) sum += u;
            Assert.NotEqual(sum * Charges * density / 100.0, estimate, 6);
        }

        [Fact]
        public void AMovedSlider_RestatesTheEstimateAtOnce()
        {
            // Charged at +10 last time; the slider is now at -5: the estimate is the base at -5 before any new charge.
            PayerTaxParts now = PayerTaxParts.FromCharge(1000, 10, -5, 0, Min, Max, Charges);
            Assert.Equal(-1600, now.DensityPart, 6); // 1000 x 32 x -5%
            Assert.NotEqual(3200, now.DensityPart, 6); // not the +10 charge's part
        }

        [Fact]
        public void SmallIncomes_EstimateIncludesTheChargesRounding()
        {
            // Issue #83: payers with small untaxed incomes, where each charge's rounding is a large share of it.
            int[] untaxed = { 13, 7, 21, 9, 33, 4, 17, 26, 11, 5, 19, 8, 29, 14, 3, 23 };
            const int stored = 10, density = 5, delta = 3;
            var ledger = new TaxBookingLedger();
            double density_ = 0, district_ = 0;
            for (int f = 0; f < untaxed.Length; f++)
            {
                int u = untaxed[f];
                int without = EffectiveTaxRate.Compute(stored, 0, delta, Min, Max, u);
                int charged = EffectiveTaxRate.Compute(stored, density, delta, Min, Max, u);
                DensityTaxBooking.Parts(DensityTaxBooking.Tax(stored, u), DensityTaxBooking.Tax(without, u),
                    DensityTaxBooking.Tax(charged, u), out int district, out int part);
                ledger.AddSums(f, TaxZone.Residential, district, part > 0 ? part : 0, part < 0 ? part : 0, 0);
                PayerTaxParts p = PayerTaxParts.FromCharge(u, stored, density, delta, Min, Max, Charges);
                density_ += p.DensityPart;
                district_ += p.DistrictPart;
            }
            ledger.Monthly(TaxZone.Residential, out long bookedDistrict, out long bookedDensity, out _);
            Assert.Equal(bookedDensity, density_, 6);
            Assert.Equal(bookedDistrict, district_, 6);
            // Fail case: the unrounded base x rate is not what is booked.
            long sum = 0;
            foreach (int u in untaxed) sum += u;
            Assert.NotEqual(sum * Charges * density / 100.0, density_, 6);
        }
    }
}
