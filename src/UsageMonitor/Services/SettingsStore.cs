using System;
using System.IO;
using System.Security;
using System.Text.Json;

namespace UsageMonitor.Services;

public sealed class SettingsStore
{
    public SettingsStore(string baseDirectory = "")
    {
        var directory = baseDirectory;
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageMonitor");
        FilePath = Path.Combine(Path.GetFullPath(directory), "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, ProfileJson.Options) ?? new();
            return Normalize(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException)
        {
            return new();
        }
    }

    public bool TrySave(AppSettings settings)
    {
        var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? "");
            var json = JsonSerializer.SerializeToUtf8Bytes(Normalize(settings), ProfileJson.Options);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(true);
            }

            File.Move(temporaryPath, FilePath, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var finitePosition = double.IsFinite(settings.Left) && double.IsFinite(settings.Top);
        var appearance = AppearanceProfile.Normalize(new()
        {
            Theme = settings.Theme,
            Scale = settings.Scale,
            Opacity = settings.Opacity
        });
        var fiveHourDisplay = settings.FiveHourDisplay;
        if (!Enum.IsDefined(fiveHourDisplay))
            fiveHourDisplay = FiveHourDisplay.Automatic;
        return settings with
        {
            FiveHourDisplay = fiveHourDisplay,
            Theme = appearance.Theme,
            Scale = appearance.Scale,
            Opacity = appearance.Opacity,
            HasPosition = settings.HasPosition && finitePosition,
            Left = NormalizePosition(settings.Left, finitePosition),
            Top = NormalizePosition(settings.Top, finitePosition),
            RefreshSeconds = Math.Clamp(settings.RefreshSeconds, 30, 600),
            CodexPath = settings.CodexPath ?? "",
            ClaudePath = settings.ClaudePath ?? "",
            GrokPath = settings.GrokPath ?? ""
        };
    }

    private static double NormalizePosition(double position, bool finitePosition)
    {
        if (finitePosition)
            return position;
        return 0;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }

}
