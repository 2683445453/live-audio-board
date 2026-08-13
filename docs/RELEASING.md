# LiveAudioBoard 发布指南

本文定义 `0.22.x` 及后续 Windows 版本的标准发布流程。正式 Release 必须来自 `main` 上的
带注释 SemVer 标签。

## 1. 发布前提

- Windows 10/11 x64；
- `global.json` 指定的 .NET SDK 10.0.302；
- 对 `git@github.com:2683445453/live-audio-board.git` 的 SSH 推送权限；
- 干净且与 `origin/main` 同步的 `main`；
- 可选的 Windows 代码签名证书。

版本采用 `MAJOR.MINOR.PATCH`。不兼容变更增加 MAJOR，向后兼容功能增加 MINOR，修复增加
PATCH。预发布版本可以使用 `0.23.0-beta.1`。

## 2. 更新发行元数据

发布版本必须同时出现在：

- `src/LiveAudioBoard.App/LiveAudioBoard.App.csproj` 的 `<Version>`；
- `scripts/build-release.ps1` 的默认版本；
- `.github/workflows/release.yml` 的手动触发默认版本；
- `CHANGELOG.md` 的正式版本标题；
- README 中的示例命令（如适用）。

许可证、`Required Notice`、第三方声明和商业授权说明必须随发布包分发。

## 3. 本地验收

```powershell
./scripts/verify-release-metadata.ps1 -Version 0.22.1
dotnet restore LiveAudioBoard.sln
dotnet format LiveAudioBoard.sln --verify-no-changes --no-restore
dotnet build LiveAudioBoard.sln --configuration Release --no-restore
dotnet test LiveAudioBoard.sln --configuration Release --no-build --no-restore
dotnet list LiveAudioBoard.sln package --vulnerable --include-transitive
./scripts/build-release.ps1 -Version 0.22.1
```

检查 `artifacts/release-local/releases` 中至少存在：

- `LiveAudioBoard-win-Setup.exe`；
- `LiveAudioBoard-win.msi`；
- `LiveAudioBoard-0.22.1-full.nupkg`；
- `LiveAudioBoard-0.22.1-win-x64-portable.zip`；
- `SHA256SUMS.txt`。

随后从 `artifacts/release-local/publish/win-x64/LiveAudioBoard.exe` 做一次启动烟雾测试，并确认
文件版本、主窗口、资料库启动和下载中心正常。

## 4. 代码签名

GitHub 仓库 Secrets 可配置：

- `WINDOWS_SIGN_PFX_BASE64`：PFX 文件的 Base64 内容；
- `WINDOWS_SIGN_PFX_PASSWORD`：证书密码。

两个 Secret 必须同时存在。流水线使用 SHA-256 和 DigiCert 时间戳服务。证书、密码、解码后
的 PFX 和签名命令输出不得提交到 Git。未配置证书时仍会生成包，但 SmartScreen 可能提示
未知发布者。

## 5. 合并和打标签

```powershell
git switch main
git pull --ff-only origin main
git merge --ff-only <validated-release-branch>
git push origin main
git tag -a v0.22.1 -m "LiveAudioBoard 0.22.1"
git push origin v0.22.1
```

禁止给不属于 `main` 的提交打正式标签。标签触发 `.github/workflows/release.yml`，流水线会再次
验证版本、测试、构建、计算 SHA-256 并创建 GitHub Release。

## 6. GitHub Release 验收

- Windows Release workflow 为绿色；
- Release 标题和标签版本一致；
- Setup、MSI、便携包、Velopack 文件与 `SHA256SUMS.txt` 均已上传；
- 下载一个产物并核对 SHA-256；
- 安装版和便携版至少各做一次启动测试；
- README 的“最新版”链接能打开新 Release。

## 7. 回滚与修复

不要重写或复用已经公开的版本标签。若发布包存在问题：

1. 在 GitHub Release 中标记问题并暂停推广；
2. 从 `main` 创建 `fix/<description>`；
3. 修复、测试并增加 PATCH 版本；
4. 发布新的标签，例如 `v0.22.1`；
5. 必要时在旧 Release 说明中链接到替代版本。

用户资料库位于 `%LOCALAPPDATA%\LiveAudioBoard`，回滚安装程序不得删除该目录。
