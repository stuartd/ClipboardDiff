using System.IO;
using System.Text.Json;
using ClipDiff.Windows.Hotkeys;

namespace ClipDiff.Windows.Settings;

internal sealed record ClipDiffSettings(
    string? SelectedExecutablePath = null,
    bool PlaintextWarningAcknowledged = false,
    HotKeyGesture? HotKey = null);

internal sealed class ClipDiffSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public ClipDiffSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipDiff",
            "settings.json");
    }

    public ClipDiffSettings Load()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<ClipDiffSettings>(File.ReadAllText(_settingsPath), JsonOptions) ?? new()
                : new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public bool TrySave(ClipDiffSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _settingsPath, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
