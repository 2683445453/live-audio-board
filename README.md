# LiveAudioBoard

面向 Windows 直播场景的本地音频资料库与快捷播放面板。

当前里程碑已经包含：

- WPF/.NET 8 分层项目骨架；
- 夜航玻璃拟态主界面；
- 本地音频导入与 SQLite 持久化；
- 分类、搜索、收藏筛选；
- 基于 NAudio/WASAPI 的 48 kHz 双声道多路混音；
- Windows 输出设备选择、刷新与设置持久化；
- 全局紧急停止热键 `Ctrl+Shift+F10`；
- 站内搜索 Freesound、Jamendo、Wikimedia Commons 的开放音频；
- 搜索结果分页与下载前在线试听；
- HTTP/HTTPS 音频下载、进度、取消、重试、许可证记录与自动导入；
- xUnit 核心模型、声道转换和设置存储测试。

> 当前开发机只有 .NET 8 SDK，因此先保证项目可构建。稳定发布前按
> [开发路线](docs/DEVELOPMENT_ROADMAP.md)升级到 .NET 10 LTS。

## 运行

```powershell
dotnet restore
dotnet build LiveAudioBoard.sln
dotnet run --project src/LiveAudioBoard.App/LiveAudioBoard.App.csproj
```

首次运行会在 `%LOCALAPPDATA%\LiveAudioBoard` 创建 `library.db`，播放偏好保存在
同目录的 `settings.json`。
用户音频、数据库、缓存与密钥不会进入 Git 仓库。

下载中心通过 [Openverse 官方 API](https://api.openverse.org/) 聚合开放授权音频，默认只显示 CC0、公共领域
和 CC BY 内容；使用前仍应在“查看来源”中核对具体授权和署名要求。直链模式
只处理可直接访问的合法音频文件地址，不解析网页、不绕过登录或 DRM，也不抓取
流媒体平台内容。下载文件保存在
`%LOCALAPPDATA%\LiveAudioBoard\Downloads`。

在线试听使用当前选择的 Windows 输出设备；直播期间如果 OBS 正在捕获本应用音频，
试听声也可能进入直播，请先确认监听和采集状态。

## 文档

- [开发路线与功能建议](docs/DEVELOPMENT_ROADMAP.md)
- [玻璃拟态界面规范原文](docs/UI_STYLE_REFERENCE.md)
- [Soundpad 功能参考与项目取舍](docs/SOUNDPAD_REFERENCE.md)
