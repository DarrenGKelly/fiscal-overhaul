using FiscalOverhaul.Logic;
using Xunit;

namespace FiscalOverhaul.Tests
{
    public class TaxPropertyScoreMathTests
    {
        // The game's CitizenHappinessPrefab defaults (1.6.0f1): uneducated .. highly educated.
        private const float U = -0.25f, P = -0.5f, E = -1f, W = -1.5f, H = -2f;
        // Vanilla's job-level limits for the tests (TaxParameterData.m_JobLevelTaxLimits).
        private const int Min = -10, Max = 30;

        /// <summary>
        /// A household's score for a home whose vanilla score is <paramref name="vanillaScore"/>, as TaxFindPropertySystem
        /// computes it: each adult's bonus at the city rate and at the home's rate (vanilla + density + district, clamped).
        /// </summary>
        private static float Score(float vanillaScore, int cityRate, int density, int district, params int[] adultLevels)
        {
            var math = new TaxPropertyScoreMath();
            foreach (int level in adultLevels)
            {
                int vanillaBonus = TaxHappinessMath.Bonus(cityRate, level, U, P, E, W, H, 0f, 0f);
                int homeRate = TaxHappinessMath.ResidentRate(cityRate, density, district, Min, Max);
                int homeBonus = homeRate == cityRate ? vanillaBonus : TaxHappinessMath.Bonus(homeRate, level, U, P, E, W, H, 0f, 0f);
                math.AddAdult(vanillaBonus, homeBonus);
            }
            return math.Adjust(vanillaScore);
        }

        [Fact]
        public void OtherwiseEqualBuildings_LowerEffectiveTax_ScoresHigher()
        {
            float untouched = Score(100f, 10, 0, 0, 2, 3);
            float taxedUp = Score(100f, 10, 5, 0, 2, 3);
            float districtCut = Score(100f, 10, 0, -4, 2, 3);
            Assert.True(taxedUp < untouched);
            Assert.True(districtCut > untouched);
            Assert.True(taxedUp < districtCut);
        }

        [Fact]
        public void HigherAdjustment_DoesNotScoreHigher()
        {
            // Fail case of the above: a +5 home never beats a +1 home for the same household.
            Assert.False(Score(100f, 10, 5, 0, 4, 4) > Score(100f, 10, 1, 0, 4, 4));
        }

        [Theory]
        [InlineData(10, 0)]
        [InlineData(10, 2)]
        [InlineData(17, 4)]
        public void EqualRates_AreVanillasScoreExactly(int cityRate, int level)
        {
            float vanilla = 87.3125f;
            Assert.Equal(vanilla, Score(vanilla, cityRate, 0, 0, level, level));
            // An adjustment that cancels out (density +3, district -3) is no drift either.
            Assert.Equal(vanilla, Score(vanilla, cityRate, 3, -3, level));
        }

        [Fact]
        public void AnAdjustment_MovesTheScore()
        {
            // Fail case of the no-drift test: a real adjustment is not vanilla's score.
            Assert.NotEqual(87.3125f, Score(87.3125f, 10, 5, 0, 4));
        }

        [Fact]
        public void TheHomeRate_IsClampedToTheJobLevelLimits()
        {
            // City rate 28, max 30: +2 and +5 both charge 30, so they score the same.
            Assert.Equal(Score(50f, 28, 2, 0, 4), Score(50f, 28, 5, 0, 4));
            // Below the cap they differ.
            Assert.NotEqual(Score(50f, 20, 2, 0, 4), Score(50f, 20, 5, 0, 4));
        }

        [Fact]
        public void Term_IsVanillasTruncatingAverage()
        {
            Assert.Equal(-3, TaxPropertyScoreMath.Term(-7, 2));
            Assert.NotEqual(-4, TaxPropertyScoreMath.Term(-7, 2));
            Assert.Equal(0, TaxPropertyScoreMath.Term(0, 0));
        }

        [Fact]
        public void ScoreDelta_IsTwiceTheAverageDifference()
        {
            var math = new TaxPropertyScoreMath();
            math.AddAdult(0, -5);
            math.AddAdult(-2, -6);
            // home term (-11 / 2 = -5) - vanilla term (-2 / 2 = -1) = -4, weighted 2.
            Assert.Equal(-8, math.ScoreDelta());
            Assert.NotEqual(-9, math.ScoreDelta());
        }

        [Fact]
        public void NoAdults_IsNoChange()
        {
            var math = new TaxPropertyScoreMath();
            Assert.Equal(0, math.ScoreDelta());
            Assert.Equal(12f, math.Adjust(12f));
        }

        [Fact]
        public void NoProperty_StaysNegativeInfinity()
        {
            var math = new TaxPropertyScoreMath();
            math.AddAdult(0, -10);
            Assert.Equal(float.NegativeInfinity, math.Adjust(float.NegativeInfinity));
            Assert.Equal(10f - 20f, math.Adjust(10f));
        }
    }
}
