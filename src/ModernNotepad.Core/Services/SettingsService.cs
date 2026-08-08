using System.Text.Json;
using System.Text.Json.Serialization;
using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Services;

public sealed class SettingsService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    public SettingsService(string? baseDirectory = null)
    {
        BaseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModernNotepad");
        SettingsPath = Path.Combine(BaseDirectory, "settings.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new GrammarAnalysisModeJsonConverter());
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string BaseDirectory { get; }
    public string SettingsPath { get; }
    public string? LastLoadWarning { get; private set; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(BaseDirectory);
            LastLoadWarning = null;

            if (!File.Exists(SettingsPath))
            {
                return AppSettings.CreateDefaults();
            }

            try
            {
                var json = await File.ReadAllTextAsync(SettingsPath, cancellationToken).ConfigureAwait(false);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions)
                    ?? AppSettings.CreateDefaults();
                settings.Normalize();
                return settings;
            }
            catch (Exception exception) when (
                exception is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                LastLoadWarning = $"Settings could not be read and defaults were loaded: {exception.Message}";
                PreserveCorruptedSettingsFile();
                return AppSettings.CreateDefaults();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(BaseDirectory);
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await AtomicFileWriter.WriteBytesAsync(
                SettingsPath,
                TextEncodingInfo.Utf8NoBom.Encode(json),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class GrammarAnalysisModeJsonConverter : JsonConverter<GrammarAnalysisMode>
    {
        public override GrammarAnalysisMode Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (string.Equals(value, "AI", StringComparison.OrdinalIgnoreCase))
                {
                    return GrammarAnalysisMode.OpenAI;
                }

                if (Enum.TryParse<GrammarAnalysisMode>(value, ignoreCase: true, out var mode)
                    && Enum.IsDefined(typeof(GrammarAnalysisMode), mode))
                {
                    return mode;
                }

                throw new JsonException($"Unknown grammar analysis mode '{value}'.");
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue))
            {
                return (GrammarAnalysisMode)numericValue;
            }

            throw new JsonException("Grammar analysis mode must be a string or integer.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            GrammarAnalysisMode value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                GrammarAnalysisMode.Traditional => "Traditional",
                GrammarAnalysisMode.OpenAI => "OpenAI",
                GrammarAnalysisMode.PythonSpacy => "PythonSpacy",
                GrammarAnalysisMode.PythonNltk => "PythonNltk",
                GrammarAnalysisMode.GoogleCloudNaturalLanguage => "GoogleCloudNaturalLanguage",
                GrammarAnalysisMode.Provider => "Provider",
                _ => "Traditional"
            });
        }
    }

    private void PreserveCorruptedSettingsFile()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            var target = Path.Combine(
                BaseDirectory,
                $"settings.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            File.Move(SettingsPath, target, overwrite: true);
        }
        catch
        {
            // A warning is already exposed through LastLoadWarning.
        }
    }
}
