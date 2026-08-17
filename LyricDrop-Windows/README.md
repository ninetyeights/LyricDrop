# LyricDrop for Windows

LyricDrop 的 Windows 版本，使用 .NET 10 + WPF 实现。功能与 macOS 版对齐：托盘常驻、本地音频 + LRC、桌面悬浮歌词、多种歌词配色主题。

## 系统要求

- Windows 10 1903 或更新版本
- [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)（运行）
- .NET 10 SDK 或更新（构建源码）

## 构建

```pwsh
cd LyricDrop-Windows
dotnet restore
dotnet build -c Release
```

可执行文件位于 `bin/Release/net10.0-windows10.0.19041.0/LyricDrop.exe`。

发布单文件版（包含运行时，可直接分发）：

```pwsh
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 使用

启动后会在系统托盘看到一个 ♪ 图标：

- **左键**：弹出/收起播放器面板
- **右键**：打开桌面歌词、设置、退出

播放器面板里：

- 拖拽音频文件或 `.lrc` 到面板上即可加载（同名 `.lrc` 自动加载）
- URL 框支持直接 HTTP 音频地址或包含播放信息的网页
- 倍速、音量、循环、桌面歌词均可一键切换
- 歌词列表双击任意行跳转到该时间点

桌面歌词窗口：

- 鼠标悬停时显示半透明背景和工具栏（设置、锁定、关闭）
- 拖动可移动；锁定后窗口会"穿透"鼠标点击，从托盘菜单或设置取消锁定

## 第三方依赖

- `WPF-UI`（MIT）— Win11 Fluent 控件主题和 `FluentWindow`（提供 Mica 背景 + 圆角）
- `H.NotifyIcon.Wpf`（MIT）— 系统托盘图标
- `System.Text.Encoding.CodePages`（MIT）— LRC 文件 GB18030 解码回退

## 与 macOS 版本的差异

- 用 WPF 的 `MediaPlayer` 替代了 AVAudioPlayer，编解码能力依赖 Windows Media Foundation。MP3/WAV/AIFF/M4A/AAC/WMA/FLAC 在 Win10+ 默认可用；OGG 需另装编解码扩展。
- 托盘提示文字最多 127 字符（Windows API 限制），过长歌词会被截断。
- "桌面悬浮歌词锁定"使用 `WS_EX_TRANSPARENT` 实现点击穿透；解锁需通过托盘菜单或设置面板。
- 媒体快捷键、登录启动暂未实现。
