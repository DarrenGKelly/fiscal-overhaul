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

        /// <summary>Starts <paramref name="frame"/>'s tick: drops what that frame's previous tick booked.</summary>
        public void BeginFrame(int frame)
        {
            int f = Wrap(frame);
            for (int z = 0; z < Zones; z++)
            {
                m_District[f * Zones + z] = 0;
                m_DensityPositive[f * Zones + z] = 0;
                m_DensityNegative[f * Zones + z] = 0;
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

        /// <summary>True when nothing is booked beyond vanilla: the booking leaves vanilla's numbers alone.</summary>
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
        }

        private static int Wrap(int frame) => ((frame % Frames) + Frames) % Frames;
    }
}
