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

## 文档

- [开发路线与功能建议](docs/DEVELOPMENT_ROADMAP.md)
- [玻璃拟态界面规范原文](docs/UI_STYLE_REFERENCE.md)
