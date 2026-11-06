using System.Globalization;

namespace FiscalOverhaul.Logic
{
    /// <summary>The string the UI uses to pick a district: "" = Entire City, "outside", or "index:version" of the entity.</summary>
    public static class DistrictKey
    {
        public const string City = "";
        public const string Outside = "outside";

        public static string Format(int index, int version) => index.ToString(CultureInfo.InvariantCulture) + ":" + version.ToString(CultureInfo.InvariantCulture);

        public static bool TryParse(string key, out int index, out int version)
        {
            index = 0;
            version = 0;
            if (string.IsNullOrEmpty(key)) return false;
            int colon = key.IndexOf(':');
            if (colon <= 0 || colon == key.Length - 1) return false;
            return int.TryParse(key.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out index)
                && int.TryParse(key.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out version);
        }
    }
}
