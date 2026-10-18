# 鲸鱼娘的媒体库 · VideoManager

一个本地影音库管理器：把散落在硬盘各处的动画 / 剧集文件夹导进来，自动按「作品 + 季 + 集」整理成卡片墙，
封面直接从视频里抽帧，双击卡片看分集，单击单集调用系统默认播放器。

- 平台：Windows 10 / 11（x64）
- 技术栈：WPF + .NET 9（`net9.0-windows`），MVVM 手写，**零第三方 NuGet 依赖**
- 唯一外部依赖：ffmpeg / ffprobe（只用来抽封面，没有也能用，只是没封面）

---

## 快速开始

```powershell
cd E:\VideoManager
dotnet build VideoManager.sln -c Release
.\src\VideoManager\bin\Release\net9.0-windows\VideoManager.exe
```

首次启动窗口是空的，点左下角 **「＋ 添加路径」** 选一个装视频的文件夹（也可以直接把文件夹拖进窗口），
扫描完成后卡片墙就出来了。重新扫描按 **F5**。

---

## 功能

### 导入与整理

- **多根目录**：想加几个加几个，可单独停用（路径页里的复选框），停用的不参与扫描。
- **拖放导入**：把文件夹（或视频文件，会取其所在目录）拖进窗口即可添加。
- **自动归类**：扫描时按「文件夹名 + 文件名」推断作品名、季、集、集标题、特别篇、发布组：
  - `S01EP10_TV-矛盾だらけの死体.mp4` → 第 1 季第 10 集，标题「矛盾だらけの死体」
  - `S02EP24_SP2-しばしのお別れ.mp4` → 特别篇 SP2
  - `[FZSD][Idol_Time_Pripara][001][GB][720P][x264_AAC].mp4` → 《Idol Time Pripara》第 1 集
  - `[ANi] 世界最強的魔女，就此開始 - 01 [1080P][Baha][WEB-DL].mp4` → 《世界最強的魔女，就此開始》第 1 集
  - `【FSD粉羽社】名侦探光之美少女[36][v2][试炼的金牌].mp4` → 第 36 集，集标题「试炼的金牌」
  - 季目录 `S2` / `Season 3` / `第二季` 里的纯集数文件（`S02E59.mp4`、`01.mp4`）会归到对应的季
  - 支持 mp4 / mkv / avi / ts / m2ts / mov / wmv / flv / webm / m4v / rmvb / mpg 等 20 余种容器
- **改名**：详情抽屉里双击标题，或在卡片右键菜单里改；只改库内显示名，不动硬盘文件。
- **手动换封面**：右键卡片 →「换一张封面…」选本地图片；「重新抓这个封面」重新抽帧。
- **标记看过**：详情里点单集右侧的 ✓。
- **搜索**：标题栏搜索框，空格分隔多个关键词（全部命中才显示）。
- **排序**：标题 / 集数 / 文件夹，再点一次切换升降序。
- **卡片尺寸**：小 / 中 / 大三档。

### 封面

- 每部作品的封面取自**这部作品的第一个视频**（按季 → 特别篇靠后 → 集号 → 自然文件名排序后的首个文件）。
- 抽帧位置默认在视频 **33%** 处，宽度 480px，取不到就依次退到 15%、50%、3 秒处。
- 27 部作品首次扫描后约十余秒内全部生成，封面缓存放在数据目录，命中缓存不重复抽帧。
- ffmpeg 查找顺序：设置页里手动指定的路径 → 环境变量 PATH → `D:\Tools\ffmpeg`、`C:\ffmpeg\bin` 等常见位置。

### 播放

- 单击单集 → 调用**系统默认播放器**（即资源管理器里双击的行为），不绑定具体播放器。
- 「▶ 播放」按钮播该作品第一集；右键 →「在资源管理器中打开」定位文件。
- 播放过的集会被记为「看过」，并出现在「最近播放」里（保留最近 60 部作品）。

### 界面

- 无边框深色主题（鲸鱼蓝配色），标题栏可拖动、双击最大化。
- 左侧导航：全部作品 / 最近播放 / 媒体库路径 / 设置。
- 右侧详情抽屉：封面、标题、集数、季信息、文件夹、分集列表。
- 窗口大小、位置、最大化状态会记住，启动时自动夹回当前屏幕工作区内。

### 快捷键

| 按键 | 作用 |
| --- | --- |
| `F5` | 重新扫描全部路径 |
| `Ctrl + F` | 聚焦搜索框 |
| `Esc` | 关闭详情抽屉 / 清空搜索 |

---

## 数据与配置

全部数据在 `%LOCALAPPDATA%\VideoManager\`：

| 文件 | 内容 |
| --- | --- |
| `settings.json` | 根目录列表、显示名覆盖、自定义封面、播放记录、ffmpeg 路径、窗口状态等 |
| `library-cache.json` | 上次扫描结果（启动时先秒开，超 6 小时或手动 F5 才重扫） |
| `covers\*.jpg` | 抽取的封面缓存（文件名是作品 key 的 SHA1 前 20 位） |

设置页里可以一键「清空并重抓」全部封面，也可以打开数据目录。

卸载 / 重置：直接删掉 `%LOCALAPPDATA%\VideoManager` 即可，硬盘上的视频一个都不会动。

---

## 项目结构

```
E:\VideoManager
├─ VideoManager.sln
└─ src
   ├─ VideoManager.Core        # 与 UI 无关的核心逻辑（net9.0）
   │  ├─ Models\               # Series / MediaFile / AppSettings / LibraryRoot
   │  ├─ Parsing\NameParser.cs # 文件名 → 作品/季/集/集标题/特别篇/发布组
   │  ├─ Library\LibraryScanner.cs  # 遍历目录、归组、排序
   │  ├─ Media\CoverService.cs      # ffmpeg 抽帧 + ffprobe 时长
   │  ├─ Media\PlayerLauncher.cs    # 调用默认播放器 / 资源管理器
   │  ├─ Storage\AppStorage.cs      # settings.json / library-cache.json / 封面缓存
   │  └─ Util\                      # 自然排序、SHA1 短哈希
   ├─ VideoManager             # WPF 表现层（net9.0-windows）
   │  ├─ Views\                # MainWindow、RenameDialog
   │  ├─ ViewModels\           # MainViewModel、各类卡片 VM
   │  ├─ Themes\Theme.xaml     # 配色、控件样式
   │  └─ Common\               # ObservableObject、RelayCommand、值转换器
   └─ VideoManager.Probe       # 命令行探针：验证扫描 / 解析 / 抽帧
```

### 探针（调试用）

```powershell
cd E:\VideoManager\src\VideoManager.Probe
dotnet run -c Release                                  # 扫三个参考路径，打印归组结果
dotnet run -c Release -- parse "某文件名.mp4"          # 只看解析结果
dotnet run -c Release -- cover "某视频.mp4" out.jpg    # 只看抽帧
```

---

## 常见问题

**封面一片空白？** 装 ffmpeg 并加到 PATH，或到「设置 → 封面抓取」手动指定 `ffmpeg.exe`。
状态栏显示「已就绪」即代表探测成功。

**某部作品被拆成两张卡片？** 说明两张的文件夹名或文件名差异过大。右键改一次显示名即可，
或者把文件按统一命名放好。

**集号显示不对？** 详情抽屉里能看到原始文件名；文件名里没有可识别的集号时按顺序计数。
