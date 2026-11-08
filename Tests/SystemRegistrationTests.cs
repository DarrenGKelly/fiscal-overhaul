using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FiscalOverhaul.Tests
{
    /// <summary>
    /// Every UI system in Mod/Systems must be registered in Mod.cs (UpdateAt/UpdateBefore/UpdateAfter), or its bindings
    /// have no handler in game ("Tried to trigger ... has no handler" in UI.log). A merge once dropped TaxRatesUISystem's
    /// line. Mod.cs needs the game's DLLs, so this reads the source text.
    /// </summary>
    public class SystemRegistrationTests
    {
        private static readonly Regex UISystemClass = new Regex(@"class\s+(\w+)\s*:\s*UISystemBase\b");

        /// <summary>The UI system classes declared in <paramref name="systemSources"/> that <paramref name="modSource"/> never registers.</summary>
        private static List<string> Unregistered(IEnumerable<string> systemSources, string modSource) =>
            systemSources
                .SelectMany(src => UISystemClass.Matches(src).Cast<Match>().Select(m => m.Groups[1].Value))
                .Where(name => !Regex.IsMatch(modSource, @"Update(At|Before|After)<" + name + @"\b"))
                .ToList();

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mod", "Mod.cs"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        [Fact]
        public void EveryUISystem_IsRegisteredInModCs()
        {
            string root = RepoRoot();
            var sources = Directory.GetFiles(Path.Combine(root, "Mod", "Systems"), "*.cs").Select(File.ReadAllText).ToList();
            string mod = File.ReadAllText(Path.Combine(root, "Mod", "Mod.cs"));

            Assert.Contains(sources, s => s.Contains("class TaxRatesUISystem"));
            Assert.Empty(Unregistered(sources, mod));
        }

        [Fact]
        public void Unregistered_NamesASystemModCsLeavesOut()
        {
            var sources = new[] { "public partial class AUISystem : UISystemBase {}", "public partial class BUISystem : UISystemBase {}" };
            string mod = "updateSystem.UpdateAt<AUISystem>(SystemUpdatePhase.UIUpdate);";

            Assert.Equal(new[] { "BUISystem" }, Unregistered(sources, mod));
            Assert.Empty(Unregistered(sources, mod + " updateSystem.UpdateAfter<BUISystem, X>(p);"));
        }
    }
}
