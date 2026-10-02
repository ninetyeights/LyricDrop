# LyricDrop 1.0.2

安全加固版本。

## 安全改进

- 启用 macOS App Sandbox，仅授予用户选择文件的只读权限和出站网络权限
- 使用 security-scoped bookmark 安全恢复上次选择的音频和歌词文件
- 远程媒体仅允许 HTTPS，并限制 localhost、私网地址和不安全重定向
- 限制网页、歌词和音频下载大小，降低资源耗尽风险
- 避免在日志和 UserDefaults 中保存带凭据或签名参数的 URL
- 修复 GitHub Actions 手动发布参数的命令注入风险
- GitHub Actions 升级并固定到不可变提交 SHA

## 安装

下载 `LyricDrop-1.0.2.dmg`，打开后把 `LyricDrop.app` 拖到 `Applications` 文件夹。

首次启动若提示"无法打开"，在 **系统设置 → 隐私与安全性** 中点"仍要打开"，或终端执行：

```sh
xattr -cr /Applications/LyricDrop.app
```

## 系统要求

macOS 15.7 或更新版本（Intel 和 Apple Silicon 均支持）。

## Build Provenance

本次 DMG 由 GitHub Actions 构建并自动签发 build provenance attestation。可通过以下命令验证：

```sh
gh attestation verify LyricDrop-1.0.2.dmg --repo secure-artifacts/LyricDrop
```
