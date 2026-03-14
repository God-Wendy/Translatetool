using System.Text.Json;
using TranslateTool.Models;

namespace TranslateTool.Services;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsFile;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AppSettings? _cache;

    public AppSettingsStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslateTool");
        Directory.CreateDirectory(root);
        _settingsFile = Path.Combine(root, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null)
            {
                return _cache;
            }

            if (!File.Exists(_settingsFile))
            {
                _cache = new AppSettings();
                await SaveInternalAsync(_cache, cancellationToken);
                return _cache;
            }

            await using var stream = File.OpenRead(_settingsFile);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
            _cache = settings ?? new AppSettings();
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            _cache = settings;
            await SaveInternalAsync(settings, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveInternalAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(_settingsFile);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(_settingsFile);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }
}
