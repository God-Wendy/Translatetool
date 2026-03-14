using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TranslateTool.Models;
using TranslateTool.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using WinRT.Interop;

namespace TranslateTool.Views;

public sealed partial class MainPage : Page
{
    private const string DefaultTargetLanguageCode = "zh";
    private const string DefaultHotkey = "Ctrl+Shift+A";
    private const double MinUiFontSize = 12;
    private const double MaxUiFontSize = 24;
    private const double WideLayoutThreshold = 1000;

    private static readonly HashSet<string> SupportedOcrImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"
    };

    private readonly AppServices _services;
    private readonly ObservableCollection<HistoryItem> _history = [];

    private bool _isLoaded;
    private bool _suppressFontSizeEvent;
    private byte[]? _selectedImageBytes;
    private string _selectedImageHistoryType = "image";

    public MainPage()
    {
        InitializeComponent();

        _services = App.Current.Services;
        UiFontSizeSlider.Minimum = MinUiFontSize;
        UiFontSizeSlider.Maximum = MaxUiFontSize;
        UiFontSizeSlider.StepFrequency = 1;
        HistoryListView.ItemsSource = _history;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        InitializeLanguageSelectors();
        await LoadSettingsAsync();
        await CleanupAndRefreshHistoryAsync();
        await RegisterGlobalHotkeyAsync();
        ApplyImageLayout(MainTabView.ActualWidth);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _services.GlobalHotkeyService.HotkeyPressed -= OnGlobalHotkeyPressed;
        _services.GlobalHotkeyService.Unregister();
    }

    private void OnGoTextTabClicked(object sender, RoutedEventArgs e)
    {
        MainTabView.SelectedIndex = 0;
    }

    private void OnGoImageTabClicked(object sender, RoutedEventArgs e)
    {
        MainTabView.SelectedIndex = 1;
    }

    private void InitializeLanguageSelectors()
    {
        InitializeLanguageCombo(SourceLanguageCombo, LanguageCatalog.SourceLanguages);
        InitializeLanguageCombo(TargetLanguageCombo, LanguageCatalog.TargetLanguages);
        InitializeLanguageCombo(SourceLanguageImageCombo, LanguageCatalog.SourceLanguages);
        InitializeLanguageCombo(TargetLanguageImageCombo, LanguageCatalog.TargetLanguages);
    }

    private static void InitializeLanguageCombo(ComboBox comboBox, IReadOnlyList<LanguageOption> options)
    {
        comboBox.ItemsSource = options;
        comboBox.DisplayMemberPath = nameof(LanguageOption.DisplayNameZh);
        comboBox.SelectedValuePath = nameof(LanguageOption.Code);
    }

    private async void OnTranslateClicked(object sender, RoutedEventArgs e)
    {
        await TranslateTextAsync();
    }

    private async Task TranslateTextAsync()
    {
        if (string.IsNullOrWhiteSpace(SourceTextBox.Text))
        {
            ShowMessage("请输入要翻译的文本。", InfoBarSeverity.Warning);
            return;
        }

        var request = BuildTranslationRequest(
            SourceTextBox.Text,
            SourceLanguageCombo.SelectedValue?.ToString() ?? "auto",
            TargetLanguageCombo.SelectedValue?.ToString() ?? DefaultTargetLanguageCode);

        var result = await _services.TranslationService.TranslateWithFallbackAsync(request);
        if (!result.Success)
        {
            ShowMessage($"翻译失败：{result.ErrorMessage}", InfoBarSeverity.Error);
            return;
        }

        ResultTextBox.Text = result.TranslatedText;
        await _services.HistoryRepository.AddAsync(new HistoryItem(
            0,
            "text",
            result.OriginalText,
            result.TranslatedText,
            result.Provider,
            DateTimeOffset.Now));

        await RefreshHistoryAsync();
        ShowMessage($"翻译成功（{result.Provider}）。", InfoBarSeverity.Success);
    }

    private async void OnSourceTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        var shiftPressed = InputKeyboardSource
            .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (shiftPressed)
        {
            return;
        }

        e.Handled = true;
        await TranslateTextAsync();
    }

    private void OnSwapTextLanguagesClicked(object sender, RoutedEventArgs e)
    {
        SwapLanguageSelections(SourceLanguageCombo, TargetLanguageCombo);
    }

    private void OnSwapImageLanguagesClicked(object sender, RoutedEventArgs e)
    {
        SwapLanguageSelections(SourceLanguageImageCombo, TargetLanguageImageCombo);
    }

    private static void SwapLanguageSelections(ComboBox sourceCombo, ComboBox targetCombo)
    {
        var source = sourceCombo.SelectedValue?.ToString() ?? "auto";
        var target = targetCombo.SelectedValue?.ToString() ?? DefaultTargetLanguageCode;

        if (string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase))
        {
            sourceCombo.SelectedValue = target;
            targetCombo.SelectedValue = DefaultTargetLanguageCode;
            return;
        }

        sourceCombo.SelectedValue = target;
        targetCombo.SelectedValue = source;
    }

    private async void OnSelectImageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = CreateImagePicker();
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            if (!IsSupportedOcrImage(file))
            {
                ShowMessage("仅支持 PNG/JPG/JPEG/BMP/TIF/TIFF 图片。", InfoBarSeverity.Warning);
                return;
            }

            var bytes = await LoadImageBytesAsync(file);
            if (!await SetSelectedImageAsync(bytes, file.Path, "image"))
            {
                return;
            }

            ShowMessage("图片已载入，可点击识别并翻译。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage($"图片上传失败：{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void OnClearImageClicked(object sender, RoutedEventArgs e)
    {
        _selectedImageBytes = null;
        _selectedImageHistoryType = "image";
        ImagePreview.Source = null;
        SelectedImagePathText.Text = "未选择图片";
        ImageOcrTextBox.Text = string.Empty;
        ImageResultTextBox.Text = string.Empty;
    }

    private async void OnImageTranslateClicked(object sender, RoutedEventArgs e)
    {
        await TranslateSelectedImageAsync();
    }

    private async Task TranslateSelectedImageAsync()
    {
        if (_selectedImageBytes is null || _selectedImageBytes.Length == 0)
        {
            ShowMessage("请先上传图片或进行截图。", InfoBarSeverity.Warning);
            return;
        }

        var ocrResult = await _services.OcrService.RecognizeWithFallbackAsync(new OcrRequest(_selectedImageBytes));
        if (!ocrResult.Success || string.IsNullOrWhiteSpace(ocrResult.RecognizedText))
        {
            ShowMessage($"OCR 失败：{ocrResult.ErrorMessage}", InfoBarSeverity.Error);
            return;
        }

        ImageOcrTextBox.Text = ocrResult.RecognizedText;

        var request = BuildTranslationRequest(
            ocrResult.RecognizedText,
            SourceLanguageImageCombo.SelectedValue?.ToString() ?? "auto",
            TargetLanguageImageCombo.SelectedValue?.ToString() ?? DefaultTargetLanguageCode);

        var translateResult = await _services.TranslationService.TranslateWithFallbackAsync(request);
        if (!translateResult.Success)
        {
            ShowMessage($"翻译失败：{translateResult.ErrorMessage}", InfoBarSeverity.Error);
            return;
        }

        ImageResultTextBox.Text = translateResult.TranslatedText;

        await _services.HistoryRepository.AddAsync(new HistoryItem(
            0,
            _selectedImageHistoryType,
            ocrResult.RecognizedText,
            translateResult.TranslatedText,
            $"{ocrResult.Provider} -> {translateResult.Provider}",
            DateTimeOffset.Now));

        await RefreshHistoryAsync();
        ShowMessage("图片识别翻译完成。", InfoBarSeverity.Success);
    }

    private async void OnCaptureFromImagePageClicked(object sender, RoutedEventArgs e)
    {
        await StartCaptureFlowAsync(promptBeforeCapture: true, switchToImageTab: true);
    }

    private void OnGlobalHotkeyPressed(object? sender, EventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() => { _ = StartCaptureFlowAsync(promptBeforeCapture: false, switchToImageTab: true); });
    }

    private async Task StartCaptureFlowAsync(bool promptBeforeCapture, bool switchToImageTab)
    {
        var settings = await _services.SettingsStore.LoadAsync();
        var hidePopup = settings.HidePopupBeforeCapture;

        if (promptBeforeCapture)
        {
            var toggle = new ToggleSwitch
            {
                Header = "截图前隐藏当前窗口",
                IsOn = hidePopup
            };

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "截图选项",
                Content = toggle,
                PrimaryButtonText = "开始截图",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            hidePopup = toggle.IsOn;
            settings.HidePopupBeforeCapture = hidePopup;
            await _services.SettingsStore.SaveAsync(settings);
            HidePopupToggle.IsOn = hidePopup;
        }

        var options = new CaptureOptions(hidePopup, true, true);
        var captured = await _services.CaptureService.BeginAsync(options, App.Current.MainWindow);
        if (captured is null)
        {
            ShowMessage("截图已取消。", InfoBarSeverity.Informational);
            return;
        }

        if (switchToImageTab)
        {
            MainTabView.SelectedIndex = 1;
        }

        var region = captured.Region;
        var label = $"截图区域 {(int)region.Width} x {(int)region.Height}";
        if (!await SetSelectedImageAsync(captured.ImageBytes, label, "snip"))
        {
            return;
        }

        await TranslateSelectedImageAsync();
    }

    private static TranslationRequest BuildTranslationRequest(string text, string sourceLanguage, string targetLanguage)
    {
        return new TranslationRequest(text, sourceLanguage, targetLanguage, Guid.NewGuid().ToString("N"));
    }

    private async Task<bool> SetSelectedImageAsync(byte[] imageBytes, string displayPath, string historyType)
    {
        if (imageBytes.Length == 0)
        {
            ShowMessage("图片数据为空，请重新选择。", InfoBarSeverity.Warning);
            return false;
        }

        try
        {
            ImagePreview.Source = await CreateBitmapImageAsync(imageBytes);
            _selectedImageBytes = imageBytes;
            _selectedImageHistoryType = historyType;
            SelectedImagePathText.Text = displayPath;
            ImageOcrTextBox.Text = string.Empty;
            ImageResultTextBox.Text = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            _selectedImageBytes = null;
            _selectedImageHistoryType = "image";
            ImagePreview.Source = null;
            SelectedImagePathText.Text = "未选择图片";
            ImageOcrTextBox.Text = string.Empty;
            ImageResultTextBox.Text = string.Empty;
            ShowMessage($"图片解码失败：{ex.Message}", InfoBarSeverity.Error);
            return false;
        }
    }

    private async void OnRefreshHistoryClicked(object sender, RoutedEventArgs e)
    {
        await RefreshHistoryAsync();
    }

    private void OnCopySelectedHistoryClicked(object sender, RoutedEventArgs e)
    {
        var selected = HistoryListView.SelectedItems
            .OfType<HistoryItem>()
            .OrderBy(x => x.CreatedAt)
            .ToList();

        if (selected.Count == 0)
        {
            ShowMessage("请先选择至少一条历史记录。", InfoBarSeverity.Warning);
            return;
        }

        var content = string.Join(
            Environment.NewLine + Environment.NewLine,
            selected.Select(x =>
                $"时间: {x.CreatedAt:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"来源: {x.Provider}{Environment.NewLine}" +
                $"原文: {x.Source}{Environment.NewLine}" +
                $"译文: {x.Result}"));

        CopyToClipboard(content);
        ShowMessage($"已复制 {selected.Count} 条历史记录。", InfoBarSeverity.Success);
    }

    private async void OnClearHistoryClicked(object sender, RoutedEventArgs e)
    {
        await _services.HistoryRepository.ClearAsync();
        await RefreshHistoryAsync();
        ShowMessage("历史记录已清空。", InfoBarSeverity.Success);
    }

    private async void OnPickBackgroundImageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = CreateImagePicker();
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var settings = await _services.SettingsStore.LoadAsync();
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslateTool", "backgrounds");
            Directory.CreateDirectory(root);

            var ext = Path.GetExtension(file.Path);
            var target = Path.Combine(root, $"bg-{Guid.NewGuid():N}{ext}");
            File.Copy(file.Path, target, true);

            settings.CustomBackgroundImagePath = target;
            settings.BackgroundStyle = BackgroundStyleKind.CustomImage;
            await _services.SettingsStore.SaveAsync(settings);

            BackgroundStyleCombo.SelectedIndex = 3;
            BackgroundImagePathText.Text = target;
            ApplyBackgroundStyle(settings);
            ShowMessage("自定义背景已设置。", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowMessage($"设置背景失败：{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void OnSaveSettingsClicked(object sender, RoutedEventArgs e)
    {
        var settings = await _services.SettingsStore.LoadAsync();

        settings.SourceLanguage = SourceLanguageCombo.SelectedValue?.ToString() ?? "auto";
        settings.TargetLanguage = TargetLanguageCombo.SelectedValue?.ToString() ?? DefaultTargetLanguageCode;
        settings.UiFontSize = Math.Clamp(UiFontSizeSlider.Value, MinUiFontSize, MaxUiFontSize);
        settings.Hotkey = string.IsNullOrWhiteSpace(HotkeyTextBox.Text) ? DefaultHotkey : HotkeyTextBox.Text.Trim();
        settings.HidePopupBeforeCapture = HidePopupToggle.IsOn;

        settings.BaiduTranslateAppId = BaiduAppIdTextBox.Text.Trim();
        settings.BaiduTranslateKey = BaiduKeyTextBox.Password.Trim();
        settings.BaiduOcrApiKey = BaiduOcrApiKeyTextBox.Text.Trim();
        settings.BaiduOcrSecretKey = BaiduOcrSecretKeyTextBox.Password.Trim();

        settings.BackgroundStyle = (BackgroundStyleKind)Math.Clamp(BackgroundStyleCombo.SelectedIndex, 0, 3);

        await _services.SettingsStore.SaveAsync(settings);
        ApplyUiFontSize(settings.UiFontSize);
        ApplyBackgroundStyle(settings);
        await RegisterGlobalHotkeyAsync();

        ShowMessage("设置已保存。", InfoBarSeverity.Success);
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _services.SettingsStore.LoadAsync();

        SourceLanguageCombo.SelectedValue = LanguageCatalog.FindOrDefault(settings.SourceLanguage, false).Code;
        TargetLanguageCombo.SelectedValue = LanguageCatalog.FindOrDefault(settings.TargetLanguage, true).Code;
        SourceLanguageImageCombo.SelectedValue = LanguageCatalog.FindOrDefault(settings.SourceLanguage, false).Code;
        TargetLanguageImageCombo.SelectedValue = LanguageCatalog.FindOrDefault(settings.TargetLanguage, true).Code;

        HotkeyTextBox.Text = settings.Hotkey;
        HidePopupToggle.IsOn = settings.HidePopupBeforeCapture;

        BaiduAppIdTextBox.Text = settings.BaiduTranslateAppId;
        BaiduKeyTextBox.Password = settings.BaiduTranslateKey;
        BaiduOcrApiKeyTextBox.Text = settings.BaiduOcrApiKey;
        BaiduOcrSecretKeyTextBox.Password = settings.BaiduOcrSecretKey;

        BackgroundStyleCombo.SelectedIndex = (int)settings.BackgroundStyle;
        BackgroundImagePathText.Text = string.IsNullOrWhiteSpace(settings.CustomBackgroundImagePath)
            ? "未设置自定义背景"
            : settings.CustomBackgroundImagePath;

        _suppressFontSizeEvent = true;
        UiFontSizeSlider.Value = Math.Clamp(settings.UiFontSize, MinUiFontSize, MaxUiFontSize);
        _suppressFontSizeEvent = false;

        ApplyUiFontSize(UiFontSizeSlider.Value);
        ApplyBackgroundStyle(settings);
    }

    private void OnUiFontSizeSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressFontSizeEvent)
        {
            return;
        }

        ApplyUiFontSize(e.NewValue);
    }

    private void ApplyUiFontSize(double fontSize)
    {
        var normalized = Math.Clamp(fontSize, MinUiFontSize, MaxUiFontSize);
        MainTabView.FontSize = normalized;
        SourceTextBox.FontSize = normalized;
        ResultTextBox.FontSize = normalized;
        ImageOcrTextBox.FontSize = normalized;
        ImageResultTextBox.FontSize = normalized;
        HistoryListView.FontSize = normalized;
        UiFontSizeValueText.Text = $"{normalized:0}";

        if (Math.Abs(UiFontSizeSlider.Value - normalized) > 0.01)
        {
            _suppressFontSizeEvent = true;
            UiFontSizeSlider.Value = normalized;
            _suppressFontSizeEvent = false;
        }
    }

    private void OnMainTabViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyImageLayout(e.NewSize.Width);
    }

    private void ApplyImageLayout(double width)
    {
        if (width >= WideLayoutThreshold)
        {
            ImageLayoutColumnLeft.Width = new GridLength(1.2, GridUnitType.Star);
            ImageLayoutColumnRight.Width = new GridLength(1, GridUnitType.Star);
            ImageLayoutRowTop.Height = new GridLength(1, GridUnitType.Star);
            ImageLayoutRowBottom.Height = new GridLength(0);
            Grid.SetColumn(ImagePreviewPanel, 0);
            Grid.SetRow(ImagePreviewPanel, 0);
            Grid.SetColumn(ImageResultPanel, 1);
            Grid.SetRow(ImageResultPanel, 0);
            return;
        }

        ImageLayoutColumnLeft.Width = new GridLength(1, GridUnitType.Star);
        ImageLayoutColumnRight.Width = new GridLength(0);
        ImageLayoutRowTop.Height = new GridLength(1, GridUnitType.Star);
        ImageLayoutRowBottom.Height = new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(ImagePreviewPanel, 0);
        Grid.SetRow(ImagePreviewPanel, 0);
        Grid.SetColumn(ImageResultPanel, 0);
        Grid.SetRow(ImageResultPanel, 1);
    }

    private void ApplyBackgroundStyle(AppSettings settings)
    {
        switch (settings.BackgroundStyle)
        {
            case BackgroundStyleKind.Gradient:
                PageRoot.Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Windows.UI.Color.FromArgb(255, 236, 245, 255), Offset = 0 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(255, 245, 238, 255), Offset = 0.6 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(255, 232, 255, 244), Offset = 1 }
                    }
                };
                ContentOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(118, 255, 255, 255));
                break;

            case BackgroundStyleKind.Frosted:
                PageRoot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 246, 250));
                ContentOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(170, 255, 255, 255));
                break;

            case BackgroundStyleKind.Illustration:
                PageRoot.Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 235, 219), Offset = 0 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 248, 210), Offset = 0.5 },
                        new GradientStop { Color = Windows.UI.Color.FromArgb(255, 214, 240, 255), Offset = 1 }
                    }
                };
                ContentOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(130, 255, 255, 255));
                break;

            case BackgroundStyleKind.CustomImage:
                if (!string.IsNullOrWhiteSpace(settings.CustomBackgroundImagePath) && File.Exists(settings.CustomBackgroundImagePath))
                {
                    var image = new BitmapImage(new Uri(settings.CustomBackgroundImagePath));
                    PageRoot.Background = new ImageBrush
                    {
                        ImageSource = image,
                        Stretch = Stretch.UniformToFill
                    };
                    ContentOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(145, 255, 255, 255));
                }
                else
                {
                    PageRoot.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 245, 245));
                    ContentOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(130, 255, 255, 255));
                }

                break;
        }
    }

    private async Task CleanupAndRefreshHistoryAsync()
    {
        var settings = await _services.SettingsStore.LoadAsync();
        var cutoff = DateTimeOffset.Now.AddDays(-Math.Max(settings.HistoryRetentionDays, 1));
        await _services.HistoryRepository.DeleteOlderThanAsync(cutoff);
        await RefreshHistoryAsync();
    }

    private async Task RefreshHistoryAsync()
    {
        var items = await _services.HistoryRepository.QueryAsync(new HistoryQuery(300));
        _history.Clear();
        foreach (var item in items)
        {
            _history.Add(item);
        }
    }

    private async Task RegisterGlobalHotkeyAsync()
    {
        _services.GlobalHotkeyService.HotkeyPressed -= OnGlobalHotkeyPressed;

        var settings = await _services.SettingsStore.LoadAsync();
        if (_services.GlobalHotkeyService.Register(App.Current.MainWindow, settings.Hotkey))
        {
            _services.GlobalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        }
        else
        {
            ShowMessage("全局快捷键注册失败，请检查是否冲突。", InfoBarSeverity.Warning);
        }
    }

    private static bool IsSupportedOcrImage(StorageFile file)
    {
        var ext = Path.GetExtension(file.Path);
        return !string.IsNullOrWhiteSpace(ext) && SupportedOcrImageExtensions.Contains(ext);
    }

    private static async Task<byte[]> LoadImageBytesAsync(StorageFile file)
    {
        await using var stream = await file.OpenStreamForReadAsync();
        if (stream.Length <= 0)
        {
            throw new InvalidOperationException("图片文件为空。");
        }

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private FileOpenPicker CreateImagePicker()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".tif");
        picker.FileTypeFilter.Add(".tiff");
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.Current.MainWindow));
        return picker;
    }

    private static void CopyToClipboard(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
    }

    private static async Task<BitmapImage> CreateBitmapImageAsync(byte[] bytes)
    {
        var image = new BitmapImage();
        await using var stream = new MemoryStream(bytes);
        using var random = stream.AsRandomAccessStream();
        await image.SetSourceAsync(random);
        return image;
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}
