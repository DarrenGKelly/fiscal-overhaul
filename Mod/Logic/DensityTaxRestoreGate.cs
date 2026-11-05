namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// Whether <c>DensityTaxRestoreSystem</c> schedules its job this tax tick. The restore takes back what the apply
    /// system added this tick, so it is needed exactly when the apply system scheduled work: not otherwise (density
    /// taxes off, every offset 0, no payers this frame), so no job writes TaxPayer for nothing.
    /// </summary>
    public static class DensityTaxRestoreGate
    {
        /// <summary>
        /// Decided by <paramref name="appliedThisTick"/> alone. The current switch and offsets can't veto it: when the
        /// switch is turned off or the offsets go to 0 after an apply, the rates that apply raised must still come back
        /// down. They can't force it either: with nothing applied there's nothing to take back.
        /// </summary>
        public static bool RestoreNeeded(bool enabled, bool anyNonzeroOffset, bool appliedThisTick) => appliedThisTick;
    }
}
