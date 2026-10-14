# CirDeck

免费、开源的 Windows 启动台和桌面 Dock。本机扫描快捷方式，用拼音或英文搜到应用后启动。不设激活码，不收集使用数据。

## 现在能做什么

- 全屏启动台，按页浏览已安装程序
- 拼音首字母和英文名称搜索
- 常驻底部 Dock，鼠标靠近时放大图标
- 把应用固定到 Dock，或从 Dock 移除
- 图标解码后缓存在本机，下次打开直接读取
- 全局热键 `Ctrl+Alt+Space` 唤出或收起启动台

按 `Esc` 收起启动台。在应用图标上右键可以固定、移除，或打开文件所在位置。Dock 空白处右键可以选择重新扫描或退出。

## 环境

- Windows 10 或 Windows 11
- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)，需要包含 Windows 桌面工作负载

工程目标框架是 `net6.0-windows`。界面使用 WPF，Dock 放大只更新渲染缩放，不在每一帧重新测量布局。

## 构建和运行

在仓库根目录执行：

```powershell
dotnet run --project tests/CirDeck.Core.Tests -c Release
dotnet run --project src/CirDeck.App -c Release
```

只扫描开始菜单和桌面快捷方式、不打开窗口：

```powershell
dotnet run --project src/CirDeck.App -c Release -- --scan
```

扫描结果写到 `%LocalAppData%\CirDeck\scan.txt`。

## 本机数据

| 内容 | 位置 |
| --- | --- |
| 设置、Dock 固定项 | `%AppData%\CirDeck` |
| 应用列表、图标缓存、日志 | `%LocalAppData%\CirDeck` |

## 目录

```
src/CirDeck.Core       搜索、Dock 几何、设置读写
src/CirDeck.Interop    Win32、快捷方式解析、图标提取
src/CirDeck.App        WPF 启动台和 Dock
tests/CirDeck.Core.Tests
```

## 说明

拼音首字母表由 [mozillazg/pinyin-data](https://github.com/mozillazg/pinyin-data) 的第一个读音提取而来，该数据以 MIT 许可证发布。

本仓库以 [Apache License 2.0](LICENSE) 授权。
