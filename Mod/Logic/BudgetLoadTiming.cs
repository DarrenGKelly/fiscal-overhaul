using System.Globalization;

namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// Which <c>history</c> builds get a timing line in Mod.log, and the line. The first <see cref="BuildsPerOpen"/>
    /// builds after the UI subscribes (the Budget tab mounting: the Economy panel opening on it, or a switch to it) are
    /// logged, so an in-game check reads the load cost without a line every second while the panel stays open.
    /// <see cref="LogEveryBuild"/> logs them all.
    /// </summary>
    public sealed class BudgetLoadTiming
    {
        /// <summary>The subscribe-time build, the panel's first update and one periodic rebuild.</summary>
        public const int BuildsPerOpen = 3;

        /// <summary>Debug switch: a line for every build, not only the first ones after a subscribe.</summary>
        public bool LogEveryBuild { get; set; }

        private int m_Remaining;
        private int m_Build;

        /// <summary>The UI subscribed to the binding (the tab mounted): log the next builds.</summary>
        public void Subscribed()
        {
            m_Remaining = BuildsPerOpen;
            m_Build = 0;
        }

        /// <summary>A build is about to be written: true when it gets a line. Counts the builds since the subscribe.</summary>
        public bool TakeBuild()
        {
            m_Build++;
            if (LogEveryBuild) return true;
            if (m_Remaining <= 0) return false;
            m_Remaining--;
            return true;
        }

        /// <summary>The number of the build just taken since the last subscribe (1 = the subscribe-time build).</summary>
        public int Build => m_Build;

        /// <summary>
        /// "BudgetChart: build 1 compute 1.23 ms, write 0.45 ms; 3000 samples, 97 points, 29 series, 12 services, view city".
        /// </summary>
        public static string Line(int build, double computeMs, double writeMs, int samples, int points, int series, int services, string view)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "BudgetChart: build {0} compute {1:0.00} ms, write {2:0.00} ms; {3} samples, {4} points, {5} series, {6} services, view {7}",
                build, computeMs, writeMs, samples, points, series, services, string.IsNullOrEmpty(view) ? "city" : view);
        }

        /// <summary>The longest UI timing text logged; anything longer is cut.</summary>
        public const int MaxUiText = 300;

        /// <summary>The line for a timing the UI sends (logTiming): "BudgetChart UI: ...", one line, at most <see cref="MaxUiText"/> characters of text.</summary>
        public static string UiLine(string text)
        {
            text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ');
            if (text.Length > MaxUiText) text = text.Substring(0, MaxUiText);
            return "BudgetChart UI: " + text;
        }
    }
}
