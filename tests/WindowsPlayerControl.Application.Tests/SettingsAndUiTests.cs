using System.Windows.Forms;
using System.Security.Cryptography;
using System.Text;
using WindowsPlayerControl.App;
using WindowsPlayerControl.Infrastructure.Windows;

namespace WindowsPlayerControl.Application.Tests;

public sealed class SettingsAndUiTests
{
    [Fact]
    public void App_icon_is_embedded_for_tray_use()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(
            "WindowsPlayerControl.App.Assets.app-icon.ico");

        Assert.NotNull(stream);
        Assert.True(stream!.Length > 0);
        using var icon = new Icon(stream);
        Assert.NotNull(icon);
        Assert.NotEqual(0, icon.Width);
        Assert.NotEqual(0, icon.Height);
    }

    [Fact]
    public void SettingsStore_generates_url_safe_secret_on_first_run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wpc-first-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previous = Environment.GetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR", directory);
            var settings = new SettingsStore().LoadOrCreate();

            Assert.Equal(5002, settings.Port);
            Assert.Equal(5002, new SettingsStore().LoadOrCreate().Port);
            Assert.True(SettingsStore.IsValidSecret(settings.Secret));
            Assert.DoesNotContain('/', settings.Secret);
            Assert.DoesNotContain('+', settings.Secret);
            Assert.DoesNotContain('=', settings.Secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR", previous);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SettingsStore_migrates_legacy_secret_with_path_separator()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wpc-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previous = Environment.GetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR", directory);
            var legacySecret = "legacy/secret-with-invalid-path";
            var protectedSecret = Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(legacySecret), null, DataProtectionScope.CurrentUser));
            File.WriteAllText(Path.Combine(directory, "settings.json"), $$"""
            {
              "bindAddress": "127.0.0.1",
              "port": 5123,
              "startWithWindows": false,
              "protectedSecret": "{{protectedSecret}}"
            }
            """);

            var migrated = new SettingsStore().LoadOrCreate();

            Assert.NotEqual(legacySecret, migrated.Secret);
            Assert.True(SettingsStore.IsValidSecret(migrated.Secret));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR", previous);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SettingsStore_round_trips_secret_without_plaintext_file_leak()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wpc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previous = Environment.GetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR");
        try
        {
            Environment.SetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR", directory);
            var expected = new AppSettings("127.0.0.1", 54321, "sentinel-secret-for-test", false);
            var store = new SettingsStore();
            store.Save(expected);

            var raw = File.ReadAllText(Path.Combine(directory, "settings.json"));
            var loaded = store.LoadOrCreate();

            Assert.DoesNotContain(expected.Secret, raw, StringComparison.Ordinal);
            Assert.Equal(expected, loaded);
            Assert.True(SettingsStore.IsValidSecret(loaded.Secret));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR", previous);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SettingsForm_exposes_copyable_secret_text()
    {
        var secret = "visible-secret";
        SettingsForm? form = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                form = new SettingsForm(new AppSettings("127.0.0.1", 5123, secret, false), _ => { });
                var secretBox = form.Controls
                    .OfType<TableLayoutPanel>()
                    .Single()
                    .Controls
                    .OfType<TextBox>()
                    .Single(box => box.Text == secret);
                secretBox.SelectAll();
                Assert.False(secretBox.UseSystemPasswordChar);
                Assert.Equal(secret, secretBox.SelectedText);
                Assert.Equal("Start with Windows", form.Controls
                    .OfType<TableLayoutPanel>()
                    .Single()
                    .Controls
                    .OfType<CheckBox>()
                    .Single(box => box.Text == "Start with Windows")
                    .Text);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                form?.Dispose();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void SettingsForm_rejects_unsafe_or_short_secret_and_accepts_valid_secret()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(new AppSettings("127.0.0.1", 5123, "valid-secret-1234", false), _ => { });
                var table = form.Controls.OfType<TableLayoutPanel>().Single();
                var secretBox = table.Controls.OfType<TextBox>().Single(box => box.Text == "valid-secret-1234");

                secretBox.Text = "short";
                Assert.False(form.TryBuildSettings(out _));
                secretBox.Text = "invalid/secret-1234";
                Assert.False(form.TryBuildSettings(out _));
                secretBox.Text = "valid-secret-1234";
                Assert.True(form.TryBuildSettings(out var updated));
                Assert.Equal(secretBox.Text, updated!.Secret);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
