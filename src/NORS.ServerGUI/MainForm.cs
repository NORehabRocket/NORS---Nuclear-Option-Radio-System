using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NORS.Common;
using NORS.Server.Core;

namespace NORS.ServerGUI
{
    /// <summary>
    /// Admin GUI for the NORS relay: live roster grouped by room, kick / ban with reason, persistent
    /// ban management, and a log pane. Flat dark theme. Thin shell over <see cref="RelayServer"/>.
    /// </summary>
    public sealed class MainForm : Form
    {
        private RelayServer _server;

        private readonly TextBox _port = Theme.StyleInput(new TextBox { Text = NorsProtocol.DefaultPort.ToString(), Width = 60 });
        private readonly TextBox _name = Theme.StyleInput(new TextBox { Text = "NORS Relay", Width = 150 });
        private readonly TextBox _adminPass = Theme.StyleInput(new TextBox { Width = 130, UseSystemPasswordChar = true, PlaceholderText = "admin pass (optional)" });
        private readonly CheckBox _voteKick = new CheckBox { Text = "Vote-kick", Checked = true, AutoSize = true, ForeColor = Theme.Text, Padding = new Padding(8, 6, 0, 0) };
        private readonly Button _startStop = new Button { Text = "Start", Width = 84 };
        private readonly Label _status = new Label { AutoSize = true, Text = "Stopped", ForeColor = Theme.SubtleText, Padding = new Padding(10, 7, 0, 0) };

        private readonly ModernListView _clients = new ModernListView();
        private readonly ModernListView _bans = new ModernListView();
        private readonly TextBox _reason = Theme.StyleInput(new TextBox { Width = 220, PlaceholderText = "reason (optional)" });
        private readonly TextBox _log = new TextBox
        {
            Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 130,
            BackColor = Color.FromArgb(24, 24, 24), ForeColor = Theme.Good, Font = Theme.MonoFont, BorderStyle = BorderStyle.None
        };
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 1000 };

        public MainForm()
        {
            Text = "NORS Relay Server";
            Width = 880;
            Height = 620;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UiFont;

            BuildLayout();

            _startStop.Click += (_, __) => ToggleServer();
            _timer.Tick += (_, __) => RefreshUi();
            FormClosing += (_, __) => _server?.Stop();
        }

