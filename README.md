# MineCraftDownloadTool（MCDT）

MineCraftDownloadTool 是一个轻量的 Minecraft 资源下载工具。单个 exe，双击即用，不依赖启动器，也不需要登录账号。

## 功能

### 游戏本体

- 下载原版 Minecraft：正式版、快照、远古版本
- 安装模组加载器：Fabric、Quilt、Forge、NeoForge
- 游戏文件默认写入 `.minecraft\versions`，与常见启动器目录结构一致
- 默认使用 BMCLAPI 国内镜像，失败时自动切换到 Mojang / 加载器官方源
- 下载后校验 SHA1 和文件大小，已下载完整的文件会自动跳过
- 安装与下载任务会进入队列，可在“任务”页查看进度、取消或重试
- 下载完成后默认不自动退出；可在设置中开启“全部完成后 5 秒自动关闭”

### Mod、整合包、资源包、光影包、数据包

- 下载源可选 Modrinth、CurseForge，或同时搜索两者
- 按游戏版本、加载器筛选；详情页可切换版本和加载器，并选择具体版本下载
- 详情页会在版本列表顶部显示当前最新匹配版本的“前置资源”：列出必需的前置 Mod，点击可直接进入其详情页；返回时会回到原来的 Mod 详情页，而不是搜索页
- 安装 Mod 时会检查必需的前置依赖：缺少依赖时弹窗提示，可选择仅下载当前 Mod、跳到前置 Mod 的详情页或取消；已安装的依赖会自动跳过，可选依赖不处理
- 整合包安装为独立实例：加载器、客户端与配置会合并到同一个 `.minecraft\versions\<整合包名>\` 目录，不会额外留下单独的加载器版本；同时写入启动器档案（`gameDir` 指向该目录），不同整合包的 Mod 和配置互不干扰
- 支持导入本地整合包：在“整合包”页点击“导入本地整合包”，选择 `.mrpack`（Modrinth）或 `.zip`（CurseForge）文件即可离线安装，源文件不会被移动或删除。CurseForge 整合包需要先在设置中填写 API Key
- 数据包支持直接放入某个存档的 `datapacks` 目录，或仅下载到 `.minecraft\datapacks`
- 详情页可跳转 Modrinth / MC 百科查看介绍，也可一键复制名称
- 资源包、光影包、数据包均按原文件名保存，便于启动器直接识别

### 下载任务与安装位置

- 点击“下载”后先弹出安装位置选择窗口：版本或存档以图标列表展示，顶部搜索框可按名称筛选
- 所有下载按队列顺序执行，同一时间只运行一个任务；排队期间可以继续浏览和添加其他资源
- 采用多线程分段下载，连接失败会自动重试并切换镜像 / 官方源
- “任务”页显示每个任务的阶段、进度与详细日志，支持取消、重试和清除已结束任务

## 版本管理方式

在“设置”中可以选择：

- **不隔离（默认）**：所有版本共用 `.minecraft\mods`、`.minecraft\resourcepacks`、`.minecraft\shaderpacks` 和 `.minecraft\saves`。
- **版本隔离**：程序会扫描 `.minecraft\versions` 下所有已安装版本。下载 Mod、资源包或光影包时，可以指定装到哪个版本对应的目录；下载数据包时，会列出所有版本 `saves` 下的存档（标注所属版本），可直接装进对应存档的 `datapacks`。

安装位置选择采用图标列表：版本项显示原版 / Fabric / Forge / NeoForge / Quilt 图标，并标注正式版、游戏版本与加载器版本；加载器版本会显示为易读的名称，例如 `1.20.1 NeoForge 47.1.106`，而不会直接显示安装器生成的目录名。存档项显示存档图标，并注明所在版本，重名存档也能区分。

整合包始终安装为独立版本，不受该选项影响。切换选项后，下载页的安装位置会立即刷新。

## 外观主题

在“设置”的“外观主题”中可以在浅色和深色主题之间切换。切换后所有页面、分组、列表、进度条和窗口标题栏会立即更新，无需重启。

同一区域还可以切换主题色：草原绿、天空蓝、海洋青、紫罗兰、落日橙、樱花粉，或点击“自定义…”选择任意颜色。主题色会应用到顶部横幅、按钮、链接、选中状态、提示底纹与边框，浅色和深色模式下都即时生效。

主题模式和主题色都会保存在 `%APPDATA%\MineCraftDownloadTool\settings.json`，下次启动自动沿用。

## CurseForge API Key

搜索和下载 CurseForge 内容需要一个免费 API Key：

1. 打开程序右上角“设置”
2. 点击“在 CurseForge 控制台免费申请 API Key”，在网页中创建 Key
3. 把 Key 粘贴到输入框，点击“测试并保存”

Key 只保存在本机 `%APPDATA%\MineCraftDownloadTool\settings.json`，界面中默认以密码形式显示，程序不会上传或写入日志。未填写 Key 时，CurseForge 搜索会提示配置；也可以只用 Modrinth，无需任何 Key。

## 使用

1. 把 `MineCraftDownloadTool.exe` 放到你的 `.minecraft` 同级目录，例如：

   ```text
   D:\Games\
   ├─ .minecraft\
   └─ MineCraftDownloadTool.exe
   ```

   如果同级没有 `.minecraft`，程序会自动创建。也可以点击“浏览…”手动选择目录。

2. 顶部标签页选择要下载的内容类型。
3. 搜索并进入详情页，选择游戏版本、加载器和具体文件版本，点击“下载”。
4. 在弹出的窗口中选择安装位置（数据包选择存档），任务会加入队列并按顺序执行。
5. 在“任务”页查看进度；全部完成后程序默认保持打开。整合包可在启动器的版本列表中选择新增的实例。
6. 想安装已有的整合包文件时，切到“整合包”页点击“导入本地整合包”，选择 `.mrpack` 或 `.zip`，确认后按队列安装为独立版本。

> Forge / NeoForge 需要本机已安装 Java，程序会自动寻找。原版、Fabric、Quilt 的下载不需要 Java。

## 说明

- 本工具只负责下载和安装文件，不包含游戏启动器，也不提供账号登录。
- 安装 Forge / NeoForge 时会先补齐原版文件，再按安装器清单预下载并校验全部组件（BMCLAPI 优先，失败自动切换 Forge maven / Maven Central），最后调用官方安装器。这样可避免安装器自身因镜像节点异常而中断；安装器内的下载地址同样会被改写到国内镜像，失败会自动重试并回退官方源。
- 安装 Fabric / Quilt / Forge / NeoForge 时会读取原版客户端与运行库，但最终只生成一个自包含的加载器版本；如果同名原版版本此前已存在，会保留原目录和原版本 JSON，不做覆盖或删除。
- NeoForge 1.20.1 的版本目录由官方安装器生成，名称是 `1.20.1-forge-47.1.106`（这是 NeoForge 自身的历史命名）。程序会按 NeoForge 识别并显示，不修改该目录名，以免影响启动器识别。
- Modrinth 与 CurseForge 的 API 直连，不走 BMCLAPI 镜像。
- 已存在的完整文件会被复用，因此中断后重新运行可以继续，不会重复下载。
- 整合包安装会合并 `overrides/` 与 `client-overrides/`，不会覆盖你原有的其他版本。
- 整合包实例的版本 JSON 是完整的自包含配置，可直接被 PCL2、HMCL 等启动器识别。若使用旧版本安装过整合包，建议删除旧的实例版本后重新安装。
- 适用系统：Windows 7 SP1 及以上，需安装 .NET Framework 4.8（Windows 10/11 已自带）。
- 镜像仅用于加速下载，最终启动游戏仍由你的启动器负责。

## 从源码构建

需要 Windows 自带的 .NET Framework 4.x 编译器（无需安装 Visual Studio 或 .NET SDK）：

```bat
build.cmd
```

生成 `dist\MineCraftDownloadTool.exe`。源码使用 C# 5 语法，以兼容系统自带的 `csc.exe`。

## 项目结构

```text
src/                  核心源码与 WinForms 界面
assets/app.ico        程序图标
build.cmd             一键构建脚本
.gitignore            构建产物与本地文件忽略规则
dist/MineCraftDownloadTool.exe 构建产物（单文件）
```
