# LiveAudioBoard

面向 Windows 直播场景的本地音频资料库与快捷播放面板。

当前里程碑已经包含：

- WPF/.NET 8 分层项目骨架；
- 夜航玻璃拟态主界面；
- 本地音频导入与 SQLite 持久化；
- 音频自动复制到托管媒体库，并按 SHA-256 内容去重；
- SQLite 每 24 小时自动备份，默认保留最近 10 份；
- 分类、搜索、收藏筛选；
- 基于 NAudio/WASAPI 的 48 kHz 双声道多路混音；
- 单曲循环、独占播放、0–2000 ms 淡入淡出与实时播放进度；
- 毫秒级非破坏性起止点，循环严格限制在选定区间；
- EBU R128 风格离线 LUFS 分析、样本峰值与安全建议增益；
- 用户可选建议增益、单曲软峰值保护和 0–5000 ms 播放冷却；
- Windows 输出设备选择、刷新与设置持久化；
- 全局紧急停止热键 `Ctrl+Shift+F10`；
- 单条音频全局快捷键录入、冲突检测、总览与临时停用；
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

首次运行会在 `%LOCALAPPDATA%\LiveAudioBoard` 创建 `library.db`。新导入和下载成功的音频
会进入 `Media`，数据库备份进入 `Backups`，播放偏好保存在同目录的 `settings.json`。
旧版本已经记录的外部文件路径保持不变，不会在启动时擅自移动。
用户音频、数据库、缓存与密钥不会进入 Git 仓库。

下载中心通过 [Openverse 官方 API](https://api.openverse.org/) 聚合开放授权音频，默认只显示 CC0、公共领域
和 CC BY 内容；使用前仍应在“查看来源”中核对具体授权和署名要求。直链模式
只处理可直接访问的合法音频文件地址，不解析网页、不绕过登录或 DRM，也不抓取
流媒体平台内容。`Downloads` 只作为下载暂存区；导入成功后文件会转入内容寻址的
`Media` 目录，完全相同的音频只保存一份。

在线试听使用当前选择的 Windows 输出设备；直播期间如果 OBS 正在捕获本应用音频，
试听声也可能进入直播，请先确认监听和采集状态。

音效卡右上角的齿轮可打开播放设置。“独占”会在播放前停止其他音效；循环音效
再次点击播放按钮或触发同一个全局快捷键即可停止。淡出在自然结束和每次循环边界
生效，全局紧急停止始终立即执行。

播放设置中的两个区间滑块用于跳过片头、空白尾部或截取音效片段，原文件不会被
重编码。响度分析针对整段文件，结果保存到 SQLite；建议增益以 `-16 LUFS` 为目标并
保留 `-1 dBFS` 峰值余量。建议增益必须由用户在播放设置中主动启用；单曲软峰值
保护默认开启，在音效进入多路混音器前约束峰值。播放冷却可阻止连按快捷键造成叠音，
但不会拦截“停止循环”。

## 文档

- [开发路线与功能建议](docs/DEVELOPMENT_ROADMAP.md)
- [玻璃拟态界面规范原文](docs/UI_STYLE_REFERENCE.md)
- [Soundpad 功能参考与项目取舍](docs/SOUNDPAD_REFERENCE.md)
