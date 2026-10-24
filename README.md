# ScreenshotCollector —— 截图自动收集器（桌面端 + 城市天际线联动模组）

自动抓取 **微信 / QQ 截图**、**城市天际线 F12 截图**、**编辑器快照 / 提示图 / 缩略图**，统一归档到本地并分组浏览、删除、分享的双端项目。

- **桌面端**：Avalonia UI（QQ 邮箱风格三栏布局）+ .NET 8，独立 EXE 运行
- **游戏模组**：Cities Skylines 一代（Harmony 2），游戏内浮动按钮联动桌面端

---

## ✨ 功能特性

| 模块 | 能力 |
|---|---|
| 微信 / QQ 截图 | 前台为微信/QQ 时，Alt+A 等快捷键截图完成后自动从剪贴板保存 |
| 天际线 F12 截图 | 监听游戏 `Screenshots` 文件夹，新截图自动归档 |
| 编辑器快照 | 递归监听 `Snapshots\<UUID>\`，按文件名自动分三类：快照 / 提示图 / 缩略图 |
| 模组联动 | 游戏内浮动按钮一键启动桌面端；TCP 本地端口 23333 推送截图 |
| 分类归档 | 统一归档到 `图片\ScreenshotCollector\分类\日期\时间戳_分类.png`，MD5 去重 |
| 真实路径 | 每条记录持久化原始路径（`.src` 元数据），可「打开原始位置」直达游戏目录原文件 |
| 列表管理 | 今天/昨天/本周/更早 分组；来源筛选；搜索；多选删除；全选 |
| 分享 | 一键把截图粘贴到微信 / QQ 聊天窗口 |
| 托盘后台 | 关闭窗口即退出；托盘菜单「隐藏到后台」后台运行 |
| 状态灯 | 底部模组连接三态灯：🟢已连接 / 🟡等待 / 🔴断开（真实状态驱动） |

---

## 📦 桌面端（软件）使用教程

### 1. 下载
- 从本仓库 **Releases** 页面下载 `ScreenshotCollector-Release-win-x64.zip`（单文件 EXE，**已内置 .NET 8 运行时，双击即用，无需安装**）
- 或自行从源码构建（见下文「从源码构建」）

### 2. 启动
解压后双击 `ScreenshotCollector.exe` 即可。建议放在固定目录（如 `D:\Tools\ScreenshotCollector\`），方便模组「一键启动」定位。

### 3. 界面与操作
三栏布局（QQ 邮箱风格）：
- **左栏**：来源分类（全部截图 / 微信 / QQ / 天际线 / 编辑器快照…）+ 底部模组连接状态灯
- **中栏**：按 今天/昨天/本周/更早 分组的截图列表（复选框 + 缩略图 + 文件名 + 来源 + 时间）；顶部搜索框实时过滤
- **右栏**：预览大图 + 操作按钮

右键条目/勾选后可执行：**打开文件位置 / 打开原始位置 / 复制路径 / 删除 / 多选删除 / 分享到微信或QQ**。

### 4. 常用设置
`设置` 页可配置：保存目录、开机自启、各抓取来源开关、快捷键等。

---

## 🎮 游戏模组使用教程

### 1. 安装（两种方式）
- **Steam 创意工坊**：搜索并订阅 **Screenshot Linker**（本模组）
- **手动安装**：把 `mods\CitiesSkylines.ScreenshotLinker\bin\` 下的
  `CitiesSkylines.ScreenshotLinker.dll` + `CitiesHarmony.Harmony.dll`
  复制到：`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CitiesSkylines.ScreenshotLinker\`

### 2. 启用
启动游戏 → **内容管理器 → 模组** → 勾选启用 **Screenshot Linker** → 重启游戏生效。

### 3. 游戏内使用
- 游戏右上角出现浮动按钮 **「启动截图收集器」**（180×30，可右键按住拖动位置）
- **左键点击**：启动桌面端程序
- 按钮下方状态灯：🟢 桌面程序已连接 / 🟡 等待连接 / 🔴 未连接
- 模组设置（游戏内 **选项 → 模组设置**）：
  - 按钮文字自定义（限制字符数）
  - 不透明度（百分比，红色标题实时显示数值）

### 4. 工作方式
模组监听游戏截图目录（F12 截图 + 编辑器快照三分类），有新图时通过本地 TCP（127.0.0.1:23333）推送给桌面端；桌面端未运行时，游戏源目录的新截图会在桌面端下次启动时**自动补扫归档**（幂等，不重复）。

---

## 🔧 从源码构建

### 桌面端（Windows）
```powershell
dotnet publish src\ScreenshotCollector\ScreenshotCollector.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\ScreenshotCollector-win-x64
```
依赖：.NET 8 SDK；NuGet 包：Avalonia 11、CommunityToolkit.Mvvm。

### 游戏模组
```powershell
dotnet build mods\CitiesSkylines.ScreenshotLinker\CitiesSkylines.ScreenshotLinker.csproj -c Release
```
- 目标框架 net46（C# 7.3，兼容游戏 Mono 运行环境）
- **输出直接进入游戏本地模组目录**（`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\...`）
- 依赖：ICities.dll / UnityEngine.dll（游戏自带）、**Harmony 2**（`CitiesHarmony.Harmony.dll`，工坊 [2040656402](https://steamcommunity.com/sharedfiles/filedetails/?id=2040656402) 同款，随包携带）

> ⚠️ 模组开发注意（Mono 兼容铁律）：禁 `lock`/`Monitor`（用 Interlocked）、禁 `Path.Combine` 三参数重载、禁 `System.Func`/LINQ（手写委托/循环）、判空用 `ReferenceEquals`——否则游戏内会抛 `MissingMethodException`/`TypeLoadException`。

---

## 📂 项目结构

```
ScreenshotCollector/
├── src/ScreenshotCollector/          # 桌面端（Avalonia + .NET 8）
│   ├── ViewModels/                   # 主界面 / 截图列表 / 设置 ViewModel
│   ├── Views/MainWindow.axaml        # QQ 邮箱风格三栏界面
│   ├── Services/                     # 剪贴板捕获 / 文件监听 / 归档 / 热键 / 托盘 / TCP 服务
│   └── ...
├── mods/CitiesSkylines.ScreenshotLinker/   # 游戏模组（net46 + ICities + Harmony 2）
├── docs/源码文件说明书.md            # 桌面端源码结构说明
├── mods/CitiesSkylines.ScreenshotLinker/说明书.md  # 模组源码结构说明
└── publish/                          # 发布产物（Release EXE / ZIP，不入库）
```

---

## 🧭 开源协议

本项目采用 **GNU General Public License v3.0（GPLv3）**，详见 [LICENSE](LICENSE)。

- ✅ 允许：商用、修改、分发、个人使用
- 📌 要求：衍生作品必须同样以 GPLv3 开源；保留版权声明；附上许可证副本
- ⚠️ 免责：程序按「AS IS」提供，无任何明示或默示担保

**版权**：Copyright (C) 2026 NS-Michael-007

**第三方依赖**：
- Avalonia UI、CommunityToolkit.Mvvm（MIT License）
- Harmony 2（CitiesHarmony，工坊 2040656402，MIT/开源）
- ICities / UnityEngine（游戏自带，属游戏资产）

---

## 📄 发布说明（Releases）

- **v1.0.0**（本版）：完整双端功能——剪贴板抓取、游戏监听三分类、真实路径、幂等补扫、分组管理、分享删除、托盘后台、状态灯、模组浮动按钮
- 发布资产：
  - `ScreenshotCollector-Release-win-x64.zip`（桌面端单文件版，39.3MB，免装运行环境）
  - 源码（本仓库，GPLv3）

> 💡 桌面端 EXE 无法随 Steam 工坊分发（工坊仅接受游戏模组 DLL），因此桌面端通过 Releases 下载，工坊页面附下载链接。
