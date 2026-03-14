using TranslateTool.Models;

namespace TranslateTool.Services;

public sealed class OcrService
{
    private readonly IReadOnlyList<IOcrProvider> _providers;
    private readonly AppSettingsStore _settingsStore;

    public OcrService(IEnumerable<IOcrProvider> providers, AppSettingsStore settingsStore)
    {
        _providers = providers.ToList();
        _settingsStore = settingsStore;
    }

    public async Task<OcrResult> RecognizeWithFallbackAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.BaiduOcrApiKey) || string.IsNullOrWhiteSpace(settings.BaiduOcrSecretKey))
        {
            return new OcrResult(false, string.Empty, "baidu", "请先在设置页填写百度 OCR API Key / Secret Key。");
        }

        OcrResult? last = null;
        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.RecognizeAsync(request, settings, cancellationToken);
                if (result.Success)
                {
                    return result;
                }

                last = result;
            }
            catch (Exception ex)
            {
                last = new OcrResult(false, string.Empty, "baidu", ex.Message);
            }
        }

        return last ?? new OcrResult(false, string.Empty, "baidu", "OCR 服务不可用。");
    }
}
