namespace Albion.Client
{
    /// <summary>Version stamped into the game UI so a tester can see which build is running.</summary>
    public static class BuildInfo
    {
        /// <summary>Release version, kept in sync with PlayerSettings.bundleVersion.</summary>
        public const string Version = "0.8.6";

        /// <summary>Short label in the bottom-right corner.</summary>
        public static string Label => "Альбион Онлайн v" + Version;
    }
}
