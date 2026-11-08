using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    /// <summary>
    /// The estimates follow a rate moved since a payer's last payment (C3, C4): <see cref="PaymentRate"/> and
    /// <see cref="PayerTaxParts"/>'s paid rate. A payer pays 1/32 month apart; between, its paid amount is the old rate's.
    /// </summary>
    public class PaymentRateTests
    {
        private const int Min = 0, Max = 30, Charges = 32;

        /// <summary>
        /// One refresh of the estimate systems' memo: the rate <paramref name="paid"/> was charged at, and the parts
        /// at the rates now. <paramref name="memo"/> is (paid seen, its rate), updated as the systems update it.
        /// </summary>
        private static PayerTaxParts Refresh(ref (bool known, long paid, int rate) memo, long paid, int vanilla, int density, int delta)
        {
            int now = EffectiveTaxRate.Compute(vanilla, density, delta, Min, Max, 1);
            int paidRate = PaymentRate.Of(memo.known, memo.paid, memo.rate, paid, now);
            memo = (true, paid, paidRate);
            return PayerTaxParts.Of(paid, 0, vanilla, density, delta, Min, Max, Charges, paidRate);
        }

        private static double Total(PayerTaxParts p) => p.IncomePart + p.DensityPart;

        [Fact]
        public void PaymentRate_KeepsTheRateWhileThePaymentIsTheSame()
        {
            Assert.Equal(10, PaymentRate.Of(true, 100, 10, 100, 15));
            Assert.NotEqual(15, PaymentRate.Of(true, 100, 10, 100, 15));
        }

        [Fact]
        public void PaymentRate_TakesTheRateNowForANewPaymentOrAFirstSighting()
        {
            Assert.Equal(15, PaymentRate.Of(true, 100, 10, 150, 15));
            Assert.Equal(15, PaymentRate.Of(false, 0, 0, 100, 15));
            Assert.NotEqual(10, PaymentRate.Of(true, 100, 10, 150, 15));
        }

        [Fact]
        public void Density_TheEstimateBeforeTheNextChargeEqualsTheOneAfterIt()
        {
            // Base 1000 at 10%: paid 100. The density offset goes to +5 (15%).
            var memo = (known: false, paid: 0L, rate: 0);
            Refresh(ref memo, 100, 10, 0, 0);
            PayerTaxParts before = Refresh(ref memo, 100, 10, 5, 0); // not charged since: still paid 100 at 10%
            PayerTaxParts after = Refresh(ref memo, 150, 10, 5, 0);  // charged at 15%: paid 150
            Assert.Equal(50, before.DensityPart, 6);
            Assert.Equal(after.DensityPart, before.DensityPart, 6);
            Assert.Equal(Total(after), Total(before), 6);
            Assert.Equal(150, Total(before), 6);
        }

        [Fact]
        public void Density_WithoutThePaidRate_TheEstimateIsPinnedToWhatWasPaid()
        {
            // The C3 bug: base = 100 / 15% = 667, so income + density stays 100 until the payer pays again.
            PayerTaxParts pinned = PayerTaxParts.Of(100, 0, 10, 5, 0, Min, Max, Charges);
            Assert.Equal(100, Total(pinned), 6);
            Assert.NotEqual(150, Total(pinned), 6);
        }

        [Fact]
        public void DistrictOverride_TheZoneEstimateFollowsTheOverrideAtOnce()
        {
            // C4: Commercial vanilla 10%, the district takes it to 15 (+5): the row's income part rises before any charge.
            var memo = (known: false, paid: 0L, rate: 0);
            PayerTaxParts was = Refresh(ref memo, 200, 10, 0, 0);
            PayerTaxParts before = Refresh(ref memo, 200, 10, 0, 5);
            PayerTaxParts after = Refresh(ref memo, 300, 10, 0, 5);
            Assert.Equal(200, was.IncomePart, 6);
            Assert.Equal(300, before.IncomePart, 6);
            Assert.Equal(after.IncomePart, before.IncomePart, 6);
            Assert.Equal(100, before.DistrictPart, 6);
            Assert.NotEqual(was.IncomePart, before.IncomePart, 6);
        }

        [Fact]
        public void APaymentAtZero_TakesTheBaseFromUntaxedIncome()
        {
            // Paid nothing at 0%; the density offset goes back from -10 to 0: the base is the untaxed income × 32.
            PayerTaxParts p = PayerTaxParts.Of(0, 50, 10, 0, 0, Min, Max, Charges, 0);
            Assert.Equal(1600, p.Base, 6);
            Assert.Equal(160, p.IncomePart, 6);
            Assert.Equal(0, PayerTaxParts.Of(0, 0, 10, 0, 0, Min, Max, Charges, 0).Base, 6);
        }
    }
}
