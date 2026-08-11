# LiveAudioBoard 开发路线与功能建议

## 产品目标

LiveAudioBoard 是一款仅面向 Windows 的直播音频面板。核心使用路径是：导入或合法下载音频、分类整理、收藏或绑定热键，并稳定输出到 OBS、直播软件或指定音频设备。

## 技术基线

| 层 | 技术 | 约束 |
|---|---|---|
| 桌面界面 | C#、WPF、MVVM Toolkit | 当前使用 `net8.0-windows`；发布前升级 .NET 10 LTS |
| 音频 | NAudio 2 稳定版、WASAPI 共享模式 | 固定 48 kHz 双声道混音，支持选择一个 Windows 输出端点 |
| 数据 | SQLite、EF Core | 分类是逻辑关系；磁盘只保存一份媒体文件 |
| 下载 | `IDownloadProvider` 适配器 | 只接合法直链、RSS 与授权 API，不绕过 DRM |
| 发布 | Git、GitHub Actions、GitHub Releases、Velopack | 源码与用户媒体分离，标签使用 SemVer |

## 分层边界

- `LiveAudioBoard.App`：WPF 视图、ViewModel、Windows 文件选择和窗口行为。
- `LiveAudioBoard.Core`：领域模型及播放、资料库、下载源接口，不依赖 WPF。
- `LiveAudioBoard.Audio`：NAudio 解码、WASAPI 播放、设备枚举与后续混音。
- `LiveAudioBoard.Infrastructure`：SQLite、文件资料库、设置、备份和日志。
- `LiveAudioBoard.Providers`：直链、RSS、Freesound 等下载适配器。
- `LiveAudioBoard.Tests`：领域规则、筛选、存储与播放状态测试。

## 资料库存储

默认根目录为 `%LOCALAPPDATA%\LiveAudioBoard`：

```text
LiveAudioBoard/
├─ Media/       SHA-256 内容寻址的正式媒体文件
├─ Downloads/   下载中的 .part 临时文件
├─ Covers/      封面与波形缓存
├─ Backups/     数据库备份
├─ library.db   分类、收藏、标签与来源
└─ settings.json 输出设备与全局热键偏好
```

从 MVP 0.5 起，新导入与下载文件会复制到正式媒体库并按 SHA-256 去重；旧版本记录的
外部路径保持原状，后续通过显式迁移工具处理。数据库每 24 小时创建一致性快照，默认
保留最近 10 份。

## 功能优先级

### P0：可用于第一次直播验证

- 音频导入、播放、停止全部和基础错误反馈。
- 分类、搜索、收藏和 SQLite 持久化。
- 输出设备选择、设备失效恢复。
- 音效宫格、播放状态和 OBS 应用音频捕获说明。
- 全局紧急停止热键。

### P1：直播可靠性

- 独占播放模式、淡入淡出和循环。（已完成首版）
- 全局热键编辑、冲突检测和防重复触发。
- 非破坏性起止点与播放冷却时间（已完成）；单曲音量编辑。
- 文件复制、SHA-256 去重、资料库备份和恢复。
- 设备热插拔、崩溃日志与长时间播放压力测试。

### P2：内容来源与发布

- RSS 下载队列与 HTTP 断点续传；Openverse 搜索、直链下载、进度、取消、重试和 `.part` 文件已完成。
- Freesound OAuth2 适配器并保存作者、来源和许可证。
- Internet Archive 等授权来源；平台适配器与播放核心隔离。
- Velopack 安装和自动更新；GitHub 标签触发 Windows Release。

## 直播音频路由

首选让 OBS 使用“应用程序音频捕获”直接捕获 LiveAudioBoard，同时软件输出到主播监听设备。只接受麦克风输入的平台，使用 VB-CABLE 或 VoiceMeeter 等虚拟设备；自研虚拟声卡驱动不进入 MVP。

## 界面规范落地

- 背景固定为深墨夜景，使用月光蓝、深海蓝和少量香槟金光井。
- 面板保持 5%–12% 中性白透明度，24px 大圆角和受光顶边。
- 香槟金 `#E4B863` 仅用于主要动作、播放状态与焦点提示。
- 禁用紫粉渐变、实心白/黑面板、快速过渡和小圆角。
- WPF 没有网页 `backdrop-filter` 的等价实现；首版用多层半透明表面、方向高光、背景光井和颗粒层还原材质，后续评估 Windows Composition Acrylic。
- 桌面首发最小窗口为 1080×640；键盘焦点、文本对比度和减少动画模式纳入验收。

完整设计约束保存在 [UI_STYLE_REFERENCE.md](UI_STYLE_REFERENCE.md)。

## Git 与发布规则

- `main` 保持可构建；功能分支命名为 `feat/<name>`，修复使用 `fix/<name>`。
- 提交采用 `feat:`、`fix:`、`docs:`、`test:`、`chore:` 前缀。
- 每个可验证的小里程碑提交一次；版本使用 `v0.1.0`、`v0.2.0`、`v1.0.0`。
- 不提交用户媒体、SQLite 数据库、下载缓存、日志、API 密钥和签名证书。
- Push/PR 自动执行 restore、build、test；`v*` 标签构建 `win-x64` 安装包并上传 GitHub Release。

## 当前迭代

Soundpad 类工作流的借鉴范围与实现次序见
[SOUNDPAD_REFERENCE.md](SOUNDPAD_REFERENCE.md)。单条音频全局快捷键、资料库文件托管、
SHA-256 去重、自动备份以及非破坏性起止点/循环/独占/淡入淡出已经完成；离线 LUFS
分析、建议增益的可选应用、单曲软峰值保护、播放冷却时间以及最终主混音总线限幅与
实时电平反馈也已落地。下一阶段优先实现响度批量分析和文件缺失恢复。

- [x] 技术路线与界面规范存档。
- [x] 解决方案、分层项目、测试项目和 Git 仓库。
- [x] 首版玻璃拟态主界面。
- [x] 音频导入、SQLite 记录、搜索、收藏、播放和停止。
- [x] 输出设备选择、刷新、偏好持久化与多音效混音。
- [x] 全局 `Ctrl+Shift+F10` 紧急停止、冲突反馈与防重复触发。
- [x] 单条音频全局快捷键、冲突检测、快捷键总览与运行期总开关。
- [x] 资料库复制、SHA-256 去重与自动备份（24 小时间隔，保留 10 份）。
- [x] 单曲循环、独占播放、淡入淡出与实时播放进度。
- [x] 毫秒级非破坏性起止点与选定区间循环。
- [x] EBU R128 风格 LUFS、样本峰值与建议增益离线分析。
- [x] 可选建议增益、单曲软峰值保护与播放冷却门控。
- [x] 所有声音叠加后的主总线 `-1 dBFS` 限幅、实时峰值与增益衰减反馈。
- [x] Openverse 站内搜索：Freesound、Jamendo、Wikimedia Commons。
- [x] 开放音频搜索结果分页、在线试听与单独停止试听。
- [x] HTTP/HTTPS 下载、来源/许可证记录和完成后自动导入。
- [ ] RSS 与 Freesound OAuth2 原始高质量文件下载。
- [x] GitHub 远程仓库与首个可运行版本推送。
- [ ] GitHub Actions 与 Windows 安装包。
