# 隐私与卸载

## 本机保存的数据

| 数据 | 位置 | 是否包含在 GitHub 下载包 |
| --- | --- | --- |
| 界面偏好、Codex 路径、窗口位置 | `%LOCALAPPDATA%\AIUsageWidget\settings.json` | 否 |
| DeepSeek API Key | Windows 凭据管理器的普通凭据 `AIUsageWidget/DeepSeekApiKey` | 否 |
| Codex 登录信息 | 由官方 Codex 管理的本机用户数据目录 | 否，本工具不打包、不导出 |
| 自动启动快捷方式 | 当前用户启动文件夹的 `AI Usage Widget.lnk` | 否 |
| 查询结果 | Windows 临时目录的 `CodexUsageWidget-*.json`，正常读取后删除 | 否 |

不收集遥测，不提供后台云服务，不存储长期用量历史。程序源码不嵌入任何个人账户、密钥或个人电脑路径。发行构建禁用调试符号，移除开发阶段的截图与性能日志代码。

## 网络请求与桌面像素

Codex 的认证与服务请求由本机 Codex CLI 完成。本程序向它请求只读用量；不会修改 Codex 客户端配置。

DeepSeek 密钥通过 HTTPS Authorization 请求头发送到 `api.deepseek.com/user/balance`。程序不会把密钥放进命令行参数、设置 JSON 或日志；后台辅助进程通过私有进程环境变量接收密钥。具有同一 Windows 用户权限的其他程序仍可能读取其进程或凭据，本工具不提供进程隔离保证。

玻璃背景在 Windows 11 优先使用系统合成器。软件回退会在内存中采样浮窗附近的桌面，仅供模糊渲染，不写入图片，不上传。

## 卸载

1. 打开设置，取消自动启动，勾选清除 DeepSeek 密钥，点击保存。
2. 点击 × 退出，删除解压的程序文件夹。
3. 如需清除偏好，删除 `%LOCALAPPDATA%\AIUsageWidget`。
4. 如果曾单独设置 `DEEPSEEK_API_KEY` 环境变量，按需在 Windows 用户环境变量中自行删除。

无法打开程序时，可以手动删除启动快捷方式、设置文件夹，并在「控制面板 → 凭据管理器 → Windows 凭据 → 普通凭据」中删除 `AIUsageWidget/DeepSeekApiKey`。

Codex CLI、其登录信息和其他应用的数据不会随浮窗卸载被删除。
