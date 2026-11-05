namespace FiscalOverhaul.Logic
{
    /// <summary>When a feature's systems do their work, given its Options switch and the game's state.</summary>
    public static class FeatureGates
    {
        /// <summary>
        /// Whether a UI system builds its Economy panel data this frame: only while its feature is enabled and the
        /// panel is open. A disabled feature's tab is vanilla, so there's nothing to build for it.
        /// </summary>
        public static bool BuildsPanelData(bool featureEnabled, bool economyPanelOpen) => featureEnabled && economyPanelOpen;
    }
}
