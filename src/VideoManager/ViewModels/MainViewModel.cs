using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoManager.Common;
using VideoManager.Core.Library;
using VideoManager.Core.Media;
using VideoManager.Core.Models;
using VideoManager.Core.Storage;
using VideoManager.Views;

namespace VideoManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppStorageHost _host;
    private readonly Dispatcher _dispatcher;

    private CancellationTokenSource? _coverCts;

    /// <summary>重抓封面的次数计数，用来轮换抽帧时间点。</summary>
    private int _coverVariant;
    private string _searchText = "";
    private string _statusText = "准备就绪";
    private bool _isBusy;
    private double _progressValue;
    private bool _isIndeterminate;
    private NavSection _section = NavSection.All;
    private CardSizePreset _cardSize = CardSizePreset.Medium;
    private SeriesCardViewModel? _selectedCard;
    private bool _isDetailOpen;
    private bool _hasScanned;
    private string _sortMode = "标题";
    private bool _sortDesc;

    public MainViewModel(AppStorageHost host)
    {
        _host = host;
        _dispatcher = Dispatcher.CurrentDispatcher;

        Cards = [];
        Episodes = [];
        Roots = [];

        CardsView = CollectionViewSource.GetDefaultView(Cards);
        CardsView.Filter = FilterCard;

        ApplySettings();

        AddRootCommand = new AsyncRelayCommand(AddRootAsync);
        RemoveRootCommand = new RelayCommand(p => RemoveRoot(p as RootItemViewModel));
        ScanCommand = new AsyncRelayCommand(ScanAsync);
        OpenSeriesCommand = new RelayCommand(p => OpenSeries(p as SeriesCardViewModel));
        CloseDetailCommand = new RelayCommand(() => IsDetailOpen = false);
        PlayFirstCommand = new AsyncRelayCommand(() => PlaySeriesAsync(SelectedCard, playFirstOnly: true));
        PlayEpisodeCommand = new RelayCommand(p => PlayEpisode(p as EpisodeItemViewModel));
        RevealCommand = new RelayCommand(p => Reveal(p));
        SetCoverCommand = new AsyncRelayCommand(p => SetCustomCoverAsync(p as SeriesCardViewModel ?? SelectedCard));
        RegenerateCoverCommand = new AsyncRelayCommand(p => RegenerateCoverAsync(p as SeriesCardViewModel ?? SelectedCard));
        SetCardSizeCommand = new RelayCommand(p => SetCardSize(p));
        NavigateCommand = new RelayCommand(p => Navigate(p));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        MarkWatchedCommand = new RelayCommand(p => MarkWatched(p as EpisodeItemViewModel));
        ChangeSortCommand = new RelayCommand(p => ChangeSort(p as string));
        ResetTitleCommand = new AsyncRelayCommand(p => ResetTitleAsync(p as SeriesCardViewModel ?? SelectedCard));
        BrowseFfmpegCommand = new RelayCommand(BrowseFfmpeg);
        RevealLibraryCommand = new RelayCommand(() => RevealPath(_host.Storage.DataDirectory));
        ClearCoversCommand = new AsyncRelayCommand(ClearCoversAsync);
        DeleteSeriesCommand = new AsyncRelayCommand(p => DeleteSeriesAsync(p as SeriesCardViewModel ?? SelectedCard));
        DeleteEpisodeCommand = new AsyncRelayCommand(p => DeleteEpisodeAsync(p as EpisodeItemViewModel));

        Cards.CollectionChanged += (_, _) => UpdateCounters();
    }

    // ─────────────── 集合 ───────────────

    public ObservableCollection<SeriesCardViewModel> Cards { get; }
    public ObservableCollection<EpisodeItemViewModel> Episodes { get; }
    public ObservableCollection<RootItemViewModel> Roots { get; }

    public ICollectionView CardsView { get; }

    // ─────────────── 命令 ───────────────

    public AsyncRelayCommand AddRootCommand { get; }
    public RelayCommand RemoveRootCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand OpenSeriesCommand { get; }
    public RelayCommand CloseDetailCommand { get; }
    public AsyncRelayCommand PlayFirstCommand { get; }
    public RelayCommand PlayEpisodeCommand { get; }
    public RelayCommand RevealCommand { get; }
    public AsyncRelayCommand SetCoverCommand { get; }
    public AsyncRelayCommand RegenerateCoverCommand { get; }
    public RelayCommand SetCardSizeCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand MarkWatchedCommand { get; }
    public RelayCommand ChangeSortCommand { get; }
    public AsyncRelayCommand ResetTitleCommand { get; }
    public RelayCommand BrowseFfmpegCommand { get; }
    public RelayCommand RevealLibraryCommand { get; }
    public AsyncRelayCommand ClearCoversCommand { get; }

    /// <summary>把整部作品的视频文件删到回收站。</summary>
    public AsyncRelayCommand DeleteSeriesCommand { get; }

    /// <summary>把单集视频文件删到回收站。</summary>
    public AsyncRelayCommand DeleteEpisodeCommand { get; }

    // ─────────────── 状态 ───────────────

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                CardsView.Refresh();
                UpdateCounters();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                Raise(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !_isBusy;

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    public NavSection Section
    {
        get => _section;
        private set
        {
            if (SetProperty(ref _section, value))
            {
                Raise(nameof(IsAll), nameof(IsRecent), nameof(IsRoots), nameof(IsSettings),
                      nameof(IsLibraryView), nameof(PageTitle), nameof(EmptyHint));
                CardsView.Refresh();
                UpdateCounters();
            }
        }
    }

    public bool IsAll => _section == NavSection.All;
    public bool IsRecent => _section == NavSection.Recent;
    public bool IsRoots => _section == NavSection.Roots;
    public bool IsSettings => _section == NavSection.Settings;
    public bool IsLibraryView => _section is NavSection.All or NavSection.Recent;

    public string PageTitle => _section switch
    {
        NavSection.Recent => "最近播放",
        NavSection.Roots => "媒体库路径",
        NavSection.Settings => "设置",
        _ => "全部作品",
    };

    public string EmptyHint => _section switch
    {
        NavSection.Recent => "还没有播放记录",
        _ => "这条路上还没有鱼……啊不，还没有视频",
    };

    public CardSizePreset CardSize
    {
        get => _cardSize;
        private set
        {
            if (SetProperty(ref _cardSize, value))
            {
                Raise(nameof(CardWidth), nameof(CoverHeight), nameof(CardTitleSize), nameof(IsSmallCard),
                      nameof(IsMediumCard), nameof(IsLargeCard));
            }
        }
    }

    public bool IsSmallCard => _cardSize == CardSizePreset.Small;
    public bool IsMediumCard => _cardSize == CardSizePreset.Medium;
    public bool IsLargeCard => _cardSize == CardSizePreset.Large;

    public double CardWidth => _cardSize switch
    {
        CardSizePreset.Small => 168,
        CardSizePreset.Large => 288,
        _ => 216,
    };

    public double CoverHeight => CardWidth * 9 / 16;

    public double CardTitleSize => _cardSize switch
    {
        CardSizePreset.Small => 12.5,
        CardSizePreset.Large => 16,
        _ => 14,
    };

    public SeriesCardViewModel? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (SetProperty(ref _selectedCard, value))
            {
                Raise(nameof(HasSelection), nameof(SelectedTitle), nameof(SelectedCountText),
                      nameof(SelectedSeasonLabel), nameof(SelectedCoverPath), nameof(SelectedHasCover),
                      nameof(SelectedFolder), nameof(SelectedSearchKey));
                LoadEpisodes(value);
            }
        }
    }

    public bool HasSelection => _selectedCard is not null;
    public string SelectedTitle => _selectedCard?.Title ?? "";
    public string SelectedSeasonLabel => _selectedCard?.SeasonLabel ?? "";
    public string SelectedFolder => _selectedCard?.FolderPath ?? "";
    public string SelectedCoverPath => _selectedCard?.CoverPath ?? "";
    public bool SelectedHasCover => _selectedCard?.HasCover ?? false;
    public string SelectedSearchKey => _selectedCard?.Key ?? "";

    public string SelectedCountText => _selectedCard is null
        ? ""
        : $"{_selectedCard.Count} 集";

    public bool IsDetailOpen
    {
        get => _isDetailOpen;
        set => SetProperty(ref _isDetailOpen, value);
    }

    public int VisibleCount { get; private set; }
    public string CountText => $"{VisibleCount} / {Cards.Count}";
    public string TotalText => $"{_host.Settings.Roots.Count} 个路径 · {Cards.Sum(c => c.Count)} 个视频";

    public string SortMode
    {
        get => _sortMode;
        private set => SetProperty(ref _sortMode, value);
    }

    public bool SortDesc
    {
        get => _sortDesc;
        private set
        {
            if (SetProperty(ref _sortDesc, value))
            {
                Raise(nameof(SortArrow));
                CardsView.Refresh();
            }
        }
    }

    public string SortArrow => _sortDesc ? "↓" : "↑";

    // 设置页绑定
    public string FfmpegPathText
    {
        get => _host.Covers.FfmpegPath ?? _host.Settings.FfmpegPath ?? "";
        set
        {
            _host.Settings.FfmpegPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _host.Save();
            Raise();
        }
    }

    public string FfmpegStatus => _host.Covers.Available
        ? $"已就绪 · {_host.Covers.FfmpegPath}"
        : "未找到 ffmpeg —— 无法提取封面（装上 ffmpeg 或手动指定路径）";

    public bool FfmpegAvailable => _host.Covers.Available;

    public string LibraryPath => _host.Storage.DataDirectory;

    public string CoverCacheInfo
    {
        get
        {
            try
            {
                var dir = _host.Storage.CoverDirectory;
                var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.jpg") : [];
                var size = files.Sum(f => new FileInfo(f).Length);
                return $"{files.Length} 张封面 · {size / 1024.0 / 1024.0:0.#} MB";
            }
            catch
            {
                return "";
            }
        }
    }

    // ─────────────── 启动 / 扫描 ───────────────

    public async Task StartupAsync()
    {
        var settings = _host.Settings;

        // 解析规则变了（例如繁体转简体），旧缓存里的标题就过期了，必须重扫一次。
        if (settings.CacheVersion != AppSettings.CurrentCacheVersion)
        {
            _host.Storage.ClearCache();
            settings.CacheVersion = AppSettings.CurrentCacheVersion;
            settings.LastScanUtc = null;
            _host.Save();
        }

        // 首次运行：把三个参考路径预填进去也没意义，让用户自己导入。
        var cached = _host.Storage.LoadCache();
        if (cached.Count > 0)
        {
            ApplyCache(cached);
            HasScanned = true;
        }

        SyncRoots();

        // 缓存只是让界面先有内容可看，真正的文件状态每次启动都重新扫一遍，
        // 免得用户在外面删了、加了视频却看不到变化。
        if (settings.Roots.Count > 0)
        {
            await ScanAsync();
        }
        else
        {
            StatusText = cached.Count > 0
                ? $"共 {Cards.Count} 部作品"
                : "还没有添加路径 —— 点「+ 添加路径」开始";
        }
    }

    public bool HasScanned
    {
        get => _hasScanned;
        private set => SetProperty(ref _hasScanned, value);
    }

    private void ApplyCache(IReadOnlyList<CachedSeries> cached)
    {
        Cards.Clear();
        foreach (var cs in cached)
        {
            if (cs.Files.Count == 0) continue;
            var files = cs.Files.Select(f => new MediaFile
            {
                FullPath = f.FullPath,
                FileName = f.FileName,
                FolderPath = f.FolderPath,
                RootPath = f.RootPath,
                ShowTitle = f.ShowTitle,
                EpisodeTitle = f.EpisodeTitle,
                Season = f.Season,
                Episode = f.Episode,
                SpecialNumber = f.SpecialNumber,
                IsSpecial = f.IsSpecial,
                ReleaseGroup = f.ReleaseGroup,
                SizeBytes = f.SizeBytes,
                ModifiedUtc = f.ModifiedUtc,
            }).ToList();

            files.Sort(LibraryScanner.CompareFiles);

            var title = _host.Settings.TitleOverrides.GetValueOrDefault(cs.Key, cs.Title);
            var series = new Series
            {
                Key = cs.Key,
                Title = title,
                OriginalTitle = cs.Title,
                RootPath = cs.RootPath,
                FolderPath = cs.FolderPath,
                CoverPath = cs.CoverPath ?? _host.Storage.CoverPathFor(cs.Key),
                CustomCover = _host.Settings.CoverOverrides.GetValueOrDefault(cs.Key),
                SeasonLabel = BuildSeasonLabel(files),
            };
            series.Files.AddRange(files);

            Cards.Add(new SeriesCardViewModel(series));
        }

        CardsView.Refresh();
        UpdateCounters();
    }

    private static string BuildSeasonLabel(List<MediaFile> files)
    {
        var seasons = files.Where(f => f.Season is > 0).Select(f => f.Season!.Value).Distinct().OrderBy(s => s).ToList();
        if (seasons.Count == 0) return files.Count > 0 ? $"{files.Count} 集" : "";
        if (seasons.Count == 1) return $"S{seasons[0]} · {files.Count} 集";
        return $"S{seasons[0]}–S{seasons[^1]} · {files.Count} 集";
    }

    public async Task RefreshAsync()
    {
        await ScanAsync();
    }

    public async Task ScanAsync()
    {
        if (IsBusy) return;

        var enabled = _host.Settings.Roots.Where(r => r.Enabled).ToList();
        if (enabled.Count == 0)
        {
            StatusText = "没有启用的路径";
            return;
        }

        IsBusy = true;
        IsIndeterminate = true;
        ProgressValue = 0;
        StatusText = "正在翻找硬盘……";

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                StatusText = p.Message;
                if (p.Total > 1)
                {
                    IsIndeterminate = false;
                    ProgressValue = p.Done * 100.0 / p.Total;
                }
            });

            var result = await Task.Run(() => LibraryScanner.Scan(enabled, progress));

            Cards.Clear();
            Episodes.Clear();
            SelectedCard = null;
            IsDetailOpen = false;

            foreach (var s in result.Series)
            {
                var titleOverride = _host.Settings.TitleOverrides.GetValueOrDefault(s.Key);
                if (titleOverride is not null) s.Title = titleOverride;
                s.CoverPath = _host.Storage.CoverPathFor(s.Key);
                if (_host.Settings.CoverOverrides.TryGetValue(s.Key, out var custom))
                {
                    s.CustomCover = custom;
                }
                Cards.Add(new SeriesCardViewModel(s));
            }

            CardsView.Refresh();
            HasScanned = true;
            UpdateCounters();

            _host.Settings.LastScanUtc = DateTime.UtcNow;
            SaveCache();
            SyncRoots();

            StatusText = $"找到 {Cards.Count} 部作品，共 {result.FileCount} 个视频（{result.Elapsed.TotalSeconds:0.0}s）";

            IsIndeterminate = false;
            ProgressValue = 100;

            StartCoverPipeline();
        }
        catch (Exception ex)
        {
            StatusText = $"扫描出错：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SaveCache()
    {
        var cached = Cards.Select(c => new CachedSeries
        {
            Key = c.Key,
            Title = c.Title,
            RootPath = c.RootPath,
            FolderPath = c.FolderPath,
            CoverPath = c.CoverPath,
            Files = c.Series.Files.Select(f => new CachedFile
            {
                FullPath = f.FullPath,
                FileName = f.FileName,
                FolderPath = f.FolderPath,
                RootPath = f.RootPath,
                ShowTitle = f.ShowTitle,
                EpisodeTitle = f.EpisodeTitle,
                Season = f.Season,
                Episode = f.Episode,
                SpecialNumber = f.SpecialNumber,
                IsSpecial = f.IsSpecial,
                ReleaseGroup = f.ReleaseGroup,
                SizeBytes = f.SizeBytes,
                ModifiedUtc = f.ModifiedUtc,
            }).ToList(),
        }).ToList();

        _host.Storage.SaveCache(cached);
        _host.Save();
    }

    // ─────────────── 封面 ───────────────

    /// <summary>为缺少封面的剧集生成封面（取自每部剧的第一个视频）。</summary>
    private void StartCoverPipeline()
    {
        _coverCts?.Cancel();
        _coverCts = new CancellationTokenSource();
        var token = _coverCts.Token;

        var svc = _host.Covers;
        if (!svc.Available)
        {
            StatusText += " · 未找到 ffmpeg，跳过封面提取";
            return;
        }

        var pending = Cards.Where(c => !c.HasCover && c.FirstFile is not null).ToList();
        foreach (var c in pending)
        {
            c.CoverPending = true;
        }

        if (pending.Count == 0) return;

        _ = Task.Run(async () =>
        {
            var done = 0;
            var failed = 0;
            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6),
                CancellationToken = token,
            };

            try
            {
                await Parallel.ForEachAsync(pending, parallel, async (card, ct) =>
                {
                    var target = _host.Storage.CoverPathFor(card.Key);
                    var first = card.FirstFile!;
                    var ok = File.Exists(target) || await Task.Run(
                        () => svc.ExtractCover(first.FullPath, target, _host.Settings.CoverWidth,
                            _host.Settings.CoverFrameRatio, ct), ct);

                    if (ok)
                    {
                        card.Series.CoverPath = target;
                        await _dispatcher.InvokeAsync(() => card.UpdateCover(target));
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                        await _dispatcher.InvokeAsync(() => card.CoverPending = false);
                    }

                    var n = Interlocked.Increment(ref done);
                    if (n % 3 == 0 || n == pending.Count)
                    {
                        await _dispatcher.InvokeAsync(() =>
                        {
                            StatusText = $"正在从视频里抓封面 {n}/{pending.Count} …";
                            ProgressValue = n * 100.0 / pending.Count;
                        });
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // 用户重新扫描或关闭程序
            }
            catch (Exception ex)
            {
                await _dispatcher.InvokeAsync(() => StatusText = $"封面提取出错：{ex.Message}");
                return;
            }

            if (token.IsCancellationRequested) return;

            await _dispatcher.InvokeAsync(() =>
            {
                StatusText = failed == 0
                    ? $"全部就绪 · {Cards.Count} 部作品"
                    : $"{Cards.Count} 部作品（{failed} 张封面没抓到）";
                ProgressValue = 100;
                Raise(nameof(CoverCacheInfo));
                SaveCache();
            });
        }, token);
    }

    private async Task<bool> EnsureCoverAsync(SeriesCardViewModel? card, bool force)
    {
        if (card?.FirstFile is null) return false;
        var svc = _host.Covers;
        if (!svc.Available)
        {
            StatusText = "没有 ffmpeg，抓不了封面";
            return false;
        }

        var target = _host.Storage.CoverPathFor(card.Key);

        // 重抓时换一个时间点：同一个视频固定抽 ratio 位置必然得到同一帧。
        var variant = 0;
        if (force)
        {
            variant = Interlocked.Increment(ref _coverVariant) & 0xFFFF;
            if (File.Exists(target))
            {
                try { File.Delete(target); } catch { }
            }
        }

        StatusText = force ? $"正在换个时间点重抓「{card.Title}」的封面……" : $"正在抓「{card.Title}」的封面……";
        var ok = await Task.Run(() => svc.ExtractCover(card.FirstFile.FullPath, target,
            _host.Settings.CoverWidth, _host.Settings.CoverFrameRatio, default, variant));

        if (ok)
        {
            // 同一个文件路径写入了新画面，WPF 的位图缓存会继续显示旧图，必须先失效。
            ImagePathToSourceConverter.Invalidate(target);
            card.Series.CoverPath = target;
            card.UpdateCover(target);
            card.RefreshCover();
            StatusText = "封面换好了";
            _dispatcher.Invoke(() => Raise(nameof(SelectedCoverPath), nameof(SelectedHasCover), nameof(CoverCacheInfo)));
            SaveCache();
        }
        else
        {
            StatusText = "这个视频抽不出画面……";
        }

        return ok;
    }

    private async Task RegenerateCoverAsync(SeriesCardViewModel? card)
    {
        if (card is null) return;
        await EnsureCoverAsync(card, force: true);
    }

    private async Task SetCustomCoverAsync(SeriesCardViewModel? card)
    {
        if (card is null) return;

        var dlg = new OpenFileDialog
        {
            Title = "选一张封面图",
            Filter = "图片|*.jpg;*.jpeg;*.png;*.webp;*.bmp|所有文件|*.*",
        };

        if (dlg.ShowDialog() != true) return;

        var src = dlg.FileName;
        var ext = Path.GetExtension(src);
        var target = Path.Combine(_host.Storage.CoverDirectory,
            VideoManager.Core.Util.Hashing.ShortSha1(card.Key + "|custom", 20) + ext);

        try
        {
            File.Copy(src, target, overwrite: true);
        }
        catch (Exception ex)
        {
            StatusText = $"复制封面失败：{ex.Message}";
            return;
        }

        _host.Settings.CoverOverrides[card.Key] = target;
        _host.Save();
        card.Series.CustomCover = target;
        card.UpdateCover(target);
        Raise(nameof(SelectedCoverPath), nameof(SelectedHasCover), nameof(CoverCacheInfo));
        StatusText = "封面已替换";
        await Task.CompletedTask;
    }

    private async Task ClearCoversAsync()
    {
        _coverCts?.Cancel();
        await Task.Run(() => _host.Storage.ClearCovers());
        foreach (var c in Cards)
        {
            c.UpdateCover(null);
            c.Series.CoverPath = null;
        }
        _host.Settings.CoverOverrides.Clear();
        _host.Save();
        Raise(nameof(CoverCacheInfo));
        StatusText = "封面缓存清空了";
        StartCoverPipeline();
    }

    // ─────────────── 播放 ───────────────

    private async Task PlaySeriesAsync(SeriesCardViewModel? card, bool playFirstOnly)
    {
        if (card is null) return;
        var file = card.FirstFile;
        if (file is null)
        {
            StatusText = "这部没有视频文件";
            return;
        }

        PlayEpisodeFile(file);
        await Task.CompletedTask;
    }

    private void PlayEpisode(EpisodeItemViewModel? ep)
    {
        if (ep is null) return;
        PlayEpisodeFile(ep.File);
    }

    private void PlayEpisodeFile(MediaFile file)
    {
        if (!File.Exists(file.FullPath))
        {
            StatusText = "文件不见了……";
            return;
        }

        var ok = PlayerLauncher.Play(file.FullPath);
        StatusText = ok ? $"正在播放 {file.DisplayName}" : "没能叫起播放器";

        // 只记「最近打开」，不记「已看」：外部播放器可能自动连播下一集，
        // 我们无法知道用户真正看到了哪，标错了就是撒谎。已看交给手动 ✓。
        _host.Settings.Recent.RemoveAll(p => string.Equals(p, file.FullPath, StringComparison.OrdinalIgnoreCase));
        _host.Settings.Recent.Insert(0, file.FullPath);
        if (_host.Settings.Recent.Count > 60)
        {
            _host.Settings.Recent.RemoveRange(60, _host.Settings.Recent.Count - 60);
        }
        _host.Save();

        if (IsRecent) CardsView.Refresh();
    }

    private void MarkWatched(EpisodeItemViewModel? ep)
    {
        if (ep is null) return;
        ep.Watched = !ep.Watched;
        if (ep.Watched)
        {
            _host.Settings.Watched[ep.FullPath] = DateTime.UtcNow.Ticks;
        }
        else
        {
            _host.Settings.Watched.Remove(ep.FullPath);
        }
        _host.Save();
    }

    // ─────────────── 删除到回收站 ───────────────

    private async Task DeleteEpisodeAsync(EpisodeItemViewModel? ep)
    {
        if (ep is null) return;

        var path = ep.FullPath;
        if (!File.Exists(path))
        {
            StatusText = "文件已经不在硬盘上了，我顺手把它从列表里清掉";
            RemoveEpisodesFromLibrary([path]);
            return;
        }

        var owner = Application.Current?.MainWindow;
        var card = SelectedCard;
        var detail = $"{ep.DisplayName}\n{ep.FileName}";
        if (!ConfirmDialog.Ask(owner, "把这一集删到回收站？", detail))
        {
            StatusText = "那就先留着";
            return;
        }

        var ok = await Task.Run(() => RecycleBin.MoveToRecycleBin([path]));
        if (!ok.Success || File.Exists(path))
        {
            var why = RecycleBin.DescribeError(ok.ErrorCode);
            StatusText = string.IsNullOrEmpty(why) ? "删除没成功，文件还在" : $"删除没成功：{why}";
            return;
        }

        RemoveEpisodesFromLibrary([path]);
        StatusText = $"「{ep.DisplayName}」进回收站了";

        // 这一部已经一集不剩了，卡片也一起收走
        if (card is not null && card.Series.Files.Count == 0)
        {
            Cards.Remove(card);
            IsDetailOpen = false;
            SelectedCard = null;
            CardsView.Refresh();
            UpdateCounters();
        }
    }

    private async Task DeleteSeriesAsync(SeriesCardViewModel? card)
    {
        if (card is null) return;

        var files = card.Series.Files.Select(f => f.FullPath).ToList();
        if (files.Count == 0)
        {
            StatusText = "这部本来就没有文件";
            return;
        }

        var owner = Application.Current?.MainWindow;
        var headline = $"把《{card.Title}》整个删到回收站？";
        var detail = $"一共 {files.Count} 个视频文件\n{card.FolderPath}";
        if (!ConfirmDialog.Ask(owner, headline, detail, $"删掉这 {files.Count} 个"))
        {
            StatusText = "那就先留着";
            return;
        }

        StatusText = $"正在把 {files.Count} 个文件送进回收站……";
        var result = await Task.Run(() => RecycleBin.MoveToRecycleBin(files));

        // 以「文件还在不在」为准：SHFileOperation 有时返回非零但已经删掉了一部分
        var leftover = files.Where(File.Exists).ToList();
        var deleted = files.Count - leftover.Count;

        if (deleted == 0)
        {
            var why = RecycleBin.DescribeError(result.ErrorCode);
            StatusText = string.IsNullOrEmpty(why) ? "一个也没删掉" : $"一个也没删掉：{why}";
            return;
        }

        RemoveEpisodesFromLibrary(files.Where(p => !File.Exists(p)).ToList());

        if (leftover.Count == 0)
        {
            Cards.Remove(card);
            SelectedCard = null;
            IsDetailOpen = false;
            CardsView.Refresh();
            UpdateCounters();
            StatusText = $"《{card.Title}》的 {deleted} 个文件进回收站了";
        }
        else
        {
            StatusText = $"删掉 {deleted} 个，还有 {leftover.Count} 个没删掉（可能正被播放器占用）";
        }
    }

    /// <summary>把已经删除的文件从内存里的作品 / 单集列表里摘掉，并同步缓存。</summary>
    private void RemoveEpisodesFromLibrary(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return;

        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

        foreach (var card in Cards.ToList())
        {
            var removed = card.Series.Files.RemoveAll(f => set.Contains(f.FullPath));
            if (removed == 0) continue;

            card.UpdateCount(card.Series.Files.Count);
        }

        for (var i = Episodes.Count - 1; i >= 0; i--)
        {
            if (set.Contains(Episodes[i].FullPath))
            {
                Episodes.RemoveAt(i);
            }
        }

        foreach (var p in set)
        {
            _host.Settings.Watched.Remove(p);
            _host.Settings.Recent.RemoveAll(r => string.Equals(r, p, StringComparison.OrdinalIgnoreCase));
        }

        // 详情抽屉里的集数、季标签跟着变；首集也可能换人了
        if (_selectedCard is not null)
        {
            Raise(nameof(SelectedCountText), nameof(SelectedSeasonLabel),
                  nameof(SelectedCoverPath), nameof(SelectedHasCover));
        }

        UpdateCounters();
        SaveCache();
    }

    // ─────────────── 详情 ───────────────

    private void OpenSeries(SeriesCardViewModel? card)
    {
        if (card is null) return;
        SelectedCard = card;
        IsDetailOpen = true;
    }

    private void LoadEpisodes(SeriesCardViewModel? card)
    {
        Episodes.Clear();
        if (card is null) return;

        foreach (var f in card.Series.Files)
        {
            var watched = _host.Settings.Watched.ContainsKey(f.FullPath);
            Episodes.Add(new EpisodeItemViewModel(f, watched));
        }
    }

    // ─────────────── 路径管理 ───────────────

    private async Task AddRootAsync()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "把这个文件夹加进媒体库（里面放视频就行）",
            Multiselect = false,
        };

        if (dlg.ShowDialog() != true) return;
        AddRootPaths([dlg.FolderName]);
        await ScanAsync();
    }

    /// <summary>可由外部（拖拽 / 命令行）直接加一批路径。</summary>
    public async Task AddRootPathsAsync(IEnumerable<string> paths)
    {
        var list = paths.Where(Directory.Exists).ToList();
        if (list.Count == 0) return;
        AddRootPaths(list);
        await ScanAsync();
    }

    private void AddRootPaths(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || !Directory.Exists(p)) continue;
            var full = Path.GetFullPath(p);
            if (_host.Settings.Roots.Any(r => string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase))) continue;
            _host.Settings.Roots.Add(LibraryRoot.Create(full));
            added++;
        }

        if (added == 0) return;
        _host.Save();
        SyncRoots();
        StatusText = $"加了 {added} 个路径";
        Raise(nameof(TotalText));
    }

    private void RemoveRoot(RootItemViewModel? item)
    {
        if (item is null) return;
        _host.Settings.Roots.RemoveAll(r => string.Equals(r.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        _host.Save();
        SyncRoots();
        Raise(nameof(TotalText));
        StatusText = $"已移除 {item.Name}";

        // 顺手把已扫描的条目清掉，避免显示不存在的路径
        foreach (var card in Cards.Where(c => string.Equals(c.RootPath, item.Path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Cards.Remove(card);
        }
        CardsView.Refresh();
        UpdateCounters();
    }

    private void SyncRoots()
    {
        Roots.Clear();
        foreach (var root in _host.Settings.Roots)
        {
            var count = Cards.Count(c => string.Equals(c.RootPath, root.Path, StringComparison.OrdinalIgnoreCase));
            var item = new RootItemViewModel(root, count)
            {
                Status = Directory.Exists(root.Path) ? (root.Enabled ? "已启用" : "已停用") : "路径不存在",
            };
            item.Toggled += (_, _) =>
            {
                _host.Save();
                item.Status = item.Enabled ? "已启用" : "已停用";
            };
            Roots.Add(item);
        }
        Raise(nameof(TotalText), nameof(LibraryPath));
    }

    private void Reveal(object? parameter)
    {
        var path = parameter switch
        {
            EpisodeItemViewModel e => e.FullPath,
            SeriesCardViewModel c => c.FirstFile?.FullPath ?? c.FolderPath,
            RootItemViewModel r => r.Path,
            string s => s,
            _ => SelectedCard?.FolderPath,
        };

        if (path is not null) PlayerLauncher.RevealInExplorer(path);
    }

    private void RevealPath(string path) => PlayerLauncher.RevealInExplorer(path);

    // ─────────────── 排序 / 过滤 / 布局 ───────────────

    private void Navigate(object? parameter)
    {
        if (parameter is string s && Enum.TryParse<NavSection>(s, out var section))
        {
            Section = section;
            if (section is NavSection.All or NavSection.Recent) IsDetailOpen = false;
        }
    }

    private void ChangeSort(string? mode)
    {
        if (string.IsNullOrEmpty(mode)) return;
        if (SortMode == mode)
        {
            SortDesc = !SortDesc;
        }
        else
        {
            SortMode = mode;
            SortDesc = mode == "集数";
        }

        CardsView.SortDescriptions.Clear();
        switch (mode)
        {
            case "集数":
                CardsView.SortDescriptions.Add(new SortDescription(nameof(SeriesCardViewModel.Count), SortDesc ? ListSortDirection.Descending : ListSortDirection.Ascending));
                break;
            case "文件夹":
                CardsView.SortDescriptions.Add(new SortDescription(nameof(SeriesCardViewModel.Subtitle), SortDesc ? ListSortDirection.Descending : ListSortDirection.Ascending));
                break;
            default:
                CardsView.SortDescriptions.Add(new SortDescription(nameof(SeriesCardViewModel.Title), SortDesc ? ListSortDirection.Descending : ListSortDirection.Ascending));
                break;
        }

        CardsView.Refresh();
    }

    private void SetCardSize(object? parameter)
    {
        if (parameter is string s && Enum.TryParse<CardSizePreset>(s, out var preset))
        {
            CardSize = preset;
            _host.Settings.CardSize = preset.ToString();
            _host.Save();
        }
    }

    private bool FilterCard(object obj)
    {
        if (obj is not SeriesCardViewModel card) return false;

        if (_section == NavSection.Recent)
        {
            var recentSet = _host.Settings.Recent;
            if (recentSet.Count == 0) return false;
            return card.Series.Files.Any(f => recentSet.Contains(f.FullPath, StringComparer.OrdinalIgnoreCase));
        }

        if (_searchText.Length == 0) return true;

        var q = _searchText.Trim();
        if (q.Length == 0) return true;

        // 多个关键词空格分隔，全部命中才算
        foreach (var token in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!card.SearchText.Contains(token.ToLowerInvariant(), StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private void UpdateCounters()
    {
        VisibleCount = CardsView.Cast<object>().Count();
        Raise(nameof(VisibleCount), nameof(CountText), nameof(TotalText));
    }

    private void ApplySettings()
    {
        if (Enum.TryParse<CardSizePreset>(_host.Settings.CardSize, out var preset))
        {
            _cardSize = preset;
        }

        CardsView.SortDescriptions.Clear();
        CardsView.SortDescriptions.Add(new SortDescription(nameof(SeriesCardViewModel.Title), ListSortDirection.Ascending));
    }

    private async Task ResetTitleAsync(SeriesCardViewModel? card)
    {
        if (card is null) return;
        _host.Settings.TitleOverrides.Remove(card.Key);
        _host.Save();
        card.UpdateTitle(card.Series.OriginalTitle);
        card.Series.Title = card.Series.OriginalTitle;
        Raise(nameof(SelectedTitle));
        CardsView.Refresh();
        SaveCache();
        await Task.CompletedTask;
    }

    /// <summary>用户在详情里改了标题。</summary>
    public void RenameSeries(SeriesCardViewModel card, string newTitle)
    {
        var t = newTitle.Trim();
        if (t.Length == 0 || t == card.Title) return;

        card.UpdateTitle(t);
        card.Series.Title = t;
        _host.Settings.TitleOverrides[card.Key] = t;
        _host.Save();
        Raise(nameof(SelectedTitle));
        CardsView.Refresh();
        SaveCache();
        StatusText = $"改名为「{t}」";
    }

    private void BrowseFfmpeg()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 ffmpeg.exe",
            Filter = "ffmpeg|ffmpeg.exe|可执行文件|*.exe",
        };

        if (dlg.ShowDialog() != true) return;
        _host.Settings.FfmpegPath = dlg.FileName;
        _host.Save();
        var svc = _host.RebuildCoverService();
        Raise(nameof(FfmpegPathText), nameof(FfmpegStatus), nameof(FfmpegAvailable), nameof(LibraryPath));
        StatusText = svc.Available ? "ffmpeg 接上了，封面可以抓了" : "这个路径里没有 ffmpeg";
    }
}
