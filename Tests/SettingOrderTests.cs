using System;
using System.IO;
using Xunit;

namespace FiscalOverhaul.Tests
{
    /// <summary>
    /// Setting.cs targets net48 and needs the game's DLLs, so it can't be compiled here. The game lists the
    /// Features group in property declaration order, so this reads the source text and checks the order.
    /// </summary>
    public class SettingOrderTests
    {
        private static string SettingSource()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mod", "Setting.cs")))
            {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, "Mod", "Setting.cs"));
        }

        [Fact]
        public void DistrictBudget_SitsRightAfterBudgetChart_AndBeforeDensityTaxes()
        {
            var src = SettingSource();
            int budgetChart = src.IndexOf("public bool EnableBudgetChart", StringComparison.Ordinal);
            int districtBudget = src.IndexOf("public bool EnableDistrictBudget", StringComparison.Ordinal);
            int densityTaxes = src.IndexOf("public bool EnableDensityTaxes", StringComparison.Ordinal);

            Assert.True(budgetChart >= 0 && districtBudget >= 0 && densityTaxes >= 0);
            Assert.True(budgetChart < districtBudget, "District budget must come after Budget chart");
            Assert.True(districtBudget < densityTaxes, "District budget must come before Density taxes");

            // Nothing else is declared between Budget chart and District budget.
            string between = src.Substring(budgetChart + 1, districtBudget - budgetChart - 1);
            Assert.DoesNotContain("public bool ", between);
        }
    }
}