        // ---- dark title bar (Windows 10 20H1+) ----
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int on = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE = 20 (19 on older builds)
                if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
            }
            catch { }
        }

        private void BuildLayout()
        {
            // --- top bar ---
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 7, 0, 0), BackColor = Theme.Bg };
            top.Controls.Add(Theme.Caption("Port"));
            top.Controls.Add(_port);
            top.Controls.Add(Theme.Caption("Name"));
            top.Controls.Add(_name);
            top.Controls.Add(_adminPass);
            top.Controls.Add(_voteKick);
            top.Controls.Add(Theme.StyleButton(_startStop, Theme.Accent, Theme.AccentHover));
            top.Controls.Add(_status);

            // --- clients tab ---
            _clients.Columns.Add("ID", 66);
            _clients.Columns.Add("Callsign", 130);
            _clients.Columns.Add("Room (host SteamID)", 150);
            _clients.Columns.Add("Host", 48);
            _clients.Columns.Add("Faction", 70);
            _clients.Columns.Add("Steam ID", 140);
            _clients.Columns.Add("IP", 120);
            _clients.Columns.Add("Freqs", 48);
            _clients.Columns.Add("Idle", 54);

            var clientButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, BackColor = Theme.Bg, Padding = new Padding(4, 4, 0, 0) };
            var btnKick = Theme.StyleButton(new Button { Text = "Kick", Width = 84 }, Theme.Neutral, Theme.NeutralHover);
            var btnBan = Theme.StyleButton(new Button { Text = "Ban", Width = 84 }, Theme.Danger, Theme.DangerHover);
            var btnRefresh = Theme.StyleButton(new Button { Text = "Refresh", Width = 84 }, Theme.Neutral, Theme.NeutralHover);
            btnKick.Click += (_, __) => ActOnSelected(ban: false);
            btnBan.Click += (_, __) => ActOnSelected(ban: true);
            btnRefresh.Click += (_, __) => RefreshUi();
            clientButtons.Controls.Add(Theme.Caption("Reason"));
            clientButtons.Controls.Add(_reason);
            clientButtons.Controls.Add(btnKick);
            clientButtons.Controls.Add(btnBan);
            clientButtons.Controls.Add(btnRefresh);

            var clientsTab = new TabPage("Connected") { BackColor = Theme.Bg, ForeColor = Theme.Text, Padding = new Padding(2) };
            clientsTab.Controls.Add(_clients);
            clientsTab.Controls.Add(clientButtons);

            // --- bans tab ---
            _bans.Columns.Add("Steam ID", 150);
            _bans.Columns.Add("Scope", 150);
            _bans.Columns.Add("IP", 120);
            _bans.Columns.Add("Name", 140);
            _bans.Columns.Add("Reason", 180);
            _bans.Columns.Add("When (UTC)", 140);

            var banButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, BackColor = Theme.Bg, Padding = new Padding(4, 4, 0, 0) };
            var btnUnban = Theme.StyleButton(new Button { Text = "Unban selected", Width = 140 }, Theme.Neutral, Theme.NeutralHover);
            btnUnban.Click += (_, __) => UnbanSelected();
            banButtons.Controls.Add(btnUnban);

            var bansTab = new TabPage("Bans") { BackColor = Theme.Bg, ForeColor = Theme.Text, Padding = new Padding(2) };
            bansTab.Controls.Add(_bans);
            bansTab.Controls.Add(banButtons);

            // --- tabs (dark, owner-drawn headers) ---
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed,
                SizeMode = TabSizeMode.Fixed, ItemSize = new Size(120, 28), Appearance = TabAppearance.Normal
            };
            tabs.DrawItem += (s, e) =>
            {
                var tc = (TabControl)s;
                bool sel = e.Index == tc.SelectedIndex;
                using (var b = new SolidBrush(sel ? Theme.Surface : Theme.Bg)) e.Graphics.FillRectangle(b, e.Bounds);
                if (sel) using (var p = new Pen(Theme.Accent, 2))
                        e.Graphics.DrawLine(p, e.Bounds.Left + 2, e.Bounds.Bottom - 1, e.Bounds.Right - 2, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, tc.TabPages[e.Index].Text, Theme.UiFont, e.Bounds,
                    sel ? Theme.Text : Theme.SubtleText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            tabs.TabPages.Add(clientsTab);
            tabs.TabPages.Add(bansTab);

            Controls.Add(tabs);
            Controls.Add(_log);
            Controls.Add(top);
        }

        private void ToggleServer()
        {
            if (_server != null && _server.Running)
            {
                _server.Stop();
                _server = null;
                _startStop.Text = "Start";
                _startStop.BackColor = Theme.Accent;
                _status.Text = "Stopped";
                _status.ForeColor = Theme.SubtleText;
                _port.Enabled = _name.Enabled = _adminPass.Enabled = _voteKick.Enabled = true;
                AppendLog("Server stopped.");
                return;
            }

            if (!int.TryParse(_port.Text, out int port))
            {
                MessageBox.Show("Invalid port.", "NORS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string banFile = Path.Combine(AppContext.BaseDirectory, "nors-bans.txt");
                string adminPass = string.IsNullOrWhiteSpace(_adminPass.Text) ? null : _adminPass.Text;
                _server = new RelayServer(port, string.IsNullOrWhiteSpace(_name.Text) ? "NORS Relay" : _name.Text,
                    banFile, adminPass, _voteKick.Checked);
                _server.Log += OnServerLog;
                _server.Start();
                _startStop.Text = "Stop";
                _startStop.BackColor = Theme.Danger;
                _port.Enabled = _name.Enabled = _adminPass.Enabled = _voteKick.Enabled = false;
                _timer.Start();
                AppendLog($"Listening on UDP {port}. Vote-kick {(_voteKick.Checked ? "on" : "off")}, remote admin {(_server.RemoteAdminEnabled ? "enabled" : "disabled")}.");
                AppendLog("Open this port on your firewall/router.");
                RefreshBans();
            }
            catch (Exception e)
            {
                MessageBox.Show($"Could not start the relay on UDP {_port.Text}:\n{e.Message}", "NORS", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _server = null;
            }
        }

        private void OnServerLog(string line)
        {
            if (IsHandleCreated)
                BeginInvoke(new Action(() => { AppendLog(line); RefreshBans(); }));
        }

        private void AppendLog(string line) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");

        private void RefreshUi()
        {
            if (_server == null) return;
            _status.Text = $"Listening · {_server.ClientCount} client(s) · voice {_server.VoiceReceived} in / {_server.VoiceForwarded} fwd";
            _status.ForeColor = Theme.Good;

            // Remember the selection so the periodic refresh doesn't deselect the player you clicked.
            uint? selectedId = _clients.SelectedItems.Count > 0 ? (uint?)(uint)_clients.SelectedItems[0].Tag : null;
            int topIndex = _clients.TopItem?.Index ?? 0;

            var clients = _server.GetClients();
            // Group the roster by room (each room is a separate game "server"), hosts first.
            clients.Sort((a, b) =>
            {
                int r = a.Room.CompareTo(b.Room);
                if (r != 0) return r;
                int h = b.IsHost.CompareTo(a.IsHost);
                return h != 0 ? h : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            _clients.BeginUpdate();
            _clients.Items.Clear();
            ListViewItem reselect = null;
            foreach (var c in clients)
            {
                var item = new ListViewItem(c.Id.ToString("X8"));
                item.SubItems.Add(c.Name);
                item.SubItems.Add(c.Room == 0 ? "—" : c.Room.ToString());
                item.SubItems.Add(c.IsHost ? "HOST" : "");
                item.SubItems.Add(c.FactionId.ToString());
                item.SubItems.Add(c.SteamId == 0 ? "—" : c.SteamId.ToString());
                item.SubItems.Add($"{c.Ip}:{c.Port}");
                item.SubItems.Add(c.FreqCount.ToString());
                item.SubItems.Add($"{c.IdleSeconds:0.0}s");
                item.Tag = c.Id;
                if (selectedId.HasValue && c.Id == selectedId.Value) reselect = item;
                _clients.Items.Add(item);
            }
            if (reselect != null) { reselect.Selected = true; reselect.Focused = true; }
            if (topIndex > 0 && topIndex < _clients.Items.Count)
                try { _clients.TopItem = _clients.Items[topIndex]; } catch { }
            _clients.EndUpdate();
        }

        private void RefreshBans()
        {
            if (_server == null) return;
            string selectedKey = _bans.SelectedItems.Count > 0 ? (string)_bans.SelectedItems[0].Tag : null;

            var bans = _server.Bans.Snapshot();
            _bans.BeginUpdate();
            _bans.Items.Clear();
            ListViewItem reselect = null;
            foreach (var b in bans)
            {
                var item = new ListViewItem(b.SteamId == 0 ? "—" : b.SteamId.ToString());
                item.SubItems.Add(b.Room == 0 ? "Global" : $"Room {b.Room}");
                item.SubItems.Add(b.Ip);
                item.SubItems.Add(b.Name);
                item.SubItems.Add(b.Reason);
                item.SubItems.Add(b.WhenUtc.ToString("u"));
                item.Tag = b.Key;
                if (selectedKey != null && b.Key == selectedKey) reselect = item;
                _bans.Items.Add(item);
            }
            if (reselect != null) { reselect.Selected = true; reselect.Focused = true; }
            _bans.EndUpdate();
        }

        private void ActOnSelected(bool ban)
        {
            if (_server == null || _clients.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select a connected client first.", "NORS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            uint id = (uint)_clients.SelectedItems[0].Tag;
            string who = _clients.SelectedItems[0].SubItems[1].Text;
            if (ban && MessageBox.Show($"Ban {who}? They won't be able to reconnect.", "Confirm ban",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            string reason = _reason.Text;
            bool ok = ban ? _server.Ban(id, reason) : _server.Kick(id, reason);
            if (!ok) AppendLog("That client is no longer connected.");
            _reason.Clear();
            RefreshUi();
            RefreshBans();
        }

        private void UnbanSelected()
        {
            if (_server == null || _bans.SelectedItems.Count == 0) return;
            string key = (string)_bans.SelectedItems[0].Tag;
            _server.Unban(key);
            RefreshBans();
        }
    }
}
