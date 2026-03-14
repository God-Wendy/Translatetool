# TranslateTool

Windows 桌面翻译软件（WinUI 3, Unpackaged）。

## 功能（v1.0）
- 文字翻译：Enter 翻译，Shift+Enter 换行，语言互换。
- 图片与截图翻译：图片上传 + 全屏框选截图，统一 OCR + 翻译链路。
- 历史：多选批量复制，单条文本可局部选择复制。
- 设置：百度翻译/OCR 密钥、全局字号、截图选项、背景主题与自定义背景。

## 本地运行
```powershell
$env:DOTNET_CLI_HOME="C:\Users\29256\Desktop\Project\Codex\project1\.dotnet_home"
dotnet build .\src\TranslateTool\TranslateTool.csproj --configfile .\NuGet.Config
dotnet run --project .\src\TranslateTool\TranslateTool.csproj --configfile .\NuGet.Config
```

## 打包（每版本目录只保留一个 zip）
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package.ps1 -Version v1.0 -Configuration Release -Runtime win-x64
```

产物：
- `versions/v1.0/TranslateTool-v1.0-win-x64.zip`
