namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The tax term of a household's score for a home, at the rate that home is charged: vanilla
    /// <c>PropertyUtils.GetPropertyScore</c> (PropertyUtils.cs:490-561, game version 1.6.3f1) adds <c>2 × the average tax
    /// happiness bonus of the household's adults</c> (:511, :524, :552-555, :560), each bonus at the CITY rate for the
    /// adult's education level. TaxFindPropertySystem calls vanilla's score and corrects it by the difference between
    /// that term at the home's rate (the vanilla rate plus the home's density and district adjustments,
    /// <see cref="TaxHappinessMath.ResidentRate"/>) and at the city rate. Pure int/float maths, Burst-safe.
    ///
    /// Copied from PropertyUtils.GetPropertyScore (:511, :521-525, :552-555, :560), game version 1.6.3f1: the adults'
    /// bonus sum, its integer average (C# truncating division, only when there is an adult) and the weight 2.
    /// </summary>
    public struct TaxPropertyScoreMath
    {
        /// <summary>The weight vanilla gives the averaged tax bonus in the score (PropertyUtils.cs:560).</summary>
        public const int TaxWeight = 2;

        /// <summary>The sum of the adults' bonuses at the city rate (vanilla's num6 before averaging).</summary>
        public int VanillaSum;

        /// <summary>The sum of the adults' bonuses at the home's rate.</summary>
        public int HomeSum;

        /// <summary>The adults counted (vanilla's num3).</summary>
        public int Adults;

        /// <summary>One adult: their tax bonus at the city rate and at the home's rate (equal when no adjustment applies).</summary>
        public void AddAdult(int vanillaBonus, int homeBonus)
        {
            VanillaSum += vanillaBonus;
            HomeSum += homeBonus;
            Adults++;
        }

        /// <summary>Vanilla's averaged term (PropertyUtils.cs:552-555): the sum over the adults, integer-divided, or the sum (0) without one.</summary>
        public static int Term(int sum, int adults)
        {
            return adults > 0 ? sum / adults : sum;
        }

        /// <summary>The score difference the home's rate makes: 2 × (home term − vanilla term). 0 when every bonus is vanilla's.</summary>
        public int ScoreDelta()
        {
            return TaxWeight * (Term(HomeSum, Adults) - Term(VanillaSum, Adults));
        }

        /// <summary>
        /// <paramref name="vanillaScore"/> (GetPropertyScore's result) with its tax term at the home's rate. Returns
        /// <paramref name="vanillaScore"/> unchanged (bit for bit) when the delta is 0, and leaves -∞ (no property) as is.
        /// </summary>
        public float Adjust(float vanillaScore)
        {
            int delta = ScoreDelta();
            if (delta == 0 || float.IsNegativeInfinity(vanillaScore)) return vanillaScore;
            return vanillaScore + delta;
        }
    }
}
