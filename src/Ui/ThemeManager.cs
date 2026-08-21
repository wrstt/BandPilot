using System;
using System.Windows;
using Microsoft.Win32;

namespace BandPilot.Ui
{
    public enum AppTheme { Light, Dark }

    /// <summary>
    /// Swaps the colour palette at runtime.
    ///
    /// The palette is merged dictionary zero and nothing else, so switching is a
    /// single replacement. Every theme-dependent brush is referenced with
    /// DynamicResource, which is what lets the open window repaint rather than
    /// requiring a restart.
    /// </summary>
    public static class ThemeManager
    {
        private const string SettingsKey = @"Software\BandPilot";
        private const string ValueName = "Theme";

        public static AppTheme Current { get; private set; }

        /// <summary>Raised after a swap so pages can re-pull cached brushes.</summary>
        public static event EventHandler Changed;

        public static void Initialise()
        {
            Apply(Load(), false);
        }

        public static void Toggle()
        {
            Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark, true);
        }

        public static void Apply(AppTheme theme, bool persist)
        {
            var dict = new ResourceDictionary
            {
                Source = new Uri(
                    theme == AppTheme.Dark ? "Ui/Palette.Dark.xaml" : "Ui/Palette.Light.xaml",
                    UriKind.Relative)
            };

            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count == 0) merged.Add(dict);
            else merged[0] = dict;

            Current = theme;
            if (persist) Save(theme);

            EventHandler handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        private static AppTheme Load()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (k != null)
                    {
                        string v = k.GetValue(ValueName) as string;
                        if (string.Equals(v, "Dark", StringComparison.OrdinalIgnoreCase))
                            return AppTheme.Dark;
                        if (string.Equals(v, "Light", StringComparison.OrdinalIgnoreCase))
                            return AppTheme.Light;
                    }
                }
            }
            catch (Exception) { /* fall through to the system preference */ }

            return FollowsSystemDark() ? AppTheme.Dark : AppTheme.Light;
        }

        /// <summary>
        /// With no saved choice, match what the user already told Windows they
        /// want rather than assuming light.
        /// </summary>
        private static bool FollowsSystemDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch (Exception) { return false; }
        }

        private static void Save(AppTheme theme)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (k != null) k.SetValue(ValueName, theme.ToString(), RegistryValueKind.String);
                }
            }
            catch (Exception) { /* a preference that will not persist is not worth failing over */ }
        }
    }
}
