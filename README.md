# AI 用量浮窗

**AI 还剩多少额度，看一眼桌面就知道。**

Windows 桌面上的小巧玻璃浮窗，集中显示 Codex 的 5 小时／每周剩余额度、重置倒计时，以及 DeepSeek API 余额。独立运行，无需保持终端打开，不占用任务栏按钮。

[**下载最新版 Windows x64**](https://github.com/huleixin/ai-usage-widget/releases/latest) · [配置说明](docs/CONFIGURATION.md) · [隐私说明](docs/PRIVACY.md) · [English](docs/README.en.md)

## 下载与启动

1. 在 [Releases](https://github.com/huleixin/ai-usage-widget/releases/latest) 下载 `AIUsageWidget-v1.0.1-win-x64.zip`（以后选择相应最新版）。**不要下载 Source code 来直接运行。**
2. 解压到一个固定文件夹，例如「文档 / AIUsageWidget」。不要直接在压缩包中运行。
3. 双击 `AIUsageWidget.exe`。发行包自带 .NET 运行组件，无需另外安装 .NET 或 Visual Studio。
4. 首次启动会打开配置页。连接自己的 Codex；需要 DeepSeek 时，再填写自己的 API Key。点击「保存并使用」。

推荐 **Windows 11 x64**。Windows 10 2004 及以上保留软件模糊回退，但视觉效果、流畅度取决于系统，不保证与 Windows 11 一致。暂不提供 ARM64 / 32 位 / macOS / Linux 构建。

这是未签名的社区工具，Windows 可能提示「未知发布者」。请核对下载来源与发布页 SHA-256 校验文件；不要关闭系统安全保护。企业策略如果阻止本地 PowerShell 辅助组件，需要由管理员允许，工具不会修改全局执行策略。

## 连接账户

### Codex

- 本工具读取**你自己本机登录的 Codex**，不包含作者的登录信息。
- 安装并使用 ChatGPT 账号登录 Codex。已有 Codex 桌面客户端时，浮窗会尝试查找其内置 `codex.exe`；也可按[官方 CLI 安装说明](https://developers.openai.com/codex/cli/)安装。
- 配置页中路径可留空自动查找；失败时点击「浏览」选择真正的 `codex.exe`，再点击「登录 Codex」。CLI 也可以在终端运行 `codex login`。
- npm 安装的 `codex.cmd` / `codex.ps1` 是启动脚本，浮窗需要它们对应的原生 `codex.exe`。自动查找会检查常见 npm 目录；找不到时请手动指定。
- API Key 登录、未登录、无订阅额度，或服务端没有返回相应窗口时，不能显示 5 小时／每周订阅额度。

### DeepSeek（可选）

在浮窗顶部点击 **⚙**，将自己的 [DeepSeek API Key](https://platform.deepseek.com/api_keys) 填入密码框，保存即可。密钥保存在当前 Windows 用户的**凭据管理器**，不会写入源码、设置 JSON 或发布包。

这里显示的是**官方 API 账户余额**，不代表 DeepSeek 网页或 App 聊天额度。也兼容当前用户已有的 `DEEPSEEK_API_KEY` 环境变量。DeepSeek 与 Codex 可单独使用。

## 日常使用

| 操作 | 方式 |
| --- | --- |
| 移动浮窗 | 按住顶部 `≡` 手柄拖动 |
| 吸附顶部 | 拖到屏幕工作区顶部附近后松开 |
| 更改配置 | 点击顶部 `⚙`；再次打开程序也会呼出设置 |
| 刷新 | 点击 `↻`；默认每 60 秒自动刷新 |
| 关闭 | 点击 `×`，完全退出 |
| 开机启动 | 在设置中勾选「登录 Windows 后自动启动」 |

额度显示**剩余百分比**；倒计时每秒更新，省略为零的单位。卡片和底部区域鼠标穿透，顶部保留交互。玻璃外观、圆角及流光动画由 WPF 和 Windows Composition 实现，属于 Liquid Glass 风格的社区设计。

## 数据与隐私

- 不提供公共服务器，不收集遥测，不上传使用记录。
- Codex 用量通过本机 [`codex app-server`](https://developers.openai.com/codex/app-server/) 的只读额度接口查询；认证和上游网络请求由官方 Codex 处理。
- DeepSeek 密钥只在余额请求中发送到官方 `https://api.deepseek.com/user/balance`。
- 用户设置位于 `%LOCALAPPDATA%\AIUsageWidget\settings.json`，只保存路径、刷新间隔、窗口位置和启动偏好。
- 余额结果通过临时本地 JSON 传回界面，读取后删除；超时或异常退出时可能残留于 Windows 临时目录。
- Windows 11 优先使用系统 GPU 背景合成。旧系统的软件回退仅在内存中采样窗口附近的桌面像素，用于模糊显示；不写入截图，不发送像素。

详细存储位置、清理与卸载见[隐私说明](docs/PRIVACY.md)。仓库不包含任何作者余额截图、配置、密钥、账户数据库或历史运行日志。

## 从源码构建

需要 Windows x64、.NET 8 SDK 或更新版本，以及 Visual Studio 2022 / Build Tools 的「使用 C++ 的桌面开发」与 Windows SDK（建议 10.0.26100 或更新版本）。不要求用户安装这些开发工具来运行发行包。

```powershell
git clone https://github.com/huleixin/ai-usage-widget.git
cd ai-usage-widget
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish.ps1
```

输出位于 `artifacts/`，同时生成便携 ZIP 和 SHA-256 校验文件。`scripts/Publish.ps1 -FrameworkDependent` 可生成需要 .NET 8 桌面运行时的较小构建。C++ 构建脚本自动寻找 Visual Studio，不使用作者电脑路径。

推送 `v*` 标签时，GitHub Actions 自动构建并发布 Releases；手动运行工作流时生成可下载的构建产物。工作流只使用 GitHub 提供的 `GITHUB_TOKEN`，不需要配置个人访问令牌或账户密钥。

## 常见问题

- **Codex 待连接**：打开设置，确认 CLI 路径和 ChatGPT 登录；额度接口可能随 Codex 更新改变，升级 CLI 后再试。
- **只看到 DeepSeek**：两个来源独立读取，先检查 Codex 是否可用。
- **DeepSeek 读取失败**：检查 API Key、余额接口的网络连接和账户状态。
- **改完 Windows 环境变量没生效**：保存本工具的密钥，或退出后重新打开；本工具不会自动修改已有环境变量。
- **关闭终端后不见了**：请直接运行发行包中的 `.exe`，不要使用旧版脚本。
- **移动了文件夹后开机启动失效**：重新打开设置，将自动启动关闭再开启并保存。

## 许可

[MIT](LICENSE)。第三方运行组件保留其自身许可，详情见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。本项目与 OpenAI、DeepSeek、Apple 无官方隶属关系。
