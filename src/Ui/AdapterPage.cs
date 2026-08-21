using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BandPilot.Adapter;
using BandPilot.Wifi;

namespace BandPilot.Ui
{
    /// <summary>
    /// Driver-level radio settings: preferred band, roaming aggressiveness,
    /// channel widths and whatever else the installed driver exposes.
    ///
    /// Pinning an AP on the Bands page is per-connection. Settings here are
    /// standing preferences, which is what stops Windows from wandering back to
    /// 2.4 GHz an hour later.
    /// </summary>
    public sealed class AdapterPage : UserControl
    {
        private readonly ListView _list = new ListView();
        private readonly ComboBox _values = new ComboBox();
        private readonly Button _apply;
        private readonly Button _reload;
        private readonly CheckBox _showAll = new CheckBox();
        private readonly Label _header = new Label();
        private readonly Label _hint = new Label();

        private string _adapterName;
        private List<AdvancedProperty> _props = new List<AdvancedProperty>();

        public AdapterPage()
        {
            _apply = Theme.MakeButton("Apply setting", true);
            _reload = Theme.MakeButton("Reload", false);

            BackColor = Theme.Background;
            Dock = DockStyle.Fill;
            Padding = new Padding(16);

            BuildLayout();

            _list.SelectedIndexChanged += (s, e) => OnSelectionChanged();
            _apply.Click += (s, e) => ApplySelected();
            _reload.Click += (s, e) => Reload();
            _showAll.CheckedChanged += (s, e) => Populate();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Theme.Background
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

            var head = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            _header.Text = "Adapter settings";
            _header.Font = Theme.TitleFont;
            _header.ForeColor = Theme.Text;
            _header.AutoSize = true;
            _header.Location = new Point(0, 0);
            head.Controls.Add(_header);

            _showAll.Text = "Show every driver setting";
            _showAll.ForeColor = Theme.TextDim;
            _showAll.Font = Theme.UiFont;
            _showAll.AutoSize = true;
            _showAll.Location = new Point(0, 28);
            head.Controls.Add(_showAll);
            root.Controls.Add(head, 0, 0);

            Theme.StyleList(_list);
            _list.Dock = DockStyle.Fill;
            _list.Columns.Add("Setting", 300);
            _list.Columns.Add("Current value", 220);
            _list.Columns.Add("Driver keyword", 220);
            root.Controls.Add(_list, 0, 1);

            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Theme.Background,
                Padding = new Padding(0, 8, 0, 0)
            };
            Label l = Theme.MakeLabel("Change to", true);
            l.Margin = new Padding(0, 8, 8, 0);
            row.Controls.Add(l);

            _values.DropDownStyle = ComboBoxStyle.DropDownList;
            _values.Width = 260;
            _values.BackColor = Theme.SurfaceAlt;
            _values.ForeColor = Theme.Text;
            _values.FlatStyle = FlatStyle.Flat;
            _values.Font = Theme.UiFont;
            _values.Margin = new Padding(0, 4, 12, 0);
            row.Controls.Add(_values);

            _apply.Width = 130;
            _apply.Margin = new Padding(0, 0, 8, 0);
            _reload.Width = 90;
            row.Controls.Add(_apply);
            row.Controls.Add(_reload);
            root.Controls.Add(row, 0, 2);

            _hint.Dock = DockStyle.Fill;
            _hint.ForeColor = Theme.TextDim;
            _hint.Font = Theme.UiFont;
            _hint.Text = "Applying a setting resets the radio, so the connection drops for a moment.";
            root.Controls.Add(_hint, 0, 3);

            Controls.Add(root);
        }

        public void SetAdapter(WifiAdapter adapter)
        {
            if (adapter == null) return;
            _header.Text = adapter.Description;

            try
            {
                _adapterName = AdapterProperties.ResolveAdapterName(adapter.Guid, adapter.Description);
            }
            catch (Exception)
            {
                _adapterName = null;
            }

            Reload();
        }

        private void Reload()
        {
            if (string.IsNullOrEmpty(_adapterName))
            {
                _list.Items.Clear();
                _hint.Text = "Could not match this radio to a Windows network adapter, so driver settings are unavailable.";
                _hint.ForeColor = Theme.Warn;
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                _props = AdapterProperties.GetAdvanced(_adapterName);
                _hint.Text = "Applying a setting resets the radio, so the connection drops for a moment.";
                _hint.ForeColor = Theme.TextDim;
                Populate();
            }
            catch (Exception ex)
            {
                _hint.Text = "Could not read driver settings: " + ex.Message;
                _hint.ForeColor = Theme.Bad;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void Populate()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (AdvancedProperty p in _props)
                {
                    if (!_showAll.Checked && !p.IsBandRelated) continue;

                    var item = new ListViewItem(new[]
                    {
                        p.DisplayName ?? "(unnamed)",
                        p.DisplayValue ?? "",
                        p.RegistryKeyword ?? ""
                    })
                    {
                        Tag = p,
                        UseItemStyleForSubItems = false
                    };
                    item.SubItems[2].ForeColor = Theme.TextDim;
                    item.SubItems[2].Font = Theme.MonoFont;
                    if (p.IsBandRelated) item.SubItems[0].ForeColor = Theme.Accent;
                    _list.Items.Add(item);
                }

                if (_list.Items.Count == 0)
                {
                    _list.Items.Add(new ListViewItem(new[]
                    {
                        "No band-related settings reported by this driver", "", ""
                    }));
                }
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        private void OnSelectionChanged()
        {
            _values.Items.Clear();
            if (_list.SelectedItems.Count == 0) return;

            var p = _list.SelectedItems[0].Tag as AdvancedProperty;
            if (p == null) return;

            foreach (string v in p.ValidValues) _values.Items.Add(v);

            // A driver that reports no enumerated values takes free-form input,
            // which this page deliberately does not offer.
            if (_values.Items.Count == 0 && !string.IsNullOrEmpty(p.DisplayValue))
            {
                _values.Items.Add(p.DisplayValue);
            }

            int idx = _values.Items.IndexOf(p.DisplayValue ?? "");
            if (idx >= 0) _values.SelectedIndex = idx;
            else if (_values.Items.Count > 0) _values.SelectedIndex = 0;
        }

        private void ApplySelected()
        {
            if (_list.SelectedItems.Count == 0 || _values.SelectedItem == null)
            {
                MessageBox.Show(this, "Pick a setting and a value first.", "BandPilot",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var p = _list.SelectedItems[0].Tag as AdvancedProperty;
            if (p == null || string.IsNullOrEmpty(p.RegistryKeyword)) return;

            string value = _values.SelectedItem.ToString();
            if (string.Equals(value, p.DisplayValue, StringComparison.Ordinal))
            {
                _hint.Text = "That is already the current value.";
                _hint.ForeColor = Theme.TextDim;
                return;
            }

            DialogResult ok = MessageBox.Show(this,
                "Set \"" + p.DisplayName + "\" to \"" + value + "\"?\n\n" +
                "The Wi-Fi connection will drop briefly while the radio restarts.",
                "BandPilot", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ok != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                AdapterProperties.SetAdvanced(_adapterName, p.RegistryKeyword, value);
                _hint.Text = "Applied. \"" + p.DisplayName + "\" is now \"" + value + "\".";
                _hint.ForeColor = Theme.Good;
                Reload();
            }
            catch (Exception ex)
            {
                _hint.Text = "Could not apply: " + ex.Message;
                _hint.ForeColor = Theme.Bad;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }
}
