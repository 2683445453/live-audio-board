<p align="center">
  <img src="assets/branding/app-icon.png" width="128" alt="LiveAudioBoard 图标" />
</p>

<h1 align="center">LiveAudioBoard</h1>

<p align="center">面向 Windows 直播场景的本地音频资料库、快捷播放面板与双总线调音工具。</p>

<p align="center">
  <a href="https://github.com/2683445453/live-audio-board/actions/workflows/ci.yml"><img src="https://github.com/2683445453/live-audio-board/actions/workflows/ci.yml/badge.svg?branch=main" alt="Windows CI" /></a>
  <a href="https://github.com/2683445453/live-audio-board/releases/latest"><img src="https://img.shields.io/github/v/release/2683445453/live-audio-board?display_name=tag" alt="GitHub Release" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-2d7dff" alt="Windows 10/11" />
  <img src="https://img.shields.io/badge/.NET-10.0-512bd4" alt=".NET 10" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-PolyForm%20Noncommercial%201.0.0-d6a84b" alt="PolyForm Noncommercial 1.0.0" /></a>
</p>

<p align="center">
  简体中文 · <a href="README.en.md">English</a> ·
  <a href="https://github.com/2683445453/live-audio-board/releases/latest">下载最新版</a> ·
  <a href="docs/USER_GUIDE.md">使用指南</a> ·
  <a href="CHANGELOG.md">更新记录</a>
</p>

> [!IMPORTANT]
> 本项目允许个人和非商业使用、修改及再分发，但禁止未经授权的商业使用。它属于
> **源码可用软件**，不是 OSI 认证的开源软件。商业直播、带货、付费服务、企业用途或
> 转售请先阅读[商业授权说明](COMMERCIAL_LICENSE.md)。

## 功能概览

| 模块 | 能力 |
| --- | --- |
| 资料库 | 文件/文件夹导入、递归扫描、拖放分类、收藏、搜索、分页和稳定排序 |
| 播放 | 多音效混音、循环、独占、淡入淡出、起止点、播放冷却和全局快捷键 |
| 直播路由 | 独立“直播输出”和“主播监听”WASAPI 总线，同设备自动去重 |
| 音量安全 | EBU R128 风格 LUFS 分析、建议增益、单曲峰值保护和 `-1 dBFS` 主总线限幅 |
| 录制与编辑 | 麦克风/系统回环录音、静音裁剪、WAV/MP3/M4A 非破坏性编辑导出 |
| 下载中心 | Openverse、Internet Archive、RSS/Atom、Freesound OAuth2 和合法音频直链 |
| 下载可靠性 | 最多 3 路后台下载、单项取消、ETag/Last-Modified 断点续传和 SHA-256 去重 |
| 数据安全 | SQLite 持久化、内容寻址媒体库、自动备份、文件找回和轮换崩溃日志 |
| 更新发布 | .NET 10 自包含 `win-x64`、Velopack Setup/MSI/便携包和应用内更新 |

完整功能与实现状态见[开发路线图](docs/DEVELOPMENT_ROADMAP.md)。

## 下载与安装

前往 [GitHub Releases](https://github.com/2683445453/live-audio-board/releases/latest)：

- `LiveAudioBoard-win-Setup.exe`：推荐，当前用户安装并支持应用内更新；
- `LiveAudioBoard-win.msi`：适合标准 Windows 部署流程；
- `LiveAudioBoard-*-win-x64-portable.zip`：解压即用，不执行安装。

正式包为 Windows x64 自包含应用，不需要另行安装 .NET Desktop Runtime。未使用商业代码
签名证书的版本可能触发 Windows SmartScreen“未知发布者”提示，请从本仓库 Release 下载并
使用同一页面提供的 `SHA256SUMS.txt` 校验文件完整性。

## 快速开始

1. 启动后通过“导入音频”或拖放文件/文件夹建立资料库。
2. 在右侧分别选择“直播输出”和“主播监听”设备。
3. 将直播输出设为 OBS 捕获的虚拟声卡，将监听输出设为耳机。
4. 点击音效卡播放，或在播放设置中录入全局快捷键。
5. 使用 `Ctrl+Shift+F10` 可在任何时候紧急停止全部播放。

严格隔离直播与监听时，建议使用 VB-CABLE 或 VoiceMeeter，并让 OBS 捕获虚拟设备。
“应用程序音频捕获”可能同时采集本进程的监听总线。详细配置、录音、编辑和下载流程见
[使用指南](docs/USER_GUIDE.md)。

## 开放音频来源

- Openverse：聚合 Freesound、Jamendo 和 Wikimedia Commons；
- Internet Archive：仅显示明确标注 CC0、公共领域或 CC BY 且格式可处理的条目；
- Freesound：通过 OAuth2 下载上传者提供的原始文件；
- RSS/Atom：读取公开 Feed 中的音频附件；
- 直链：只接受合法、可直接访问的 HTTP/HTTPS 音频文件。

软件不会解析下载网页、绕过登录或 DRM，也不会抓取流媒体平台。来源元数据只是筛选依据，
使用者仍须核对音频作者、许可证、署名和商业使用条件。软件许可不授予任何第三方音频权利。

## 本地数据与隐私

运行数据默认保存在 `%LOCALAPPDATA%\LiveAudioBoard`：

- `library.db`：资料库数据库；
- `Media`、`Recordings`、`Renders`：托管音频和生成文件；
- `Downloads`：下载与断点续传暂存；
- `Backups`：数据库自动备份；
- `settings.json`：播放与设备偏好；
- `freesound.auth`：由 Windows 当前用户 DPAPI 加密的 Freesound 凭据；
- `Logs`：本地崩溃诊断日志，默认仅保留最近 20 份。

这些文件不会自动上传，也不会进入 Git 仓库。应用更新只替换安装目录中的程序文件。

## 从源码运行

要求 Windows 10/11 x64 与仓库 `global.json` 指定的 .NET SDK 10.0.302。

```powershell
dotnet restore LiveAudioBoard.sln
dotnet build LiveAudioBoard.sln --configuration Release --no-restore
dotnet test LiveAudioBoard.sln --configuration Release --no-build --no-restore
dotnet run --project src/LiveAudioBoard.App/LiveAudioBoard.App.csproj
```

## 构建发行包

```powershell
./scripts/verify-release-metadata.ps1 -Version 0.22.2
./scripts/build-release.ps1 -Version 0.22.2
```

产物生成到 `artifacts/release-local/releases`。版本标签 `v0.22.2` 会触发 GitHub Actions，
重新测试并创建 GitHub Release。完整步骤、签名密钥和回滚流程见
[发布指南](docs/RELEASING.md)。

## 项目文档

- [使用指南](docs/USER_GUIDE.md)
- [开发路线与架构](docs/DEVELOPMENT_ROADMAP.md)
- [发布指南](docs/RELEASING.md)
- [更新记录](CHANGELOG.md)
- [贡献说明](CONTRIBUTING.md)
- [安全策略](SECURITY.md)
- [玻璃拟态界面规范](docs/UI_STYLE_REFERENCE.md)
- [Soundpad 功能参考与项目取舍](docs/SOUNDPAD_REFERENCE.md)
- [第三方组件声明](THIRD_PARTY_NOTICES.md)

## 许可证

LiveAudioBoard 采用 [PolyForm Noncommercial License 1.0.0](LICENSE)。个人和符合条款的
非商业用途可以使用、研究、修改与再分发；商业使用必须取得单独授权。该许可证不是 OSI
认证的开源许可证。详见[商业授权说明](COMMERCIAL_LICENSE.md)。

Required Notice: Copyright (c) 2026 2683445453.
