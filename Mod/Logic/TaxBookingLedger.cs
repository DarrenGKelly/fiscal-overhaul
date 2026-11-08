using System;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The tax engine's charges beyond vanilla over the last month's worth of charges, per zone: vanilla charges each
    /// payer on one of <see cref="Frames"/> update frames, once per 1/32 month, so the last <see cref="Frames"/> ticks
    /// hold one charge per payer; each is kept at the monthly scale (<see cref="DensityTaxBooking.Monthly"/>), so the
    /// sum over the frames is the monthly amount, on vanilla's scale (its taxable-income statistic is one charge per
    /// payer × 32, and its estimate the rate × that). A tick replaces its frame's slot, so the window rolls. Main thread.
    /// </summary>
    public sealed class TaxBookingLedger
    {
        /// <summary>TaxSystem's update frames (its payers are split into 16 groups).</summary>
        public const int Frames = 16;
        public const int Zones = 4;

        private readonly long[] m_District = new long[Frames * Zones];
        private readonly long[] m_DensityPositive = new long[Frames * Zones];
        private readonly long[] m_DensityNegative = new long[Frames * Zones];
        private readonly long[] m_Vanilla = new long[Frames * Zones];
        // Per density cell (TaxRateModel.DensityCells): the charged payers' taxable income and their density parts.
        private readonly long[] m_CellBase = new long[Frames * Cells];
        private readonly long[] m_CellDensity = new long[Frames * Cells];
        // The rates each frame was charged at (RateSignature); false: never charged since the last clear.
        private readonly long[] m_Signature = new long[Frames];
        private readonly bool[] m_Charged = new bool[Frames];

        /// <summary>Density cells (<see cref="DensityTaxTable.Index"/>).</summary>
        public const int Cells = DensityTaxTable.ZoneCount * DensityTaxTable.DensityCount;

        /// <summary>Starts <paramref name="frame"/>'s tick: drops what that frame's previous tick booked.</summary>
        public void BeginFrame(int frame)
        {
            int f = Wrap(frame);
            for (int z = 0; z < Zones; z++)
            {
                m_District[f * Zones + z] = 0;
                m_DensityPositive[f * Zones + z] = 0;
                m_DensityNegative[f * Zones + z] = 0;
                m_Vanilla[f * Zones + z] = 0;
            }
            for (int c = 0; c < Cells; c++)
            {
                m_CellBase[f * Cells + c] = 0;
                m_CellDensity[f * Cells + c] = 0;
            }
            m_Charged[f] = false;
        }

        /// <summary>Starts <paramref name="frame"/>'s tick charged at the rates <paramref name="signature"/> fingerprints.</summary>
        public void BeginFrame(int frame, long signature)
        {
            BeginFrame(frame);
            m_Signature[Wrap(frame)] = signature;
            m_Charged[Wrap(frame)] = true;
        }

        /// <summary>
        /// True when every frame in the window was charged at the rates <paramref name="signature"/> fingerprints: the
        /// monthly amounts are then all at the current rates (not a mix of a moved rate's old and new charges).
        /// </summary>
        public bool Settled(long signature)
        {
            for (int f = 0; f < Frames; f++)
                if (!m_Charged[f] || m_Signature[f] != signature) return false;
            return true;
        }

        /// <summary>
        /// Adds a group of charges in density cell <paramref name="cell"/> (per charge): their taxable income (positive
        /// untaxed income) and density parts. For the log's comparison with the Taxation tab's cell estimate; never booked.
        /// </summary>
        public void AddCell(int frame, int cell, long taxableIncome, long density)
        {
            if (cell < 0 || cell >= Cells) return;
            int i = Wrap(frame) * Cells + cell;
            m_CellBase[i] += taxableIncome * DensityTaxBooking.ChargesPerMonth;
            m_CellDensity[i] += density * DensityTaxBooking.ChargesPerMonth;
        }

        /// <summary>A density cell's monthly taxable income and density part (both on the ledger's scale).</summary>
        public void CellMonthly(int cell, out long taxableIncome, out long density)
        {
            taxableIncome = density = 0;
            if (cell < 0 || cell >= Cells) return;
            for (int f = 0; f < Frames; f++)
            {
                taxableIncome += m_CellBase[f * Cells + cell];
                density += m_CellDensity[f * Cells + cell];
            }
        }

        /// <summary>Adds one charge's parts (<see cref="DensityTaxBooking.Parts"/>, per charge) to <paramref name="frame"/>.</summary>
        public void Add(int frame, TaxZone zone, int district, int density)
        {
            int i = Wrap(frame) * Zones + (int)zone;
            m_District[i] += DensityTaxBooking.Monthly(district);
            if (density > 0) m_DensityPositive[i] += DensityTaxBooking.Monthly(density);
            else m_DensityNegative[i] += DensityTaxBooking.Monthly(density);
        }

        /// <summary>
        /// Adds a group of charges' sums (per charge, as the engine's jobs queue them per chunk): the district parts, the
        /// positive and the negative density parts, and their tax at the vanilla rate (logged against vanilla's estimate,
        /// never booked).
        /// </summary>
        public void AddSums(int frame, TaxZone zone, long district, long densityPositive, long densityNegative, long vanillaTax)
        {
            int i = Wrap(frame) * Zones + (int)zone;
            m_District[i] += district * DensityTaxBooking.ChargesPerMonth;
            m_DensityPositive[i] += densityPositive * DensityTaxBooking.ChargesPerMonth;
            m_DensityNegative[i] += densityNegative * DensityTaxBooking.ChargesPerMonth;
            m_Vanilla[i] += vanillaTax * DensityTaxBooking.ChargesPerMonth;
        }

        /// <summary>
        /// The zone's monthly tax at the payers' vanilla rates, on the ledger's scale: the engine's own measure of what
        /// vanilla's estimate pays (TaxResidential − SubsidyResidential, …), to check the scale against it.
        /// </summary>
        public long VanillaMonthly(TaxZone zone)
        {
            long sum = 0;
            for (int f = 0; f < Frames; f++) sum += m_Vanilla[f * Zones + (int)zone];
            return sum;
        }

        /// <summary>The zone's monthly parts: the district part and the density parts of each sign (negative ≤ 0).</summary>
        public void Monthly(TaxZone zone, out long district, out long densityPositive, out long densityNegative)
        {
            district = densityPositive = densityNegative = 0;
            for (int f = 0; f < Frames; f++)
            {
                int i = f * Zones + (int)zone;
                district += m_District[i];
                densityPositive += m_DensityPositive[i];
                densityNegative += m_DensityNegative[i];
            }
        }

        /// <summary>True when nothing is booked beyond vanilla (the vanilla sums don't count): the booking leaves vanilla's numbers alone.</summary>
        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < m_District.Length; i++)
                    if (m_District[i] != 0 || m_DensityPositive[i] != 0 || m_DensityNegative[i] != 0) return false;
                return true;
            }
        }

        /// <summary>Forgets every charge (a load, or the engine handing back to vanilla).</summary>
        public void Clear()
        {
            Array.Clear(m_District, 0, m_District.Length);
            Array.Clear(m_DensityPositive, 0, m_DensityPositive.Length);
            Array.Clear(m_DensityNegative, 0, m_DensityNegative.Length);
            Array.Clear(m_Vanilla, 0, m_Vanilla.Length);
            Array.Clear(m_CellBase, 0, m_CellBase.Length);
            Array.Clear(m_CellDensity, 0, m_CellDensity.Length);
            Array.Clear(m_Charged, 0, m_Charged.Length);
        }

        private static int Wrap(int frame) => ((frame % Frames) + Frames) % Frames;
    }
}
