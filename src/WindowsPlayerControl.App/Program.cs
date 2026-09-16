using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using WindowsPlayerControl.Api;
using WindowsPlayerControl.Application;
using WindowsPlayerControl.Infrastructure.Windows;

namespace WindowsPlayerControl.App;

internal static class Program
{
    private const string InstanceMutexName = "Local\\WindowsPlayerControl.SingleInstance";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Windows Player Control is already running.",
                "Windows Player Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            System.Windows.Forms.Application.Run(new TrayApplicationContext());
        }
        catch (PortAlreadyInUseException)
        {
            MessageBox.Show(
                "Windows Player Control is already running, or the configured API port is in use.",
                "Windows Player Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly SettingsStore settingsStore = new();
    private readonly MediaService mediaService;
    private readonly UpdateChecker updateChecker = new();
    private readonly CancellationTokenSource updateCheckCancellation = new();
    private readonly SemaphoreSlim updateCheckGate = new(1, 1);
    private System.Windows.Forms.Timer? updateCheckTimer;
    private Task? updateCheckTask;
    private AppSettings settings;
    private ApiHost apiHost;
    private ToolStripMenuItem statusItem = null!;

    public TrayApplicationContext()
    {
        settings = settingsStore.LoadOrCreate();
        if (!IsPortAvailable(settings.BindAddress, settings.Port))
        {
            throw new PortAlreadyInUseException();
        }

        mediaService = new MediaService(new WindowsMediaController(), new CoreAudioController());
        apiHost = ApiHost.Create(new ApiHostOptions(settings.BindAddress, settings.Port, settings.Secret), mediaService);

        var menu = new ContextMenuStrip();
        statusItem = new ToolStripMenuItem("Status: API starting");
        statusItem.Click += (_, _) => MessageBox.Show($"API is running on {settings.BindAddress}:{settings.Port}.");
        menu.Items.Add(statusItem);
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Check for updates", null, async (_, _) => await RunUpdateCheckAsync(manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());

        trayIcon = new NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "Windows Player Control",
            ContextMenuStrip = menu,
            Visible = true
        };

        _ = StartApiAsync();
        if (settings.CheckForUpdates)
        {
            updateCheckTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            updateCheckTimer.Tick += async (_, _) =>
            {
                updateCheckTimer.Stop();
                updateCheckTimer.Dispose();
                updateCheckTimer = null;
                await RunUpdateCheckAsync(manual: false);
            };
            updateCheckTimer.Start();
        }
    }

    private static bool IsPortAvailable(string bindAddress, int port)
    {
        var address = ResolveBindAddress(bindAddress);
        using var listener = new TcpListener(address, port);
        try
        {
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static IPAddress ResolveBindAddress(string bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress) || bindAddress is "0.0.0.0" or "*")
        {
            return IPAddress.Any;
        }

        if (IPAddress.TryParse(bindAddress, out var address))
        {
            return address;
        }

        return Dns.GetHostAddresses(bindAddress).FirstOrDefault() ?? IPAddress.Any;
    }

    private static Icon LoadApplicationIcon()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("WindowsPlayerControl.App.Assets.app-icon.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }

    private async Task StartApiAsync()
    {
        try
        {
            await apiHost.StartAsync();
            statusItem.Text = $"Status: listening on {settings.BindAddress}:{settings.Port}";
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Could not start API: {exception.Message}", "Windows Player Control");
        }
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(settings, SaveSettings);
        form.ShowDialog();
    }

    private void SaveSettings(AppSettings updated)
    {
        settingsStore.Save(updated);
        new StartupManager().Apply(updated.StartWithWindows, System.Windows.Forms.Application.ExecutablePath);
        settings = updated;
        _ = RestartApiAsync();
    }

    private async Task RunUpdateCheckAsync(bool manual)
    {
        if (!await updateCheckGate.WaitAsync(0))
        {
            return;
        }

        var task = CheckForUpdatesAsync(manual);
        updateCheckTask = task;
        try
        {
            await task;
        }
        finally
        {
            if (ReferenceEquals(updateCheckTask, task))
            {
                updateCheckTask = null;
            }

            updateCheckGate.Release();
        }
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        try
        {
            var current = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var latest = await updateChecker.GetLatestReleaseAsync(updateCheckCancellation.Token);
            if (latest is null || !Version.TryParse(latest.Version, out var latestVersion))
            {
                if (manual)
                {
                    MessageBox.Show("The latest release version could not be determined.", "Updates");
                }
                return;
            }

            if (latestVersion <= current)
            {
                if (manual)
                {
                    MessageBox.Show("You are using the latest version.", "Updates");
                }
                return;
            }

            var result = MessageBox.Show(
                $"A new version {latest.Version} is available. Open the release page?",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (result == DialogResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(latest.Url) { UseShellExecute = true });
                }
                catch (Win32Exception)
                {
                    MessageBox.Show("Could not open the release page.", "Updates");
                }
            }
        }
        catch (OperationCanceledException) when (updateCheckCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or JsonException
                or InvalidOperationException)
        {
            if (manual)
            {
                MessageBox.Show("Could not check for updates. Check your Internet connection.", "Updates");
            }
        }
    }

    private async Task RestartApiAsync()
    {
        try
        {
            await apiHost.StopAsync();
            await apiHost.DisposeAsync();
            apiHost = ApiHost.Create(new ApiHostOptions(settings.BindAddress, settings.Port, settings.Secret), mediaService);
            await apiHost.StartAsync();
            statusItem.Text = $"Status: listening on {settings.BindAddress}:{settings.Port}";
        }
        catch (Exception exception)
        {
            statusItem.Text = "Status: API error";
            MessageBox.Show($"Could not restart API: {exception.Message}", "Windows Player Control");
        }
    }

    private async Task ExitAsync()
    {
        trayIcon.Visible = false;
        trayIcon.Dispose();
        updateCheckCancellation.Cancel();
        updateCheckTimer?.Dispose();
        if (updateCheckTask is not null)
        {
            await updateCheckTask;
        }
        updateChecker.Dispose();
        updateCheckCancellation.Dispose();
        updateCheckGate.Dispose();
        await apiHost.StopAsync();
        await apiHost.DisposeAsync();
        ExitThread();
    }
}

internal sealed class PortAlreadyInUseException : Exception;
