# LyricDrop Windows 安全审核报告

审核日期：2026-10-02

## 1. 项目概况与范围

- 应用：Windows 桌面歌词播放器，C# / XAML / WPF，目标框架 `.NET 10`。
- 包管理器：NuGet；审核前没有锁文件。
- 直接依赖：5 个（运行时 2 个、测试 3 个）。
- 关键间接依赖：21 个唯一包。
- 审核范围：`LyricDrop-Windows` 的全部受版本控制源码和配置、配套测试项目、仓库发布与 CodeQL 工作流，共 23 个受版本控制文件（11 个 C#、4 个 XAML、2 个工作流及其他项目/配置/文档文件）。构建输出目录只做密钥模式扫描，不作为源码逐文件审查对象。
- 核查方式：人工逐文件审查、危险 API/凭据模式扫描、`dotnet list package --vulnerable --include-transitive`、弃用与过期包检查、OSV 精确版本查询、GitHub Advisory/NVD WebSearch 交叉核对、Release 构建与测试。

## 2. 依赖清单

### 直接依赖（修复后）

| 项目 | 包 | 实际版本 | 备注 |
|---|---|---:|---|
| 应用 | H.NotifyIcon.Wpf | 2.4.1 | 从 2.2.0 小版本升级 |
| 应用 | WPF-UI | 4.3.0 | 未发现匹配的已知漏洞 |
| 测试 | Microsoft.NET.Test.Sdk | 18.10.1 | 已完成大版本迁移 |
| 测试 | xunit.v3.mtp-v2 | 4.0.1 | 已从 xUnit v2 迁移到 v3 / MTP v2 |
| 测试 | xunit.runner.visualstudio | 4.0.0 | 已完成大版本迁移 |

### 锁文件中的关键间接依赖

| 包 | 实际版本 |
|---|---:|
| H.GeneratedIcons.System.Drawing | 2.4.1 |
| H.NotifyIcon | 2.4.1 |
| WPF-UI.Abstractions | 4.3.0 |
| Microsoft.ApplicationInsights | 2.23.0 |
| Microsoft.Bcl.AsyncInterfaces | 6.0.0 |
| Microsoft.CodeCoverage | 18.10.1 |
| Microsoft.Testing.Extensions.Telemetry | 2.4.0 |
| Microsoft.Testing.Extensions.TrxReport.Abstractions | 2.4.0 |
| Microsoft.Testing.Platform / MSBuild | 2.4.0 |
| Microsoft.TestPlatform.ObjectModel / TestHost | 18.10.1 |
| Microsoft.Win32.Registry | 5.0.0 |
| System.Security.AccessControl | 6.0.1 |
| xunit.analyzers | 2.1.0 |
| xunit.v3.assert / common | 4.0.1 |
| xunit.v3.core.mtp-v2 | 4.0.1 |
| xunit.v3.extensibility.core | 4.0.1 |
| xunit.v3.runner.common / inproc.console | 4.0.1 |

NuGet 审计和 OSV 精确版本查询均未发现当前锁定版本的已知漏洞。WebSearch 中名为 “xUnit” 的 NVD 结果属于 Jenkins xUnit 插件或 Allure xunit-xml-plugin，并非本项目使用的 xUnit.net NuGet 包，已排除同名误报。没有发现疑似仿冒包；迁移后 NuGet 不再报告已弃用依赖或可用更新。

## 3. 问题汇总

| 编号 | 类别 | 严重级别 | 位置 | 状态 |
|---|---|---|---|---|
| LD-SEC-001 | 代码 | High | `ViewModels/LyricPlayer.cs:268` | 已修复 |
| LD-SEC-002 | 代码 | Medium | `ViewModels/LyricPlayer.cs:359` | 已修复 |
| LD-SEC-003 | CI/CD | High | `.github/workflows/release.yml:21`、`codeql.yml:23` 等 | 已修复 |
| LD-SEC-004 | CI/CD | Medium | `.github/workflows/release.yml:102` | 已修复 |
| LD-SEC-005 | 依赖 | Medium | 两个 `.csproj` 与新增 `packages.lock.json` | 已修复 |
| LD-SEC-006 | 代码 | Medium | `Services/LrcParser.cs:67`、`Services/AppSettings.cs:40` | 已修复 |
| LD-SEC-007 | 密钥 | Low | 根目录及 Windows 子项目 `.gitignore` | 已修复 |
| LD-SEC-008 | 依赖 | Low | `LyricDrop-Windows.Tests.csproj`、`global.json`、工作流 | 已修复 |
| LD-SEC-009 | 代码 | Low | `App.xaml.cs`、`ViewModels/LyricPlayer.cs` | 已修复 |

## 4. 已修复问题与具体改动

### LD-SEC-001：远程内容无大小限制，可导致磁盘/内存耗尽（High）

- 利用条件：用户打开恶意或异常 URL；服务器发送超大响应或不结束的响应流。
- 原风险：音频直接复制到临时文件，页面与 LRC 一次性读入内存，均无上限；可造成应用无响应、内存耗尽或磁盘占满。
- 修复：为音频、页面、LRC 分别设置 1 GiB、2 MiB、5 MiB 上限；同时检查 `Content-Length` 并在流式读取中再次计数，覆盖分块传输；HTTP 客户端增加两分钟超时；失败时清理半成品临时文件。
- 额外修复：只接受 HTTP/HTTPS；网页解析出的音频和歌词地址再次校验协议；临时文件扩展名只允许受支持的音频后缀，避免直接采用攻击者控制的任意后缀。

