using WindowsPlayerControl.Infrastructure.Windows;

namespace WindowsPlayerControl.App;

internal sealed class SettingsForm : Form
{
    private readonly TextBox bindAddress = new();
    private readonly NumericUpDown port = new();
    private readonly TextBox secret = new();
    private readonly CheckBox startWithWindows = new();
    private readonly CheckBox checkForUpdates = new();
    private readonly Action<AppSettings> saveSettings;

    public SettingsForm(AppSettings settings, Action<AppSettings> saveSettings)
    {
        this.saveSettings = saveSettings;
        Text = "Windows Player Control — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 250);

        bindAddress.Text = settings.BindAddress;
        port.Minimum = 1024;
        port.Maximum = 65535;
        port.Value = settings.Port;
        secret.Text = settings.Secret;
        secret.UseSystemPasswordChar = false;
        startWithWindows.Text = "Start with Windows";
        startWithWindows.AutoSize = true;
        startWithWindows.Checked = settings.StartWithWindows;
        checkForUpdates.Text = "Check for updates";
        checkForUpdates.AutoSize = true;
        checkForUpdates.Checked = settings.CheckForUpdates;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 6,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, 0, "Bind address", bindAddress);
        AddRow(table, 1, "Port", port);
        AddRow(table, 2, "Secret", secret);
        table.Controls.Add(startWithWindows, 1, 3);
        table.Controls.Add(checkForUpdates, 1, 4);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = "Save", AutoSize = true };
        save.Click += SaveClicked;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        table.Controls.Add(buttons, 0, 5);
        table.SetColumnSpan(buttons, 2);

        Controls.Add(table);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);
    }

    private void SaveClicked(object? sender, EventArgs e)
    {
        if (!TryBuildSettings(out var updated))
        {
            MessageBox.Show("Bind address is required and secret must contain at least 16 URL-safe characters (letters, digits, '-' or '_').", "Invalid settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            saveSettings(updated!);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not save settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal bool TryBuildSettings(out AppSettings? updated)
    {
        if (string.IsNullOrWhiteSpace(bindAddress.Text) || !SettingsStore.IsValidSecret(secret.Text))
        {
            updated = null;
            return false;
        }

        updated = new AppSettings(
            bindAddress.Text.Trim(),
            (int)port.Value,
            secret.Text,
            startWithWindows.Checked,
            checkForUpdates.Checked);
        return true;
    }
}
