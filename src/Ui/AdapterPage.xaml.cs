using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BandPilot.Adapter;
using BandPilot.Wifi;

namespace BandPilot.Ui
{
    public partial class AdapterPage : UserControl
    {
        private readonly MainWindow _shell;
        private WifiAdapter _adapter;
        private string _instanceKey;
        private string _netAdapterName;
        private List<AdvancedProperty> _all = new List<AdvancedProperty>();

        /// <summary>
        /// Set when the band filter matched nothing and the table fell back to
        /// showing everything. Kept as a field rather than by ticking the
        /// checkbox, because writing to the checkbox left the user unable to
        /// untick it on drivers whose keywords the filter does not recognise.
        /// </summary>
        private bool _filterFellBack;

        public AdapterPage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();
        }

        public void OnShown(WifiAdapter adapter)
        {
            if (adapter == null)
            {
                // Reachable if the user opens this page before the first scan
                // has picked an adapter, so it explains itself rather than
                // sitting blank.
                SubHeader.Text = "Waiting for the Bands & APs page to select an adapter.";
                Status("Open Bands & APs first, then come back.", "B.TextDim");
                return;
            }

            bool changed = _adapter == null || _adapter.Guid != adapter.Guid;
            _adapter = adapter;
            SubHeader.Text = adapter.Description + " — settings read live from the driver.";

            // The resolved Windows adapter name belongs to the previous radio.
            // Keeping it would apply the next setting change to the wrong card.
            if (changed) _netAdapterName = null;

            if (changed || _all.Count == 0) Load();
        }

        private async void Load()
        {
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (_adapter == null) return;

            Status("Reading driver settings…", "B.TextDim");
            BtnReload.IsEnabled = false;

            Guid guid = _adapter.Guid;
            string desc = _adapter.Description;

            try
            {
                var loaded = await Task.Run(() =>
                {
                    string key = AdapterRegistry.FindInstanceKey(guid, desc);
                    List<AdvancedProperty> props = key != null
                        ? AdapterRegistry.Read(key)
                        : new List<AdvancedProperty>();
                    return new Tuple<string, List<AdvancedProperty>>(key, props);
                });

                _instanceKey = loaded.Item1;
                _all = loaded.Item2 ?? new List<AdvancedProperty>();

                if (_instanceKey == null)
                {
                    Status("Could not find this radio's driver key in the registry. "
                         + "The adapter may have been removed or disabled.", "B.WarnText");
                }
                else if (_all.Count == 0)
                {
                    // Normal on several Realtek and MediaTek drivers, which
                    // publish few or no adjustable keywords. Not a failure.
                    Status("This driver exposes no adjustable settings. That is normal for "
                         + "some cards — band control on the Bands & APs page still works.",
                           "B.TextDim");
                }
                else
                {
                    Status(string.Empty, "B.TextDim");
                }

                Bind();
            }
            catch (Exception ex)
            {
                Status("Could not read driver settings: " + ex.Message, "B.Bad");
            }
            finally
            {
                BtnReload.IsEnabled = true;
            }
        }

        private void Bind()
        {
            _filterFellBack = false;

            IEnumerable<AdvancedProperty> shown = ShowAll.IsChecked == true
                ? _all
                : _all.Where(p => p.IsBandRelated);

            List<AdvancedProperty> list = shown.ToList();

            // If the band filter leaves nothing, showing everything beats showing
            // an empty table: vendors name these keywords inconsistently and the
            // filter cannot know every spelling.
            if (list.Count == 0 && _all.Count > 0)
            {
                list = _all.ToList();
                _filterFellBack = true;
            }

            SettingList.ItemsSource = list;
            SettingCount.Text = _all.Count == 0
                ? string.Empty
                : (_filterFellBack ? "all " + _all.Count + " settings" : list.Count + " of " + _all.Count + " settings");
        }

        private void OnToggleAll(object sender, RoutedEventArgs e)
        {
            Bind();
        }

