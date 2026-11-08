using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class DemandViewMathTests
    {
        private const int Min = 0;
        private const int Max = 30;

        private static int[] Vanilla()
        {
            var v = new int[DemandViewMath.SlotCount];
            v[0] = 10; // total
            v[1] = 2;  // residential +2 => 12
            v[2] = 0;  // commercial 10
            v[3] = 1;  // industrial 11
            v[4] = -1; // office 9
            return v;
        }

        [Fact]
        public void TwoPayersAtZeroAndFour_EqualWeight_IsTwo()
        {
            Assert.Equal(2, DemandViewMath.MeanAdjustment(0 + 4, 2));
            Assert.NotEqual(4, DemandViewMath.MeanAdjustment(0 + 4, 2));
        }

        [Fact]
        public void NoPayers_IsZero_AndTheSlotStaysVanilla()
        {
            Assert.Equal(0, DemandViewMath.MeanAdjustment(10, 0));
            int[] vanilla = Vanilla();
            vanilla[12] = 3;
            var sums = new long[DemandViewMath.SlotCount];
            var counts = new int[DemandViewMath.SlotCount];
            sums[12] = 8; // a sum with no payers is ignored
            var view = new int[DemandViewMath.SlotCount];
            bool changed = DemandViewMath.Compose(vanilla, sums, counts, null, Min, Max, Min, Max, view);
            Assert.False(changed);
            Assert.Equal(vanilla, view);
        }

        [Fact]
        public void HalvesRoundToEven_LikeMathRound()
        {
            Assert.Equal(0, DemandViewMath.MeanAdjustment(1, 2));   // 0.5 -> 0
            Assert.Equal(2, DemandViewMath.MeanAdjustment(3, 2));   // 1.5 -> 2
            Assert.Equal(2, DemandViewMath.MeanAdjustment(5, 2));   // 2.5 -> 2
            Assert.Equal(0, DemandViewMath.MeanAdjustment(-1, 2));  // -0.5 -> 0
            Assert.Equal(-2, DemandViewMath.MeanAdjustment(-3, 2)); // -1.5 -> -2
            Assert.Equal(1, DemandViewMath.MeanAdjustment(2, 3));   // 0.67 -> 1
            Assert.NotEqual(3, DemandViewMath.MeanAdjustment(5, 2));
        }

        [Fact]
        public void Compose_MovesALeafSlotByItsMean_AndLeavesTheCitySlots()
        {
            int[] vanilla = Vanilla();
            var sums = new long[DemandViewMath.SlotCount];
            var counts = new int[DemandViewMath.SlotCount];
            sums[DemandViewMath.CommercialFirst + 3] = 4; counts[DemandViewMath.CommercialFirst + 3] = 2; // +0 and +4
            var view = new int[DemandViewMath.SlotCount];
            Assert.True(DemandViewMath.Compose(vanilla, sums, counts, null, Min, Max, Min, Max, view));
            Assert.Equal(vanilla[DemandViewMath.CommercialFirst + 3] + 2, view[DemandViewMath.CommercialFirst + 3]);
            for (int s = 0; s <= 4; s++) Assert.Equal(vanilla[s], view[s]);
            Assert.Equal(vanilla[DemandViewMath.CommercialFirst + 4], view[DemandViewMath.CommercialFirst + 4]);
        }

        [Fact]
        public void Compose_AllZeroAdjustments_IsVanilla()
        {
            int[] vanilla = Vanilla();
            var sums = new long[DemandViewMath.SlotCount];
            var counts = new int[DemandViewMath.SlotCount];
            for (int s = 5; s < DemandViewMath.SlotCount; s++) counts[s] = 100;
            var view = new int[DemandViewMath.SlotCount];
            Assert.False(DemandViewMath.Compose(vanilla, sums, counts, null, Min, Max, Min, Max, view));
            Assert.Equal(vanilla, view);
        }

        [Fact]
        public void Compose_ClampsTheFullRateWithinTheLimits()
        {
            int[] vanilla = Vanilla();
            int slot = DemandViewMath.ResidentialFirst + 1;
            vanilla[slot] = 16; // full 10 + 2 + 16 = 28
            var sums = new long[DemandViewMath.SlotCount];
            var counts = new int[DemandViewMath.SlotCount];
            sums[slot] = 5; counts[slot] = 1; // +5 would be 33
            var view = new int[DemandViewMath.SlotCount];
            DemandViewMath.Compose(vanilla, sums, counts, null, Min, Max, Min, 50, view);
            Assert.Equal(30 - 12, view[slot]); // full 30: the job-level max, not the resource one
            Assert.NotEqual(16 + 5, view[slot]);

            // Down: never below 0.
            Assert.Equal(-12, DemandViewMath.ViewOffset(-9, 3, -5, -10, Max)); // full 3 - 5 -> 0
            // Never past vanilla: a full rate already above the max stays put, it doesn't jump to the max.
            Assert.Equal(20, DemandViewMath.ViewOffset(20, 32, 5, Min, Max));
        }

        [Fact]
        public void Compose_UsesTheOfficeOffsetForWeightlessIndustrialSlots()
        {
            int[] vanilla = Vanilla();
            int slot = DemandViewMath.IndustrialFirst + 7;
            vanilla[slot] = 19; // industrial full 10+1+19 = 30, office full 10-1+19 = 28
            var sums = new long[DemandViewMath.SlotCount];
            var counts = new int[DemandViewMath.SlotCount];
            sums[slot] = 5; counts[slot] = 1;
            var weightless = new bool[DemandViewMath.SlotCount];
            var view = new int[DemandViewMath.SlotCount];

            DemandViewMath.Compose(vanilla, sums, counts, weightless, Min, Max, Min, Max, view);
            Assert.Equal(19, view[slot]); // industrial: already at the max

            weightless[slot] = true;
            DemandViewMath.Compose(vanilla, sums, counts, weightless, Min, Max, Min, Max, view);
            Assert.Equal(21, view[slot]); // office: 28 -> 30
        }

        [Fact]
        public void PayerAdjustment_IsTheClampedChange()
        {
            Assert.Equal(4, DemandViewMath.PayerAdjustment(10, 3, 1, Min, Max));
            Assert.Equal(2, DemandViewMath.PayerAdjustment(28, 5, 0, Min, Max));
            Assert.Equal(0, DemandViewMath.PayerAdjustment(10, 0, 0, Min, Max));
            Assert.NotEqual(5, DemandViewMath.PayerAdjustment(28, 5, 0, Min, Max));
        }

        [Fact]
        public void AreaOf_FollowsVanillasLayout()
        {
            Assert.Equal(0, DemandViewMath.AreaOf(0, false));
            Assert.Equal(0, DemandViewMath.AreaOf(4, false));
            Assert.Equal(DemandViewMath.AreaResidential, DemandViewMath.AreaOf(5, false));
            Assert.Equal(DemandViewMath.AreaResidential, DemandViewMath.AreaOf(9, false));
            Assert.Equal(DemandViewMath.AreaCommercial, DemandViewMath.AreaOf(10, true));
            Assert.Equal(DemandViewMath.AreaCommercial, DemandViewMath.AreaOf(50, false));
            Assert.Equal(DemandViewMath.AreaIndustrial, DemandViewMath.AreaOf(51, false));
            Assert.Equal(DemandViewMath.AreaOffice, DemandViewMath.AreaOf(91, true));
            Assert.Equal(0, DemandViewMath.AreaOf(92, false));
        }
    }
}
