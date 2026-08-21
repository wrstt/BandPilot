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

            // Light unless the user has explicitly asked for dark.
            //
            // This deliberately does NOT follow the Windows system theme. Doing
            // so meant anyone running Windows in dark mode met a dark BandPilot
            // on first launch without choosing it, and the dark theme is the
            // less-tested of the two. An opt-in is the honest default.
            return AppTheme.Light;
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