        private void OnSettingSelected(object sender, SelectionChangedEventArgs e)
        {
            AdvancedProperty p = SettingList.SelectedItem as AdvancedProperty;
            if (p == null)
            {
                ChangeLabel.Text = "Select a setting to change it";
                ValueBox.ItemsSource = null;
                ValueBox.IsEditable = false;
                BtnApply.IsEnabled = false;
                return;
            }

            ChangeLabel.Text = "Change " + p.DisplayName + " to";

            if (p.ValidValues != null && p.ValidValues.Count > 0)
            {
                ValueBox.IsEditable = false;
                ValueBox.ItemsSource = p.ValidValues;
                ValueBox.SelectedItem = p.DisplayValue;
            }
            else
            {
                // Numeric and free-text parameters have no fixed value list, so
                // the box has to accept typing or they could never be changed.
                ValueBox.ItemsSource = null;
                ValueBox.IsEditable = true;
                ValueBox.Text = p.DisplayValue ?? string.Empty;
            }

            // Enabled only when a value can actually be produced. Previously this
            // was always on, and a row whose current value was absent from its own
            // enum left the box empty and the button silently inert.
            BtnApply.IsEnabled = !string.IsNullOrEmpty(ChosenValue());
            ValueBox.SelectionChanged -= OnValueChanged;
            ValueBox.SelectionChanged += OnValueChanged;
        }

        private void OnValueChanged(object sender, SelectionChangedEventArgs e)
        {
            BtnApply.IsEnabled = !string.IsNullOrEmpty(ChosenValue());
        }

        private string ChosenValue()
        {
            if (ValueBox.IsEditable) return (ValueBox.Text ?? string.Empty).Trim();
            return ValueBox.SelectedItem as string;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            AdvancedProperty p = SettingList.SelectedItem as AdvancedProperty;
            string value = ChosenValue();
            if (p == null)
            {
                Status("Select a setting first.", "B.WarnText");
                return;
            }
            if (string.IsNullOrEmpty(value))
            {
                Status("Choose a value for " + p.DisplayName + " first.", "B.WarnText");
                return;
            }

            _shell.ShowConfirm(
                "Change driver setting",
                "Set " + p.DisplayName + " to " + value + "? The Wi-Fi connection will drop "
                + "briefly while the radio restarts.",
                "Apply setting",
                () => Apply(p, value));
        }

        private async void Apply(AdvancedProperty p, string value)
        {
            Status("Applying…", "B.Accent");
            BtnApply.IsEnabled = false;

            try
            {
                Guid guid = _adapter.Guid;
                string desc = _adapter.Description;
                string keyword = p.RegistryKeyword;

                await Task.Run(() =>
                {
                    // Writing is still done through Set-NetAdapterAdvancedProperty
                    // rather than the registry. A raw registry write does not take
                    // effect until the adapter is restarted, and the cmdlet handles
                    // that restart correctly; reading is the part that had no
                    // reason to pay for a process launch.
                    if (_netAdapterName == null)
                    {
                        _netAdapterName = AdapterProperties.ResolveAdapterName(guid, desc);
                    }
                    if (_netAdapterName == null)
                    {
                        throw new InvalidOperationException(
                            "Could not match this radio to a Windows network adapter, so the "
                            + "setting cannot be applied.");
                    }
                    AdapterProperties.SetAdvanced(_netAdapterName, keyword, value);
                });

                // Reload first, then report. Load() opens by writing "Reading
                // driver settings…" to the same label, which used to wipe the
                // success message in the same dispatcher pass and leave a
                // successful apply looking like it did nothing.
                await LoadAsync();
                Status(p.DisplayName + " set to " + value + ".", "B.Good");
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
            }
            finally
            {
                BtnApply.IsEnabled = true;
            }
        }

        private void OnReload(object sender, RoutedEventArgs e)
        {
            Load();
        }

        private void Status(string text, string brushKey)
        {
            StatusLine.Text = text ?? string.Empty;
            StatusLine.Foreground = (Brush)FindResource(brushKey);
        }
    }
}