### LD-SEC-003：GitHub Action 使用可变标签（High）

- 利用条件：Action 上游标签被移动，或上游仓库/发布权限遭入侵。
- 原风险：`@v4`、`@v2` 会随上游标签变化执行不同代码，发布任务持有 `contents:write` 或 attestation 权限。
- 修复：所有第三方 Action 固定到从各官方仓库标签解析出的完整提交 SHA，并保留版本注释便于维护。

### LD-SEC-004：工作流表达式直接插入 PowerShell（Medium）

- 利用条件：有权限创建特殊名称标签的仓库协作者触发 Release 工作流。
- 原风险：`${{ github.ref_name }}` 在脚本执行前做文本替换，存在脚本注入面。
- 修复：通过 `env: RELEASE_TAG` 传递，PowerShell 只读取 `$env:RELEASE_TAG`。

### LD-SEC-005：依赖解析不可复现（Medium）

- 原风险：缺少锁文件使间接依赖可在不同时间解析成不同版本，增加供应链漂移风险。
- 修复：两个项目启用 `RestorePackagesWithLockFile`、`NuGetAudit` 和 `NuGetAuditMode=all`，提交各自 `packages.lock.json`；发布流水线使用 `--locked-mode`。运行时标识固定为 `win-x64`，避免普通恢复与发布恢复生成不一致锁图。

### LD-SEC-006：本地配置/LRC 可无界读入内存（Medium）

- 利用条件：用户打开超大 LRC，或同权限进程篡改设置文件。
- 修复：LRC 限制为 5 MiB；设置文件限制为 1 MiB，超限时安全回退到默认设置；新增超大 LRC 拒绝测试。

### LD-SEC-007：敏感文件缺少忽略规则（Low）

- 修复：根目录与 Windows 子项目忽略 `.env`、私钥、证书容器和 `secrets.json`，同时允许提交不含值的 `.env.example`。
- 扫描结果：当前受版本控制文件、源码/配置/文档及构建产物中未发现 API Key、Token、密码、私钥、证书、数据库连接串、云 AccessKey 或 Webhook；未发现已跟踪的 `.env`/密钥文件，因此本次无需生成 `.env.example`，也没有需要轮换的已知凭据。

### LD-SEC-002：远程地址可访问本机/内网（Medium）

- 利用条件：攻击者诱导用户打开其控制的页面，再通过页面解析结果、DNS 或重定向让应用向内网服务发起盲 GET 请求。
- 修复：采用公开发行策略，禁用系统代理并由连接层解析 DNS；只允许公网 IPv4 和全球可路由 IPv6，阻断 loopback、链路本地、RFC1918、共享地址空间、ULA、文档/保留/组播地址。关闭自动重定向，最多手工跟随 5 次，并对每个目标重新执行协议和实际连接地址校验，防止重定向及 DNS 重绑定绕过。
- 兼容性影响：不再支持 NAS、本机服务或局域网 HTTP 音频源。

### LD-SEC-008：测试栈和 Action 大版本迁移（Low）

- 测试项目已迁移到 `xunit.v3.mtp-v2` 4.0.1 和 Microsoft Testing Platform v2，并按 xUnit v3 要求改为可执行测试项目。
- `Microsoft.NET.Test.Sdk` 已升级到 18.10.1，Visual Studio runner 已升级到 4.0.0；`global.json` 明确选择 Microsoft Testing Platform。
- CI 已升级并固定到当前稳定提交：checkout 7.0.1、setup-dotnet 6.0.0、upload-artifact 7.0.1、download-artifact 8.0.1、attest-build-provenance 4.2.2、action-gh-release 3.0.3、CodeQL 4.38.2。

## 5. 需要开发者决定

本轮识别出的安全问题和已授权的大版本迁移均已处理，目前没有待开发者决定的安全项。

### LD-SEC-009：崩溃日志隐私、无界增长和 URL 凭据持久化（Low）

- 原风险：日志写入完整异常消息、内部异常和带文件信息的调用栈，可能泄露本地路径或 URL；日志无限追加；UI 未处理异常被吞掉后应用继续在未知状态运行。带 `userinfo` 的 URL 还会原样持久化到 `settings.json`。
- 修复：日志只保留异常类型、HResult、目标方法及不含源文件信息的调用栈；单条限制 64 KiB，总日志到 1 MiB 后只保留一份轮转日志；UI 未处理异常完成记录后交由 WPF 终止；拒绝包含用户名或密码的 URL，防止凭据进入设置和诊断信息。

## 6. 剩余风险与后续建议

- 远程媒体最终交给 Windows Media Foundation/系统编解码器解析；依赖操作系统补丁，应用无法完全消除恶意媒体文件风险。
- 当前 Windows 发行物没有 Authenticode 代码签名。构建来源证明可以验证 CI 产物来源，但不能替代 Windows 可执行文件签名；建议后续配置受保护的签名流程。
- 建议在仓库启用 Dependabot/Renovate、GitHub secret scanning 和 push protection，并保持每周 CodeQL。

## 7. 验证结果

- 锁定恢复：通过（`win-x64 --locked-mode`）。
- NuGet 漏洞审计：直接及间接依赖均无已知漏洞。
- Release 测试：37/37 通过，0 失败、0 跳过（含公网/内网策略、日志脱敏和 URL 凭据测试）。
- 自包含单文件 Release 发布：通过，`TreatWarningsAsErrors=true`。
- `git diff --check`：通过；仅报告现有 Windows 换行符转换提示，无空白错误。
