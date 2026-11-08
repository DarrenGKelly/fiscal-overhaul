namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// Which zone's tax line a payer's tax lands on, as vanilla TaxSystem books it (PayTaxJob.PayTax): households on
    /// Residential, service companies on Commercial, processing companies on Industrial, or Office when their output
    /// weighs nothing. The tax engine keys district adjustments by it. Burst-safe.
    /// </summary>
    public static class TaxPayerZone
    {
        public static TaxZone Of(bool isHousehold, bool isCommercial, bool weightlessOutput)
        {
            if (isHousehold) return TaxZone.Residential;
            if (isCommercial) return TaxZone.Commercial;
            return weightlessOutput ? TaxZone.Office : TaxZone.Industrial;
        }
    }
}
