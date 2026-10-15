namespace SizeboxTweaks
{
    // Read by name by BepInEx ConfigurationManager (F1) when passed as a setting tag:
    // Order sorts settings inside a section (higher first), IsAdvanced hides dev settings
    // until "Advanced settings" is ticked.
#pragma warning disable 0649
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? IsAdvanced;
    }
#pragma warning restore 0649
}
