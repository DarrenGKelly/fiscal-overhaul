using System;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The tax happiness factor for one citizen, at a given residential rate: a port of vanilla
    /// <c>CitizenHappinessSystem.GetTaxBonuses</c> (CitizenHappinessSystem.cs:1370-1396, game version 1.6.0f1) with the
    /// rate and the city's TaxHappiness modifier passed in, so TaxHappinessSystem can apply
    /// it to the rate the citizen's home is actually charged (<see cref="ResidentRate"/>). Pure float/int maths,
    /// Burst-safe: the happiness job calls it per citizen.
    /// </summary>
    public static class TaxHappinessMath
    {
        /// <summary>The rate at which tax neither pleases nor upsets (vanilla's 10).</summary>
        public const int NeutralRate = 10;

        /// <summary>
        /// The CitizenHappinessParameterData multiplier for <paramref name="educationLevel"/> (0 uneducated .. 4 highly
        /// educated); any other level is 0 (vanilla's switch default).
        /// </summary>
        public static float Multiplier(int educationLevel, float uneducated, float poorlyEducated, float educated, float wellEducated, float highlyEducated)
        {
            switch (educationLevel)
            {
                case 0: return uneducated;
                case 1: return poorlyEducated;
                case 2: return educated;
                case 3: return wellEducated;
                case 4: return highlyEducated;
                default: return 0f;
            }
        }

        /// <summary>
        /// The wellbeing bonus (vanilla returns it as int2(0, bonus)): (10 - rate), then the city modifier
        /// (CityUtils.ApplyModifier: + <paramref name="modifierFlat"/>, then + itself × <paramref name="modifierRelative"/>),
        /// times -multiplier, rounded as Mathf.RoundToInt (to even).
        /// </summary>
        public static int Bonus(int residentialRate, float multiplier, float modifierFlat, float modifierRelative)
        {
            float value = NeutralRate - residentialRate;
            value += modifierFlat;
            value += value * modifierRelative;
            value *= 0f - multiplier;
            return (int)Math.Round((double)value);
        }

        /// <summary><see cref="Bonus(int, float, float, float)"/> with the multiplier picked by education level.</summary>
        public static int Bonus(int residentialRate, int educationLevel, float uneducated, float poorlyEducated, float educated, float wellEducated, float highlyEducated, float modifierFlat, float modifierRelative)
        {
            return Bonus(residentialRate, Multiplier(educationLevel, uneducated, poorlyEducated, educated, wellEducated, highlyEducated), modifierFlat, modifierRelative);
        }

        /// <summary>
        /// The residential rate a citizen's home is charged: the vanilla rate for their education level plus the home's
        /// density and district adjustments, inside the vanilla job-level limits (<see cref="EffectiveTaxRate.Compute"/>,
        /// as the tax engine charges a household with income). All-zero adjustments give the vanilla rate.
        /// </summary>
        public static int ResidentRate(int vanillaRate, int densityAdjustment, int districtAdjustment, int min, int max)
        {
            return EffectiveTaxRate.Compute(vanillaRate, densityAdjustment, districtAdjustment, min, max, 1);
        }
    }
}
