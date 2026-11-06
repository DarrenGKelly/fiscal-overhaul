namespace FiscalOverhaul.Logic
{
    /// <summary>
    /// The shape of the <c>history</c> binding's "FiscalOverhaul.BudgetHistory" value. Gameface's typed JSON writer
    /// fixes a type's property set the first time a type name is written: writing the same name later with other
    /// properties crashes the game natively in TypeEnd. So every view writes every property, in this order, and a
    /// property a view has no value for is written as null (as vanilla's writers do).
    /// </summary>
    public static class BudgetHistoryPayload
    {
        public const string TypeName = "FiscalOverhaul.BudgetHistory";

        private static readonly string[] s_Properties = { "span", "samples", "monthsAgo", "income", "expense", "district", "nowIncome", "nowExpense" };

        /// <summary>The properties to write, in order. The same for Entire City and a district view.</summary>
        public static string[] Properties(bool districtView) => s_Properties;

        /// <summary>True when the property is written as null in this view: the live district sums for Entire City.</summary>
        public static bool IsNull(string property, bool districtView) =>
            !districtView && (property == "nowIncome" || property == "nowExpense");
    }
}
