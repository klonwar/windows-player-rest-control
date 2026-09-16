using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WindowsPlayerControl.Infrastructure.Windows;

public sealed record AppSettings(string BindAddress, int Port, string Secret, bool StartWithWindows);

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string filePath = Path.Combine(
        Environment.GetEnvironmentVariable("WINDOWS_PLAYER_CONTROL_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsPlayerControl"),
        "settings.json");

    public AppSettings LoadOrCreate()
    {
        if (!File.Exists(filePath))
        {
            var initial = new AppSettings("127.0.0.1", 5002, CreateSecret(), false);
            Save(initial);
            return initial;
        }

        var persisted = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(filePath), JsonOptions)
            ?? throw new InvalidOperationException("Settings file is empty.");
        var secret = Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(persisted.ProtectedSecret),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser));
        if (!IsValidSecret(secret))
        {
            secret = CreateSecret();
            Save(new AppSettings(persisted.BindAddress, persisted.Port, secret, persisted.StartWithWindows));
        }

        return new(persisted.BindAddress, persisted.Port, secret, persisted.StartWithWindows);
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var persisted = new PersistedSettings(
            settings.BindAddress,
            settings.Port,
            settings.StartWithWindows,
            Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(settings.Secret),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser)));
        File.WriteAllText(filePath, JsonSerializer.Serialize(persisted, JsonOptions));
    }

    public static bool IsValidSecret(string value) =>
        value.Length >= 16 && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static string CreateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');

    private sealed record PersistedSettings(string BindAddress, int Port, bool StartWithWindows, string ProtectedSecret);
}
