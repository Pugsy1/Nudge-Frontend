using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nudge.Core.Abstractions;
using Nudge.Core.Diagnostics;
using Nudge.Core.Models;

namespace Nudge.Vpx.Settings;

/// <summary>
/// Stores Nudge's own settings as JSON under %LocalAppData%\Nudge.
///
/// Two rules shape this class. A broken settings file must never stop Nudge starting, so every read
/// failure falls back to defaults. And a crash mid-write must never leave an unreadable file, so
/// writes go to a temporary file that is then moved into place.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IFileSystem _fileSystem;
    private readonly IPathRedactor _redactor;
    private readonly ILogger<JsonSettingsService> _logger;

    /// <summary>Guards against two saves interleaving and corrupting each other.</summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonSettingsService(
        IFileSystem fileSystem,
        string settingsFilePath,
        IPathRedactor redactor,
        ILogger<JsonSettingsService> logger)
    {
        _fileSystem = fileSystem;
        SettingsFilePath = settingsFilePath;
        _redactor = redactor;
        _logger = logger;
    }

    public string SettingsFilePath { get; }

    public async Task<NudgeSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_fileSystem.File.Exists(SettingsFilePath))
            {
                _logger.LogInformation(
                    "No settings file at {SettingsPath} yet; starting with defaults.",
                    _redactor.Redact(SettingsFilePath));
                return new NudgeSettings();
            }

            string json = await _fileSystem.File.ReadAllTextAsync(SettingsFilePath, cancellationToken)
                .ConfigureAwait(false);

            NudgeSettings? settings = JsonSerializer.Deserialize<NudgeSettings>(json, SerializerOptions);

            if (settings is null)
            {
                _logger.LogWarning(
                    "The settings file at {SettingsPath} was empty; using defaults.",
                    _redactor.Redact(SettingsFilePath));
                return new NudgeSettings();
            }

            return settings;
        }
        catch (JsonException ex)
        {
            // Deliberately not deleted or "repaired". The user's file is left exactly as it is so it
            // can be inspected; Nudge simply carries on with defaults.
            _logger.LogWarning(
                ex,
                "The settings file at {SettingsPath} is not valid JSON. Using defaults and leaving the "
                + "file untouched.",
                _redactor.Redact(SettingsFilePath));
            return new NudgeSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Could not read the settings file at {SettingsPath}. Using defaults.",
                _redactor.Redact(SettingsFilePath));
            return new NudgeSettings();
        }
    }

    public async Task SaveAsync(NudgeSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? directory = _fileSystem.Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory) && !_fileSystem.Directory.Exists(directory))
            {
                _fileSystem.Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, SerializerOptions);

            // Write beside the target, then move over it, so a crash cannot truncate the real file.
            string temporaryPath = SettingsFilePath + ".tmp";
            await _fileSystem.File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            _fileSystem.File.Move(temporaryPath, SettingsFilePath, overwrite: true);

            _logger.LogInformation("Saved settings to {SettingsPath}.", _redactor.Redact(SettingsFilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failing to save is worth telling the user about, but is not worth crashing over.
            _logger.LogError(
                ex,
                "Could not save settings to {SettingsPath}.",
                _redactor.Redact(SettingsFilePath));
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
