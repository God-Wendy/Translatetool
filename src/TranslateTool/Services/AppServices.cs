using TranslateTool.Models;
using TranslateTool.Providers;

namespace TranslateTool.Services;

public sealed class AppServices
{
    public AppSettingsStore SettingsStore { get; }
    public TranslationService TranslationService { get; }
    public OcrService OcrService { get; }
    public ICaptureService CaptureService { get; }
    public IHistoryRepository HistoryRepository { get; }
    public GlobalHotkeyService GlobalHotkeyService { get; }

    public AppServices()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        SettingsStore = new AppSettingsStore();
        HistoryRepository = new SqliteHistoryRepository();
        GlobalHotkeyService = new GlobalHotkeyService();
        CaptureService = new CaptureService();

        ITranslationProvider[] translationProviders =
        [
            new BaiduTranslationProvider(httpClient)
        ];

        IOcrProvider[] ocrProviders =
        [
            new BaiduOcrProvider(httpClient)
        ];

        TranslationService = new TranslationService(translationProviders, SettingsStore);
        OcrService = new OcrService(ocrProviders, SettingsStore);
    }
}
