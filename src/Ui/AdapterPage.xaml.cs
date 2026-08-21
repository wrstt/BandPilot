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
        private string _netAdapterName;
        private List<AdvancedProperty> _all = new List<AdvancedProperty>();

        public AdapterPage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();
        }

        public void OnShown(WifiAdapter adapter)
        {
            if (adapter == null)
            {
                SubHeader.Text = "No adapter selected.";
                return;
            }

            bool changed = _adapter == null || _adapter.Guid != adapter.Guid;
            _adapter = adapter;
            SubHeader.Text = adapter.Description + " — settings read live from the driver.";

            if (changed || _all.Count == 0) Load();
        }

        private async void Load()
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
                    string name = AdapterProperties.ResolveAdapterName(guid, desc);
                    List<AdvancedProperty> props = name != null
                        ? AdapterProperties.GetAdvanced(name)
                        : new List<AdvancedProperty>();
                    return new Tuple<string, List<AdvancedProperty>>(name, props);
                });

                _netAdapterName = loaded.Item1;
                _all = loaded.Item2 ?? new List<AdvancedProperty>();

                if (_netAdapterName == null)
                {
                    Status("Could not match this radio to a Windows network adapter.", "B.WarnText");
                }
                else if (_all.Count == 0)
                {
                    // Common on Realtek and some MediaTek drivers, which expose
                    // few or no advanced keywords. Not an error.
                    Status("This driver exposes no adjustable settings.", "B.TextDim");
                }
                else
                {
                    Status(string.Empty, "B.TextDim");
                }

                Bind();
            }
            catch (Exception ex)
            {
                Status(ex.Message, "B.Bad");
            }
            finally
            {
                BtnReload.IsEnabled = true;
            }
        }

        private void Bind()
        {
            IEnumerable<AdvancedProperty> shown = ShowAll.IsChecked == true
                ? _all
                : _all.Where(p => p.IsBandRelated);

            List<AdvancedProperty> list = shown.ToList();

            // If the filter leaves nothing, showing everything beats showing an
            // empty table: vendors name these keywords inconsistently and the
            // band filter cannot know every spelling.
            if (list.Count == 0 && _all.Count > 0) list = _all.ToList();

            SettingList.ItemsSource = list;
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
                BtnApply.IsEnabled = false;
                return;
            }

            ChangeLabel.Text = "Change " + p.DisplayName + " to";
            ValueBox.ItemsSource = p.ValidValues;
            ValueBox.SelectedItem = p.DisplayValue;
            BtnApply.IsEnabled = p.ValidValues != null && p.ValidValues.Count > 0;
        }

        private void OnApply(object sender, RoutedEventArgs e)
        {
            AdvancedProperty p = SettingList.SelectedItem as AdvancedProperty;
            string value = ValueBox.SelectedItem as string;
            if (p == null || value == null || _netAdapterName == null) return;

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
                string name = _netAdapterName;
                string keyword = p.RegistryKeyword;
                await Task.Run(() => AdapterProperties.SetAdvanced(name, keyword, value));
                Status(p.DisplayName + " set to " + value + ".", "B.Good");
                Load();
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
