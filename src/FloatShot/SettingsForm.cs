using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FloatShot;

internal sealed class SettingsForm : Form
{
    private readonly Settings _s;

    private TextBox  _txtFolder    = null!;
    private TextBox  _txtHotkey    = null!;
    private TextBox  _txtRegionHk  = null!;
    private TextBox  _txtFullHk    = null!;
    private ComboBox _cmbDefault   = null!;
    private CheckBox _chkClipboard = null!;
    private CheckBox _chkOpenFolder= null!;
    private CheckBox _chkButton    = null!;
    private CheckBox _chkStartup   = null!;

    public bool RestartHotkeysRequired { get; private set; }
    public bool RestartButtonRequired  { get; private set; }

    public SettingsForm(Settings s)
    {
        _s = s;
        Text          = "FloatShot Settings";
        Icon          = AppIcon.Get();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox   = false;
        MinimizeBox   = false;
        ClientSize    = new Size(660, 750);
        Font          = new Font("Segoe UI", 9.5f);
        BackColor     = Color.FromArgb(248, 248, 248);

        BuildUi();
        LoadValues();
    }

    private void BuildUi()
    {
        const int labelX = 38;
        const int fieldX = 190;
        const int fieldW = 420;
        const int btnW = 88;

        AddSectionTitle("Save", 28);
        Controls.Add(MakeLabel("Save folder", labelX, 74, 130));
        _txtFolder = MakeText(fieldX, 68, 312);
        Controls.Add(_txtFolder);
        var btnBrowse = MakeButton("Browse...", 514, 67, btnW);
        btnBrowse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = _txtFolder.Text };
            if (d.ShowDialog(this) == DialogResult.OK) _txtFolder.Text = d.SelectedPath;
        };
        Controls.Add(btnBrowse);

        AddSectionTitle("Capture", 132);
        Controls.Add(MakeLabel("Default mode", labelX, 178, 130));
        _cmbDefault = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Left = fieldX, Top = 172, Width = fieldW, Height = 32,
            Font = Font
        };
        _cmbDefault.Items.AddRange(new object[]
        {
            "Region (drag to select)",
            "Full screen (all monitors)",
            "Primary screen",
            "Active window"
        });
        Controls.Add(_cmbDefault);

        AddSectionTitle("Hotkeys", 236);
        Controls.Add(MakeLabel("Default mode", labelX, 288, 130));
        _txtHotkey = MakeText(fieldX, 280, fieldW); Controls.Add(_txtHotkey);

        Controls.Add(MakeLabel("Region", labelX, 344, 130));
        _txtRegionHk = MakeText(fieldX, 336, fieldW); Controls.Add(_txtRegionHk);

        Controls.Add(MakeLabel("Full screen", labelX, 400, 130));
        _txtFullHk = MakeText(fieldX, 392, fieldW); Controls.Add(_txtFullHk);

        var lblHint = new Label
        {
            Text = "Format: Ctrl+Alt+Shift+S  ·  Modifiers: Ctrl, Alt, Shift, Win",
            Left = fieldX, Top = 438, AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Font = new Font("Segoe UI", 8.5f)
        };
        Controls.Add(lblHint);

        AddSectionTitle("Behavior", 492);
        _chkClipboard  = MakeCheck("Copy screenshot to clipboard",    fieldX, 536, fieldW);
        _chkOpenFolder = MakeCheck("Open folder after capture",       fieldX, 572, fieldW);
        _chkButton     = MakeCheck("Show floating button",            fieldX, 608, fieldW);
        _chkStartup    = MakeCheck("Run at Windows startup",          fieldX, 644, fieldW);
        Controls.Add(_chkClipboard);
        Controls.Add(_chkOpenFolder);
        Controls.Add(_chkButton);
        Controls.Add(_chkStartup);

        var btnSave = new Button
        {
            Text = "Save", Left = 458, Top = 568, Width = 88, Height = 30,
            DialogResult = DialogResult.OK
        };
        btnSave.Click += (_, _) => { SaveValues(); Close(); };
        var btnCancel = new Button
        {
            Text = "Cancel", Left = 554, Top = 568, Width = 88, Height = 30,
            DialogResult = DialogResult.Cancel
        };
        btnSave.Top = 708;
        btnCancel.Top = 708;
        Controls.Add(btnSave);
        Controls.Add(btnCancel);
        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private void AddSectionTitle(string text, int y)
    {
        var title = new Label
        {
            Text = text,
            Left = 28,
            Top = y,
            Width = 120,
            Height = 24,
            Font = new Font("Segoe UI Semibold", 10.5f),
            ForeColor = Color.FromArgb(32, 32, 32)
        };
        var line = new Panel
        {
            Left = 142,
            Top = y + 12,
            Width = 490,
            Height = 1,
            BackColor = Color.FromArgb(218, 218, 218)
        };
        Controls.Add(title);
        Controls.Add(line);
    }

    private static Label MakeLabel(string text, int x, int y, int width) => new()
    {
        Text = text,
        Left = x,
        Top = y,
        Width = width,
        Height = 28,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(48, 48, 48)
    };

    private static TextBox MakeText(int x, int y, int w) => new()
    {
        Left = x, Top = y, Width = w, Height = 32,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f)
    };

    private Button MakeButton(string text, int x, int y, int width) => new()
    {
        Text = text,
        Left = x,
        Top = y,
        Width = width,
        Height = 32,
        UseVisualStyleBackColor = true
    };

    private CheckBox MakeCheck(string text, int x, int y, int width) => new()
    {
        Text = text,
        Left = x,
        Top = y,
        Width = width,
        Height = 30,
        UseVisualStyleBackColor = true
    };

    // ===== State =====

    private void LoadValues()
    {
        _txtFolder.Text       = _s.SaveFolder;
        _txtHotkey.Text       = _s.Hotkey;
        _txtRegionHk.Text     = _s.RegionHotkey;
        _txtFullHk.Text       = _s.FullScreenHotkey;
        _cmbDefault.SelectedIndex = (int)_s.DefaultMode;
        _chkClipboard.Checked  = _s.CopyToClipboard;
        _chkOpenFolder.Checked = _s.OpenFolderAfterCapture;
        _chkButton.Checked     = _s.ShowFloatingButton;
        _chkStartup.Checked    = _s.RunAtStartup;
    }

    private void SaveValues()
    {
        var oldHk1 = _s.Hotkey;
        var oldHk2 = _s.RegionHotkey;
        var oldHk3 = _s.FullScreenHotkey;
        var oldShow = _s.ShowFloatingButton;

        _s.SaveFolder             = _txtFolder.Text.Trim();
        _s.Hotkey                 = _txtHotkey.Text.Trim();
        _s.RegionHotkey           = _txtRegionHk.Text.Trim();
        _s.FullScreenHotkey       = _txtFullHk.Text.Trim();
        _s.DefaultMode            = (CaptureMode)_cmbDefault.SelectedIndex;
        _s.CopyToClipboard        = _chkClipboard.Checked;
        _s.OpenFolderAfterCapture = _chkOpenFolder.Checked;
        _s.ShowFloatingButton     = _chkButton.Checked;
        _s.RunAtStartup           = _chkStartup.Checked;
        _s.Save();

        ApplyStartup(_s.RunAtStartup);

        RestartHotkeysRequired = oldHk1 != _s.Hotkey || oldHk2 != _s.RegionHotkey || oldHk3 != _s.FullScreenHotkey;
        RestartButtonRequired  = oldShow != _s.ShowFloatingButton;
    }

    private static void ApplyStartup(bool enable)
    {
        try
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
            if (key is null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue("FloatShot", $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue("FloatShot", throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
