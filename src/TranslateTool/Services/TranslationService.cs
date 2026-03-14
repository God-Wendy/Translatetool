using TranslateTool.Models;

namespace TranslateTool.Services;

public sealed class TranslationService
{
    private readonly IReadOnlyList<ITranslationProvider> _providers;
    private readonly AppSettingsStore _settingsStore;

    public TranslationService(IEnumerable<ITranslationProvider> providers, AppSettingsStore settingsStore)
    {
        _providers = providers.ToList();
        _settingsStore = settingsStore;
    }

    public async Task<TranslationResult> TranslateWithFallbackAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.BaiduTranslateAppId) || string.IsNullOrWhiteSpace(settings.BaiduTranslateKey))
        {
            return new TranslationResult(false, request.Text, string.Empty, "baidu", "请先在设置页填写百度翻译 AppId / Key。");
        }

        TranslationResult? last = null;
        foreach (var provider in _providers)
        {
            try
            {
                var result = await provider.TranslateAsync(request, settings, cancellationToken);
                if (result.Success)
                {
                    return result;
                }

                last = result;
            }
            catch (Exception ex)
            {
                last = new TranslationResult(false, request.Text, string.Empty, "baidu", ex.Message);
            }
        }

        return last ?? new TranslationResult(false, request.Text, string.Empty, "baidu", "翻译服务不可用。");
    }
}
