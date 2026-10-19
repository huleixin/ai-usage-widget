# 配置指南

## 第一次使用

下载 Releases 中的 Windows x64 ZIP，完整解压，打开 `AIUsageWidget.exe`。第一次自动显示设置；日后可以点顶部 ⚙，或双击 `Configure.cmd` 打开设置。

### Codex 路径

留空会在 PATH、Codex 桌面客户端目录和常见 npm 原生程序目录中查找。需要的是 Windows 原生 `codex.exe`。点击「浏览」可手动选择。

通过「登录 Codex」使用官方登录流程。登录信息由 Codex 自己管理，本工具不复制其登录文件、不要求填写 ChatGPT 密码、不提供共享账号。路径变更后重新保存。

### DeepSeek 密钥

填写自己的 DeepSeek 官方 API Key，点击「保存并使用」。以后密码框留空会保留已保存的密钥；勾选「清除浮窗保存的 DeepSeek 密钥」可以删除。保存到凭据管理器的密钥优先于已有环境变量。

如果清除后仍然能读到余额，请检查当前用户的 `DEEPSEEK_API_KEY` 环境变量；本工具不会擅自删除已有环境变量。

### 刷新和自动启动

刷新间隔可设置为 30–3600 秒，默认 60 秒。重置倒计时始终每秒变化，不依赖网络刷新频率。

开机启动通过当前用户「启动」文件夹中的 `AI Usage Widget.lnk` 实现，不需要管理员权限。启用后不要移动发行包所在目录；移动后重新保存自动启动设置。

## 设置文件

文件：`%LOCALAPPDATA%\AIUsageWidget\settings.json`。无需手动编辑；不包含 API Key。

```json
{
  "CodexPath": "",
  "RefreshSeconds": 60,
  "StartWithWindows": false,
  "Left": null,
  "Top": null
}
```

## 更新

先点击 × 退出，解压新版本到原目录并覆盖程序文件。配置与凭据不在程序目录内，会保留。升级无需复制或分享账户文件。
