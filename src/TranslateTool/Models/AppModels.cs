using Windows.Foundation;

namespace TranslateTool.Models;

public sealed record TranslationRequest(string Text, string SourceLanguage, string TargetLanguage, string TraceId);

public sealed record TranslationResult(
    bool Success,
    string OriginalText,
    string TranslatedText,
    string Provider,
    string ErrorMessage);

public sealed record OcrRequest(byte[] ImageBytes);

public sealed record OcrResult(
    bool Success,
    string RecognizedText,
    string Provider,
    string ErrorMessage);

public sealed record HistoryItem(
    long Id,
    string Type,
    string Source,
    string Result,
    string Provider,
    DateTimeOffset CreatedAt);

public sealed record HistoryQuery(int Limit = 200);

public sealed record CaptureOptions(bool HideCurrentPopup, bool ShowPixelSizeOverlay, bool AllowFreeResize);

public sealed record CapturedImage(byte[] ImageBytes, Rect Region);

public sealed record LanguageOption(string Code, string DisplayNameZh);

public enum BackgroundStyleKind
{
    Gradient = 0,
    Frosted = 1,
    Illustration = 2,
    CustomImage = 3
}

public sealed class AppSettings
{
    public string SourceLanguage { get; set; } = "auto";
    public string TargetLanguage { get; set; } = "zh";
    public string Hotkey { get; set; } = "Ctrl+Shift+A";
    public bool HidePopupBeforeCapture { get; set; } = true;

    public string BaiduTranslateAppId { get; set; } = string.Empty;
    public string BaiduTranslateKey { get; set; } = string.Empty;
    public string BaiduOcrApiKey { get; set; } = string.Empty;
    public string BaiduOcrSecretKey { get; set; } = string.Empty;

    public double UiFontSize { get; set; } = 14;
    public BackgroundStyleKind BackgroundStyle { get; set; } = BackgroundStyleKind.Gradient;
    public string CustomBackgroundImagePath { get; set; } = string.Empty;

    public int HistoryRetentionDays { get; set; } = 30;
}

public interface ITranslationProvider
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IOcrProvider
{
    Task<OcrResult> RecognizeAsync(OcrRequest request, AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IHistoryRepository
{
    Task AddAsync(HistoryItem item, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoryItem>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken = default);
    Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface ICaptureService
{
    Task<CapturedImage?> BeginAsync(CaptureOptions options, Window hostWindow, CancellationToken cancellationToken = default);
}
