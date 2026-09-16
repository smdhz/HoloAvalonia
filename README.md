# HoloAvalonia

一个使用 [Avalonia UI](https://avaloniaui.net/) 和 .NET 构建的跨平台 Hololive 直播日程桌面客户端。

HoloAvalonia 从配置的 Forge/OData 服务读取直播日程，以本地时区展示即将开始的直播，并通过 WebSocket 接收实时更新。

> [!IMPORTANT]
> 这是依赖配套后端服务的客户端，不是独立的日程抓取器。运行前需要可访问的 OData/API 服务及其登录密码。

## Avalonia 的跨平台能力

得益于 Avalonia UI，同一套 .NET 与 XAML 代码可以在 Windows、macOS 和 Linux 上构建并运行。应用的界面、主题和交互逻辑能够跨平台复用，同时仍可针对不同操作系统发布原生桌面程序。

> 如此木大的力量！

## 功能

- 以卡片形式展示直播封面、成员、标题和本地开始时间
- 通过 WebSocket 接收更新并自动刷新日程
- 显示当天已开始的项目，或只显示最近一小时及未来项目
- 隐藏 Shorts、`#dance` 及指定成员
- 保存成员筛选设置
- 亮色与暗色主题
- 预览并复制直播封面
- 复制日程 ID
- 通过后端接口创建日历事件

## 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows、macOS 或 Linux 桌面环境
- 可访问的 Forge/OData 后端和有效登录密码

## 快速开始

```bash
git clone <your-repository-url>
cd HoloAvalonia
dotnet restore
dotnet run
```

首次启动时，应用会要求输入 API 密码。认证成功后，令牌会缓存在当前用户的应用数据目录中。

## 配置

运行参数位于 [`appsettings.json`](appsettings.json)：

```json
{
  "Odata": {
    "ServiceRoot": "https://example.com/odata",
    "EntitySet": "HololiveSchedule"
  }
}
```

| 字段 | 说明 |
| --- | --- |
| `Odata:ServiceRoot` | OData 服务根地址；应用也会使用相同主机访问认证、缩略图、WebSocket 和日历接口 |
| `Odata:EntitySet` | 日程实体集名称 |

应用根据 `ServiceRoot` 使用以下配套端点：

- `POST /api/token`：密码登录并获取 Bearer token
- `/hololive-thumbnail/{id}`：直播缩略图
- `/ws/hololive`：日程推送
- `POST /api/hololive-schedule/{id}/calendar`：创建日历事件

## 本地数据

应用会在当前用户的系统应用数据目录下创建 `HoloAvalonia` 文件夹：

| 文件 | 用途 |
| --- | --- |
| `filters.json` | 已隐藏成员和已知成员列表 |
| `api-token.json` | AES-GCM 加密后的 API token 缓存 |
| `api-token.key` | 本地生成的加密密钥；Unix 系统会尝试限制为仅当前用户可读写 |

若要退出登录，可关闭应用后删除 `api-token.json`。如需完全清除认证数据，同时删除 `api-token.key`。

> [!NOTE]
> 加密密钥和令牌缓存在同一用户目录中，这可以避免令牌以明文保存，但不能替代操作系统提供的安全凭据存储。

## 构建与发布

构建 Debug 版本：

```bash
dotnet build
```

发布 Release 版本：

```bash
dotnet publish -c Release
```

也可以指定目标平台，例如：

```bash
dotnet publish -c Release -r win-x64 --self-contained true
dotnet publish -c Release -r linux-x64 --self-contained true
dotnet publish -c Release -r osx-arm64 --self-contained true
```

## 项目结构

```text
Models/       日程数据和界面命令
Services/     OData、认证、推送、日志与本地设置
Views/        Avalonia 窗口和界面
Resources/    应用图标与成员图标映射
```

## 技术栈

- .NET 10
- Avalonia UI 12
- Microsoft.Extensions.Hosting
- AsyncImageLoader.Avalonia

## 声明

本项目是非官方的社区项目，与 COVER Corporation 或 hololive production 无隶属或授权关系。项目名称及相关商标归各自权利人所有。

## 许可证

仓库目前未包含开源许可证。在添加明确许可证之前，代码默认保留所有权利。
