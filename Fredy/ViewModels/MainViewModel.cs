using BLL;
using Common.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using Fredy.Drilling.Holes.Views;
using Fredy.Drilling.Holes.Windows.PunchAudit;
using Fredy.Drilling.Holes.Windows.Recipe;
using HAL;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fredy.Drilling.Holes.Windows.CustomPunchRange;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IAppLogExportService? _logExportService;
        private readonly IAppLogStore? _logStore;
        private readonly ILogger<MainViewModel>? _logger;
        private readonly ICamera? _camera;
        private readonly IHardwareController? _hardwareController;
        private readonly RecipeService? _recipeService;
        private readonly IHardwareStateService? _hardwareStateService;
        private readonly ISecondPassAlignmentContext? _secondPassAlignmentContext;
        private readonly IMotionService? _motionService;
        private readonly CoordinateService? _coordinateService;
        private readonly ConfigService? _configService;
        private CancellationTokenSource? _cameraPreviewCancellationTokenSource;
        private Task? _cameraPreviewTask;
        private PunchStateMachine? _punchStateMachine;
        private CancellationTokenSource? _punchingCancellationTokenSource;
        private Task? _punchingTask;
        private CancellationTokenSource? _temporaryPunchTestCancellationTokenSource;
        private Task? _temporaryPunchTestTask;
        private CancellationTokenSource? _resetCancellationTokenSource;
        private bool _disposed;
        private bool _isCameraPreviewSuspended;
        private PunchProcessAuditViewModel? _punchAuditViewModel;
        private PunchAuditWindow? _punchAuditWindow;
        private readonly Serilog.ILogger? _serilogLogger;
        private readonly HashSet<int> _completedPunchPointIndices = new();
        private List<int>? _activePunchPointIndices;
        private int _completedPunchPlanCount;
        private int? _customPunchStartIndex;
        private int? _customPunchEndIndex;

        [ObservableProperty] private MachineStatus _status = new();
        [ObservableProperty] private bool _isSimulate;
        [ObservableProperty] private bool _isFirstPass = true;
        [ObservableProperty] private bool _showVerbose = true;
        [ObservableProperty] private bool _showDebug = true;
        [ObservableProperty] private bool _showInformation = true;
        [ObservableProperty] private bool _showWarning = true;
        [ObservableProperty] private bool _showError = true;
        [ObservableProperty] private bool _showFatal = true;
        [ObservableProperty] private bool _isResetting;
        [ObservableProperty] private double _temporaryTestStartX;
        [ObservableProperty] private double _temporaryTestStartY;
        [ObservableProperty] private double _temporaryTestPunchX;
        [ObservableProperty] private double _temporaryTestPunchY;
        [ObservableProperty] private double _temporaryTestPunchStepDepth = 50d;
        [ObservableProperty] private string _temporaryPunchTestStatus = "待机";
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartTemporaryPunchTestCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopTemporaryPunchTestCommand))]
        private bool _isTemporaryPunchTestRunning;

        private ImageSource? _currentCameraImage;
        private RecipeViewModel? _currentRecipeViewModel;

        public ObservableCollection<string> RecipeNames { get; } = new();

        public IAsyncRelayCommand CustomPunchingCommand { get; }

        public int LogCapacity => _logStore?.Capacity ?? 0;

        public ICollectionView FilteredLogs { get; }

        public ReadOnlyObservableCollection<AppLogEntry> Logs { get; }

        public ImageSource? CurrentCameraImage
        {
            get => _currentCameraImage;
            set => SetProperty(ref _currentCameraImage, value);
        }

        public RecipeViewModel? CurrentRecipeViewModel
        {
            get => _currentRecipeViewModel;
            set
            {
                if (SetProperty(ref _currentRecipeViewModel, value))
                {
                    NotifyProcessCommandStateChanged();
                }
            }
        }

        public bool CanUseCustomAction => !IsPunchingActive && !IsTemporaryPunchTestRunning;

        public bool ShowPausePunchingButton => !IsPunchingPaused;

        public bool ShowContinuePunchingButton => IsPunchingPaused;

        public MainViewModel()
        {
            CustomPunchingCommand = new AsyncRelayCommand(CustomPunchingAsync, CanCustomPunching);
            Logs = new ReadOnlyObservableCollection<AppLogEntry>(new ObservableCollection<AppLogEntry>());
            FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
            FilteredLogs.Filter = FilterLogEntry;
            Status.PropertyChanged += Status_PropertyChanged;
        }

        public MainViewModel(
            IAppLogStore logStore,
            IAppLogExportService logExportService,
            ILogger<MainViewModel> logger,
            Serilog.ILogger serilogLogger,
            RecipeService recipeService,
            ICamera camera,
            IHardwareController hardwareController,
            IHardwareStateService hardwareStateService,
            ISecondPassAlignmentContext secondPassAlignmentContext,
            IMotionService motionService,
            CoordinateService coordinateService,
            ConfigService configService)
        {
            CustomPunchingCommand = new AsyncRelayCommand(CustomPunchingAsync, CanCustomPunching);
            _logStore = logStore;
            _logExportService = logExportService;
            _logger = logger;
            _serilogLogger = serilogLogger.ForContext<MainViewModel>();
            _recipeService = recipeService;
            _camera = camera;
            _hardwareController = hardwareController;
            _hardwareStateService = hardwareStateService;
            _secondPassAlignmentContext = secondPassAlignmentContext;
            _motionService = motionService;
            _coordinateService = coordinateService;
            _configService = configService;
            Logs = logStore.Entries;
            FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
            FilteredLogs.Filter = FilterLogEntry;
            Status.PropertyChanged += Status_PropertyChanged;
            _secondPassAlignmentContext.AlignmentChanged += SecondPassAlignmentContext_AlignmentChanged;
            ApplyHardwareState(hardwareStateService.CurrentState);
            _hardwareStateService.StateChanged += HardwareStateService_StateChanged;
            _ = _hardwareStateService.RefreshAsync();
            RefreshRecipes();
            StartCameraPreview();

            _logger.LogInformation("主界面视图模型已初始化");
        }

        partial void OnShowVerboseChanged(bool value) => FilteredLogs.Refresh();
        partial void OnShowDebugChanged(bool value) => FilteredLogs.Refresh();
        partial void OnShowInformationChanged(bool value) => FilteredLogs.Refresh();
        partial void OnShowWarningChanged(bool value) => FilteredLogs.Refresh();
        partial void OnShowErrorChanged(bool value) => FilteredLogs.Refresh();
        partial void OnShowFatalChanged(bool value) => FilteredLogs.Refresh();

        partial void OnIsFirstPassChanged(bool value)
        {
            if (CurrentRecipeViewModel is not null)
            {
                CurrentRecipeViewModel.IsFirstPass = value;
            }
            NotifyProcessCommandStateChanged();
        }

        [RelayCommand(CanExecute = nameof(CanStartPunching))]
        private async Task StartPunchingAsync()
        {
            await StartPunchingCoreAsync();
        }

        [RelayCommand(CanExecute = nameof(CanStartReset))]
        private async Task ResetAsync()
        {
            await ExecuteResetAsync("X/Y 复位", async token =>
            {
                await _motionService!.HomeXAsync(true, token);
                token.ThrowIfCancellationRequested();
                await _motionService.HomeYAsync(true, token);
            });
        }

        [RelayCommand(CanExecute = nameof(CanStartReset))]
        private Task ResetXAsync()
        {
            return ExecuteResetAsync("X 轴复位", token => _motionService!.HomeXAsync(true, token));
        }

        [RelayCommand(CanExecute = nameof(CanStartReset))]
        private Task ResetYAsync()
        {
            return ExecuteResetAsync("Y 轴复位", token => _motionService!.HomeYAsync(true, token));
        }

        [RelayCommand(CanExecute = nameof(CanStartReset))]
        private Task ResetZAsync()
        {
            return ExecuteResetAsync("Z 轴复位", token => _motionService!.HomeZAsync(true, token));
        }

        private bool CanStartReset() => !IsResetting && !IsPunchingActive && !IsTemporaryPunchTestRunning;

        private async Task ExecuteResetAsync(string resetName, Func<CancellationToken, Task> resetAction)
        {
            if (_motionService is null)
            {
                return;
            }

            _resetCancellationTokenSource = new CancellationTokenSource();
            IsResetting = true;
            NotifyResetCommandStateChanged();

            try
            {
                await resetAction(_resetCancellationTokenSource.Token);
                _logger?.LogInformation("{ResetName}完成", resetName);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("{ResetName}已取消", resetName);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "{ResetName}过程中发生错误", resetName);
            }
            finally
            {
                IsResetting = false;
                _resetCancellationTokenSource?.Dispose();
                _resetCancellationTokenSource = null;
                NotifyResetCommandStateChanged();
            }
        }

        private void NotifyResetCommandStateChanged()
        {
            ResetCommand.NotifyCanExecuteChanged();
            ResetXCommand.NotifyCanExecuteChanged();
            ResetYCommand.NotifyCanExecuteChanged();
            ResetZCommand.NotifyCanExecuteChanged();
            CancelResetCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanCancelReset))]
        private void CancelReset()
        {
            if (_motionService is null || _resetCancellationTokenSource is null) return;
            _motionService.StopAll();
            _resetCancellationTokenSource.Cancel();
        }

        private bool CanCancelReset() => IsResetting;

        [RelayCommand]
        private async Task EmergencyStopAsync()
        {
            if (_motionService is null) return;
            try
            {
                _resetCancellationTokenSource?.Cancel();
                await _motionService.EmergencyStopAllAsync().ConfigureAwait(false);
                _logger?.LogWarning("急停已触发");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "急停过程中发生错误");
            }
        }

        private async Task CustomPunchingAsync()
        {
            if (CurrentRecipeViewModel?.PunchPoints.Count is not > 0)
            {
                _logger?.LogWarning("未加载有效配方，无法执行自定义冲孔");
                return;
            }

            int totalCount = CurrentRecipeViewModel.PunchPoints.Count;
            int defaultStartIndex = Math.Clamp(_customPunchStartIndex ?? 1, 1, totalCount);
            int defaultEndIndex = Math.Clamp(_customPunchEndIndex ?? totalCount, defaultStartIndex, totalCount);

            var window = new CustomPunchRangeWindow(totalCount, defaultStartIndex, defaultEndIndex)
            {
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (window.ShowDialog() != true)
            {
                _logger?.LogInformation("用户取消了自定义冲孔区间设置");
                return;
            }

            _customPunchStartIndex = window.SelectedStartIndex;
            _customPunchEndIndex = window.SelectedEndIndex;
            await StartPunchingCoreAsync(_customPunchStartIndex, _customPunchEndIndex);
        }

        private async Task StartPunchingCoreAsync(int? customStartIndex = null, int? customEndIndex = null)
        {
            if (CurrentRecipeViewModel?.PunchPoints is not { Count: > 0 } punchPoints)
            {
                _logger?.LogWarning("未加载有效配方，无法启动冲孔流程");
                return;
            }

            var selectedPunchPointIndices = BuildPunchPointIndices(punchPoints.Count, customStartIndex, customEndIndex);
            if (selectedPunchPointIndices.Count == 0)
            {
                _logger?.LogWarning("未找到有效的自定义冲孔区间");
                return;
            }

            if (_punchingTask is { IsCompleted: false })
            {
                _logger?.LogWarning("冲孔流程正在执行中");
                return;
            }

            if (!IsFirstPass)
            {
                int total = selectedPunchPointIndices.Count;
                int matched = selectedPunchPointIndices.Count(index => _secondPassAlignmentContext?.MatchedPoints?.ContainsKey(index) == true);
                int unmatched = total - matched;

                if (unmatched > 0)
                {
                    var result = MessageBox.Show(
                        $"当前为二道冲孔模式。\n\n" +
                        $"总点数: {total}\n" +
                        $"已匹配: {matched}\n" +
                        $"不匹配: {unmatched}\n\n" +
                        $"未匹配的孔位将被跳过，是否继续启动？",
                        "二道冲孔确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                    {
                        _logger?.LogInformation("用户取消了二道冲孔启动");
                        return;
                    }
                }
            }

            var selectedPunchPoints = selectedPunchPointIndices
                .Select(index => punchPoints[index])
                .ToList();
            string rangeDescription = customStartIndex.HasValue && customEndIndex.HasValue
                ? $"自定义[{customStartIndex}, {customEndIndex}]"
                : "全配方";

            _logger?.LogInformation("收到启动冲孔命令，模式: {Mode}，工序: {Pass}，范围: {Range}", IsSimulate ? "模拟" : "实际", IsFirstPass ? "头道" : "二道", rangeDescription);

            ResetPunchProgress(selectedPunchPointIndices);
            ShowPunchAuditWindow(selectedPunchPoints, IsSimulate, IsFirstPass, rangeDescription);
            await Task.Yield();

            _punchingCancellationTokenSource?.Cancel();
            _punchingCancellationTokenSource = new CancellationTokenSource();

            _punchStateMachine = CreatePunchStateMachine();
            _punchStateMachine.HoleCoordinateResolver = holeIndex =>
            {
                int idx = Math.Clamp(holeIndex - 1, 0, selectedPunchPointIndices.Count - 1);
                int originalIndex = selectedPunchPointIndices[idx];
                
                if (!IsFirstPass && _secondPassAlignmentContext is { IsReady: true } && _secondPassAlignmentContext.MatchedPoints.TryGetValue(originalIndex, out var matchedTarget))
                {
                    return matchedTarget;
                }

                var p = punchPoints[originalIndex];
                return (p.X, p.Y);
            };
            _punchStateMachine.HoleCoordinateTransformer = (x, y) =>
            {
                if (_coordinateService is null) return (x, y);
                try
                {
                    var machine = _coordinateService.WorkpieceToMachine(new Point2D(x, y));
                    return (machine.X, machine.Y);
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogWarning("坐标转换失败（工件未校准），将使用原始坐标：{Message}", ex.Message);
                    return (x, y);
                }
            };
            // Debug模拟使用Mock硬件执行完整流程，避免跳过补偿计算与审计可视化
            _punchStateMachine.IsSimulationChecked = false;
            _punchStateMachine.IsDetectionEnabled = true;
            _punchStateMachine.StateChanged += PunchStateMachine_StateChanged;
            _punchStateMachine.MessageReported += PunchStateMachine_MessageReported;
            _punchStateMachine.CompensationSelected += PunchStateMachine_CompensationSelected;
            NotifyProcessCommandStateChanged();

            var processType = IsFirstPass ? PunchProcessType.FirstPass : PunchProcessType.SecondPass;
            _punchStateMachine.StartProcess(processType, CurrentRecipeViewModel.Recipe);

            try
            {
                _punchingTask = RunPunchingProcessAsync(CurrentRecipeViewModel, _punchStateMachine, processType, selectedPunchPointIndices, _punchingCancellationTokenSource.Token);
                await _punchingTask;
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("冲孔流程已响应取消请求");
            }
            finally
            {
                if (_punchStateMachine is not null)
                {
                    _punchStateMachine.StateChanged -= PunchStateMachine_StateChanged;
                    _punchStateMachine.MessageReported -= PunchStateMachine_MessageReported;
                    _punchStateMachine.CompensationSelected -= PunchStateMachine_CompensationSelected;
                    _punchAuditViewModel?.SetCompletionStatus(_punchStateMachine.CompletionStatus);
                }

                _punchingCancellationTokenSource?.Dispose();
                _punchingCancellationTokenSource = null;
                _punchingTask = null;
                NotifyProcessCommandStateChanged();
            }
        }

        [RelayCommand(CanExecute = nameof(CanStopPunching))]
        private void StopPunching()
        {
            _punchingCancellationTokenSource?.Cancel();
            _punchStateMachine?.CancelProcess();
            NotifyProcessCommandStateChanged();
            _logger?.LogWarning("收到停止冲孔命令");
        }

        [RelayCommand(CanExecute = nameof(CanStartTemporaryPunchTest))]
        private async Task StartTemporaryPunchTestAsync()
        {
            if (_motionService is null || _hardwareController is null || _configService is null)
            {
                _logger?.LogWarning("临时双点冲孔测试无法启动，运动、硬件控制器或配置服务未初始化");
                return;
            }

            var validationError = ValidateTemporaryPunchTestParameters();
            if (validationError is not null)
            {
                MessageBox.Show(validationError, "临时双点冲孔测试", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _temporaryPunchTestCancellationTokenSource?.Cancel();
            _temporaryPunchTestCancellationTokenSource?.Dispose();
            _temporaryPunchTestCancellationTokenSource = new CancellationTokenSource();
            IsTemporaryPunchTestRunning = true;
            TemporaryPunchTestStatus = "准备开始";
            NotifyProcessCommandStateChanged();

            try
            {
                _temporaryPunchTestTask = RunTemporaryPunchTestAsync(_temporaryPunchTestCancellationTokenSource.Token);
                await _temporaryPunchTestTask;
                TemporaryPunchTestStatus = "临时双点冲孔测试完成";
            }
            catch (OperationCanceledException)
            {
                TemporaryPunchTestStatus = "临时双点冲孔测试已停止";
                _logger?.LogWarning("临时双点冲孔测试已取消");
            }
            catch (Exception ex)
            {
                TemporaryPunchTestStatus = "临时双点冲孔测试失败";
                _logger?.LogError(ex, "临时双点冲孔测试过程中发生错误");
                MessageBox.Show(ex.Message, "临时双点冲孔测试", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _temporaryPunchTestCancellationTokenSource?.Dispose();
                _temporaryPunchTestCancellationTokenSource = null;
                _temporaryPunchTestTask = null;
                IsTemporaryPunchTestRunning = false;
                NotifyProcessCommandStateChanged();
            }
        }

        [RelayCommand(CanExecute = nameof(CanStopTemporaryPunchTest))]
        private async Task StopTemporaryPunchTestAsync()
        {
            if (_motionService is null)
            {
                return;
            }

            _temporaryPunchTestCancellationTokenSource?.Cancel();
            TemporaryPunchTestStatus = "正在停止临时双点冲孔测试";

            try
            {
                await _motionService.EmergencyStopAllAsync().ConfigureAwait(false);
                _logger?.LogWarning("已触发临时双点冲孔测试急停");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "停止临时双点冲孔测试时发生错误");
            }
        }

        [RelayCommand(CanExecute = nameof(CanPausePunching))]
        private void PausePunching()
        {
            _punchStateMachine?.PauseProcess();
            NotifyProcessCommandStateChanged();
            _logger?.LogInformation("收到暂停冲孔命令");
        }

        [RelayCommand(CanExecute = nameof(CanContinuePunching))]
        private void ContinuePunching()
        {
            _punchStateMachine?.ResumeProcess();
            NotifyProcessCommandStateChanged();
            _logger?.LogInformation("收到继续冲孔命令");
        }

        [RelayCommand(CanExecute = nameof(CanResetMachine))]
        private void ResetMachine()
        {
            _logger?.LogInformation("收到机台复位命令");
        }

        [RelayCommand]
        private void ClearLogs()
        {
            _logStore?.Clear();
            //_logger.LogInformation("日志已清空");
        }

        [RelayCommand]
        private async Task ExportLogsAsync()
        {
            if (_logExportService is null)
            {
                return;
            }

            var filePath = await _logExportService.ExportAsync(Logs);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger?.LogInformation("日志导出已取消");
                return;
            }

            _logger?.LogInformation("日志已导出到: {FilePath}", filePath);
        }

        [RelayCommand]
        private void RefreshRecipes()
        {
            RecipeNames.Clear();

            if (_recipeService is null)
            {
                CurrentRecipeViewModel = null;
                NotifyProcessCommandStateChanged();
                return;
            }

            var recipeNames = _recipeService.Recipes.Values
                .Select(x => x.RecipeName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var recipeName in recipeNames)
            {
                RecipeNames.Add(recipeName);
            }

            if (RecipeNames.Count == 0)
            {
                Status.WorkpieceType = string.Empty;
                CurrentRecipeViewModel = null;
                UpdateRecipeStatus();
                NotifyProcessCommandStateChanged();
                _logger?.LogWarning("未加载到任何配方名称");
                return;
            }

            var selectedRecipeName = Status.WorkpieceType;
            if (string.IsNullOrWhiteSpace(selectedRecipeName))
            {
                selectedRecipeName = _recipeService.CurrentRecipe?.RecipeName;
            }

            if (!RecipeNames.Any(x => string.Equals(x, selectedRecipeName, StringComparison.OrdinalIgnoreCase)))
            {
                selectedRecipeName = RecipeNames[0];
            }

            Status.WorkpieceType = selectedRecipeName ?? "unknown";
            LoadSelectedRecipe();
            _logger?.LogInformation("已刷新配方列表，共 {RecipeCount} 个", RecipeNames.Count);
        }

        [RelayCommand]
        private void Navigate(string winName)
        {
            Window? window = winName switch
            {
                "CameraPunchOffsetCalibration" => new CameraPunchOffsetCalibrationWindow(),
                "Config" => new ConfigWindow(),
                "PartScan" => new ScanWindow(),
                "WorkpieceCenterCalibration" => new WorkpieceCenterCalibrationWindow(),
                "RecipeWin" => new RecipeWindow(),
                _ => null
            };

            if (window is null)
            {
                _logger?.LogWarning("未识别的窗口名称: {WinName}", winName);
                MessageBox.Show($"未识别的窗口名称: {winName}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Owner = Application.Current?.MainWindow;

            SuspendCameraPreview();

            void HandleWindowClosed(object? sender, EventArgs args)
            {
                window.Closed -= HandleWindowClosed;
                ResumeCameraPreview();
            }

            window.Closed += HandleWindowClosed;

            try
            {
                window.Show();
                _logger?.LogInformation("弹出窗口: {WinName}", winName);
            }
            catch
            {
                window.Closed -= HandleWindowClosed;
                ResumeCameraPreview();
                throw;
            }
        }

        private bool FilterLogEntry(object item)
        {
            if (item is not AppLogEntry entry)
            {
                return false;
            }

            return entry.Level switch
            {
                Serilog.Events.LogEventLevel.Verbose => ShowVerbose,
                Serilog.Events.LogEventLevel.Debug => ShowDebug,
                Serilog.Events.LogEventLevel.Information => ShowInformation,
                Serilog.Events.LogEventLevel.Warning => ShowWarning,
                Serilog.Events.LogEventLevel.Error => ShowError,
                Serilog.Events.LogEventLevel.Fatal => ShowFatal,
                _ => true
            };
        }

        private void Status_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MachineStatus.WorkpieceType))
            {
                LoadSelectedRecipe();
                return;
            }

            if (e.PropertyName == nameof(MachineStatus.PunchedHoles))
            {
                UpdateRecipeStatus();
            }
        }

        private void LoadSelectedRecipe()
        {
            if (_recipeService is null || string.IsNullOrWhiteSpace(Status.WorkpieceType))
            {
                ResetPunchTracking();
                CurrentRecipeViewModel = null;
                UpdateRecipeStatus();
                NotifyProcessCommandStateChanged();
                return;
            }

            if (!_recipeService.Load(Status.WorkpieceType))
            {
                ResetPunchTracking();
                CurrentRecipeViewModel = null;
                UpdateRecipeStatus();
                NotifyProcessCommandStateChanged();
                _logger?.LogWarning("未找到配方: {RecipeName}", Status.WorkpieceType);
                return;
            }

            ResetPunchTracking();
            CurrentRecipeViewModel = _recipeService.CurrentRecipe is null ? null : new RecipeViewModel(_recipeService.CurrentRecipe);
            if (CurrentRecipeViewModel is not null)
            {
                CurrentRecipeViewModel.IsFirstPass = IsFirstPass;
                CurrentRecipeViewModel.MatchedPoints = _secondPassAlignmentContext?.MatchedPoints;
            }
            UpdateRecipeStatus();
            NotifyProcessCommandStateChanged();
        }

        private void UpdateRecipeStatus()
        {
            if (CurrentRecipeViewModel?.PunchPoints is not { Count: > 0 } punchPoints)
            {
                Status.TotalHoles = 0;
                Status.CurrentRing = 0;
                Status.CurrentHole = 0;
                Status.PunchedHoles = 0;
                Status.RemainingHoles = 0;
                return;
            }

            var activePunchPointIndices = _activePunchPointIndices is { Count: > 0 }
                ? _activePunchPointIndices
                : Enumerable.Range(0, punchPoints.Count).ToList();

            Status.TotalHoles = activePunchPointIndices.Count;

            var completedCount = Math.Clamp(_completedPunchPlanCount, 0, activePunchPointIndices.Count);
            if (completedCount != Status.PunchedHoles)
            {
                Status.PunchedHoles = completedCount;
                return;
            }

            CurrentRecipeViewModel.SetCompletedIndices(_completedPunchPointIndices);
            Status.RemainingHoles = Math.Max(0, activePunchPointIndices.Count - completedCount);

            if (completedCount >= activePunchPointIndices.Count)
            {
                var lastPoint = punchPoints[activePunchPointIndices[^1]];
                Status.CurrentRing = lastPoint.RingNumber;
                Status.CurrentHole = 0;
                return;
            }

            var nextPoint = punchPoints[activePunchPointIndices[completedCount]];
            Status.CurrentRing = nextPoint.RingNumber;
            Status.CurrentHole = nextPoint.SequenceIndex;
        }

        private PunchStateMachine CreatePunchStateMachine()
        {
            IHardwareController hardwareController = IsSimulate
                ? new MockHardwareController(_serilogLogger ?? Log.Logger)
                : _hardwareController ?? throw new InvalidOperationException("未注入实际硬件控制器。");

            var stateMachine = new PunchStateMachine(hardwareController, _serilogLogger ?? Log.Logger);
            var config = _configService?.CurrentConfig;
            stateMachine.HasInitialSurfaceReference = config?.HasWorkpieceReferenceZ == true;
            stateMachine.InitialSurfaceReferenceZ = stateMachine.HasInitialSurfaceReference
                ? config!.WorkpieceReferenceZ
                : 0d;
            stateMachine.FastApproachDistance = config?.FastMovePos ?? 0d;
            stateMachine.FastApproachSpeed = config?.FastMoveSpeed ?? 0d;
            stateMachine.SlowDetectDistance = config?.SlowMoveDist ?? 0d;
            stateMachine.SlowDetectSpeed = config?.SlowMoveSpeed ?? 0d;
            stateMachine.SafeZ = config?.PunchSafeZ ?? 0d;
            stateMachine.FastToSafeZSpeed = config?.FastToSafeZSpeed ?? 0d;
            stateMachine.PunchDownSpeed = config?.PunchDownSpeed ?? 0d;
            stateMachine.ReferenceProbeOffsetX = config?.SurfaceProbeOffsetX ?? -1d;
            stateMachine.ReferenceProbeOffsetY = config?.SurfaceProbeOffsetY ?? 0d;
            stateMachine.SurfaceDetectionOptions = new SurfaceDetectionOptions
            {
                Mode = ResolveSurfaceDetectionMode(config?.SurfaceDetectionMode),
                InputPort = config?.SurfaceDetectInputPort ?? 0,
                InputLowActive = config?.SurfaceDetectInputLowActive ?? true,
                PollIntervalMs = config?.SurfaceDetectPollIntervalMs ?? 10
            };
            return stateMachine;
        }

        private static SurfaceDetectionMode ResolveSurfaceDetectionMode(string? mode)
        {
            return Enum.TryParse<SurfaceDetectionMode>(mode, ignoreCase: true, out var result)
                ? result
                : SurfaceDetectionMode.Latch;
        }

        private async Task RunPunchingProcessAsync(RecipeViewModel recipeViewModel, PunchStateMachine punchStateMachine, PunchProcessType processType, IReadOnlyList<int> activePunchPointIndices, CancellationToken cancellationToken)
        {
            for (int index = 0; index < activePunchPointIndices.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int pointIndex = activePunchPointIndices[index];
                var point = recipeViewModel.PunchPoints[pointIndex];
                punchStateMachine.IsLastHole = index == activePunchPointIndices.Count - 1;

                if (processType == PunchProcessType.SecondPass 
                    && _secondPassAlignmentContext is { IsReady: true } 
                    && !_secondPassAlignmentContext.MatchedPoints.ContainsKey(pointIndex))
                {
                    _logger?.LogInformation("跳过未检测到的孔位: 圈 {RingNumber}, 序号 {SequenceIndex}",
                        point.RingNumber,
                        point.SequenceIndex);
                    
                    punchStateMachine.SkipCurrentHole();
                }
                else
                {
                    (double machX, double machY) = (point.X, point.Y);
                        if (_coordinateService is not null)
                        {
                            try
                            {
                                var mp = _coordinateService.WorkpieceToMachine(new Point2D(point.X, point.Y));
                                machX = mp.X;
                                machY = mp.Y;
                            }
                            catch (InvalidOperationException) { }
                        }

                        _logger?.LogInformation(
                            "开始冲孔位: 圈 {RingNumber}, 序号 {SequenceIndex}, 工件坐标 ({WX:F3}, {WY:F3}), 机械坐标 ({MX:F3}, {MY:F3})",
                            point.RingNumber,
                            point.SequenceIndex,
                            point.X,
                            point.Y,
                            machX,
                            machY);
                }

                while (punchStateMachine.CurrentState != PunchState.Finished
                    && punchStateMachine.CurrentHoleIndex == index + 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (punchStateMachine.CurrentState == PunchState.Paused)
                    {
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }

                    await Task.Run(() => punchStateMachine.ExecuteNextStep(processType), cancellationToken);
                    int stepDelayMs = IsSimulate ? Random.Shared.Next(100, 200) : 50;
                    await Task.Delay(stepDelayMs, cancellationToken);
                }

                if (punchStateMachine.CompletionStatus is PunchCompletionStatus.AbnormalFinished or PunchCompletionStatus.Cancelled)
                {
                    break;
                }

                _completedPunchPointIndices.Add(pointIndex);
                _completedPunchPlanCount = index + 1;
                UpdateRecipeStatus();
                _punchAuditViewModel?.MarkHoleCompleted(index + 1);

                if (punchStateMachine.CurrentState == PunchState.Finished)
                {
                    break;
                }
            }

            switch (punchStateMachine.CompletionStatus)
            {
                case PunchCompletionStatus.NormalFinished:
                    _logger?.LogInformation("配方冲孔流程正常结束");
                    break;
                case PunchCompletionStatus.AbnormalFinished:
                    _logger?.LogWarning("配方冲孔流程异常结束");
                    break;
                case PunchCompletionStatus.Cancelled:
                    _logger?.LogWarning("配方冲孔流程已取消");
                    break;
                default:
                    _logger?.LogInformation("配方冲孔流程结束");
                    break;
            }
        }

        private void PunchStateMachine_StateChanged(object? sender, StateChangedEventArgs e)
        {
            InvokeOnUiThread(() =>
            {
                NotifyProcessCommandStateChanged();
                _punchAuditViewModel?.OnStateChanged(e);
            });
        }

        private void PunchStateMachine_MessageReported(object? sender, MessageEventArgs e)
        {
            InvokeOnUiThread(() => _punchAuditViewModel?.OnMessage(e));
        }

        private void PunchStateMachine_CompensationSelected(object? sender, CompensationSelectedEventArgs e)
        {
            InvokeOnUiThread(() => _punchAuditViewModel?.OnCompensationSelected(e));
            _logger?.LogInformation("孔位#{HoleIndex}补偿={Compensation}, 最近邻距离={Distance}, 采样点数量={SampleCount}",
                e.HoleIndex,
                e.Compensation,
                e.NearestDistance,
                e.SampleCount);
        }

        private void ShowPunchAuditWindow(IReadOnlyList<PunchPointViewModel> selectedPunchPoints, bool isSimulation, bool isFirstPass, string rangeDescription)
        {
            _punchAuditViewModel ??= new PunchProcessAuditViewModel(_serilogLogger ?? Log.Logger);
            _punchAuditViewModel.Initialize(selectedPunchPoints, isSimulation, isFirstPass, rangeDescription);

            if (_punchAuditWindow is null || !_punchAuditWindow.IsLoaded)
            {
                _punchAuditWindow = new PunchAuditWindow(_punchAuditViewModel)
                {
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                _punchAuditWindow.Closed += (_, _) => _punchAuditWindow = null;
                _punchAuditWindow.Show();
            }
            else
            {
                _punchAuditWindow.DataContext = _punchAuditViewModel;
                _punchAuditWindow.Activate();
            }
        }

        private void HardwareStateService_StateChanged(object? sender, HardwareStateChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                ApplyHardwareState(e.State);
                return;
            }

            _ = dispatcher.InvokeAsync(() => ApplyHardwareState(e.State));
        }

        private void ApplyHardwareState(HardwareStateSnapshot state)
        {
            Status.PosX = state.X;
            Status.PosY = state.Y;
            Status.PosZ = state.Z;
            Status.IsMotionCardReady = state.IsMotionCardReady;
            Status.IsCameraConnected = state.IsCameraConnected;
        }

        private void SecondPassAlignmentContext_AlignmentChanged(object? sender, EventArgs e)
        {
            if (CurrentRecipeViewModel is not null)
            {
                CurrentRecipeViewModel.MatchedPoints = _secondPassAlignmentContext?.MatchedPoints;
            }
            NotifyProcessCommandStateChanged();
        }

        public void SuspendCameraPreview()
        {
            _isCameraPreviewSuspended = true;

            _cameraPreviewCancellationTokenSource?.Cancel();
            _cameraPreviewCancellationTokenSource = null;
            _cameraPreviewTask = null;
        }

        public void ResumeCameraPreview()
        {
            if (!_isCameraPreviewSuspended)
            {
                return;
            }

            _isCameraPreviewSuspended = false;
            StartCameraPreview();
        }

        private void StartCameraPreview()
        {
            if (_camera is null || _isCameraPreviewSuspended)
            {
                return;
            }

            _cameraPreviewCancellationTokenSource?.Cancel();
            _cameraPreviewCancellationTokenSource = new CancellationTokenSource();
        }

    
        private static BitmapSource? CreateBitmapSource(CameraArgs? frame)
        {
            if (frame?.Data is null || frame.Width <= 0 || frame.Height <= 0)
            {
                return null;
            }

            var pixelFormat = frame.Format switch
            {
                HAL.PixelFormat.Mono8 => PixelFormats.Gray8,
                HAL.PixelFormat.RGB8 => PixelFormats.Rgb24,
                HAL.PixelFormat.BGR8 => PixelFormats.Bgr24,
                HAL.PixelFormat.RGBA8 => PixelFormats.Bgra32,
                HAL.PixelFormat.BGRA8 => PixelFormats.Bgra32,
                _ => PixelFormats.Bgr24
            };

            var stride = frame.Stride > 0 ? frame.Stride : ((frame.Width * pixelFormat.BitsPerPixel) + 7) / 8;
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, pixelFormat, null, frame.Data, stride);
            bitmap.Freeze();
            return bitmap;
        }

        private bool CanStartPunching()
        {
            return CurrentRecipeViewModel?.PunchPoints.Count > 0
                && !IsPunchingActive
                && !IsTemporaryPunchTestRunning;
        }

        private bool CanCustomPunching()
        {
            return CurrentRecipeViewModel?.PunchPoints.Count > 0
                && CanUseCustomAction;
        }

        private bool CanPausePunching()
        {
            return IsPunchingActive && !IsPunchingPaused;
        }

        private bool CanContinuePunching()
        {
            return IsPunchingPaused;
        }

        private bool CanStopPunching()
        {
            return IsPunchingActive;
        }

        private bool CanResetMachine()
        {
            return !IsPunchingActive && !IsTemporaryPunchTestRunning;
        }

        private bool CanStartTemporaryPunchTest()
        {
            return !IsPunchingActive
                && !IsTemporaryPunchTestRunning
                && !IsResetting
                && _motionService is not null
                && _hardwareController is not null
                && _configService is not null;
        }

        private bool CanStopTemporaryPunchTest()
        {
            return IsTemporaryPunchTestRunning && _motionService is not null;
        }

        private bool IsPunchingActive => _punchingTask is { IsCompleted: false };

        private bool IsPunchingPaused => _punchStateMachine?.CurrentState == PunchState.Paused;

        private void NotifyProcessCommandStateChanged()
        {
            OnPropertyChanged(nameof(CanUseCustomAction));
            OnPropertyChanged(nameof(ShowPausePunchingButton));
            OnPropertyChanged(nameof(ShowContinuePunchingButton));
            StartPunchingCommand.NotifyCanExecuteChanged();
            CustomPunchingCommand.NotifyCanExecuteChanged();
            StartTemporaryPunchTestCommand.NotifyCanExecuteChanged();
            StopTemporaryPunchTestCommand.NotifyCanExecuteChanged();
            PausePunchingCommand.NotifyCanExecuteChanged();
            ContinuePunchingCommand.NotifyCanExecuteChanged();
            StopPunchingCommand.NotifyCanExecuteChanged();
            ResetMachineCommand.NotifyCanExecuteChanged();
            NotifyResetCommandStateChanged();
        }

        private static void InvokeOnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            _ = dispatcher.InvokeAsync(action);
        }

        private string? ValidateTemporaryPunchTestParameters()
        {
            if (_motionService is null || _hardwareController is null || _configService is null)
            {
                return "运动服务、硬件控制器或配置服务未初始化。";
            }

            if (double.IsNaN(TemporaryTestStartX) || double.IsInfinity(TemporaryTestStartX)
                || double.IsNaN(TemporaryTestStartY) || double.IsInfinity(TemporaryTestStartY)
                || double.IsNaN(TemporaryTestPunchX) || double.IsInfinity(TemporaryTestPunchX)
                || double.IsNaN(TemporaryTestPunchY) || double.IsInfinity(TemporaryTestPunchY))
            {
                return "临时测试点位必须是有效数值。";
            }

            if (TemporaryTestPunchStepDepth <= 0d || double.IsNaN(TemporaryTestPunchStepDepth) || double.IsInfinity(TemporaryTestPunchStepDepth))
            {
                return "单次下探高度必须大于 0。";
            }

            return null;
        }

        private async Task RunTemporaryPunchTestAsync(CancellationToken cancellationToken)
        {
            var config = _configService!.CurrentConfig;
            double safeZ = config.PunchSafeZ;
            double safeZSpeed = config.FastToSafeZSpeed;
            double punchDownSpeed = config.PunchDownSpeed;
            double singlePunchDepth = Math.Abs(TemporaryTestPunchStepDepth);
            double fastApproachDistance = config.FastMovePos;
            double fastApproachSpeed = config.FastMoveSpeed;
            double slowDetectDistance = config.SlowMoveDist;
            double slowDetectSpeed = config.SlowMoveSpeed;
            var detectionOptions = new SurfaceDetectionOptions
            {
                Mode = ResolveSurfaceDetectionMode(config.SurfaceDetectionMode),
                InputPort = config.SurfaceDetectInputPort,
                InputLowActive = config.SurfaceDetectInputLowActive,
                PollIntervalMs = config.SurfaceDetectPollIntervalMs
            };

            var points = new (string Name, double X, double Y)[]
            {
                ("第1点", TemporaryTestStartX, TemporaryTestStartY),
                ("第2点", TemporaryTestPunchX, TemporaryTestPunchY)
            };

            TemporaryPunchTestStatus = $"抬到全局 SafeZ={safeZ:F3}";
            await Task.Run(() => _hardwareController!.LiftZ(safeZ, safeZSpeed), cancellationToken);

            foreach (var point in points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TemporaryPunchTestStatus = $"移动到{point.Name}: X={point.X:F3}, Y={point.Y:F3}";
                _logger?.LogInformation("临时双点冲孔测试移动到 {PointName}: X={X:F3}, Y={Y:F3}", point.Name, point.X, point.Y);

                await Task.WhenAll(
                    _motionService!.MoveXAsync(point.X, ResolveAxisVelocity(_motionService.XAxis), true, cancellationToken),
                    _motionService.MoveYAsync(point.Y, ResolveAxisVelocity(_motionService.YAxis), true, cancellationToken));

                cancellationToken.ThrowIfCancellationRequested();
                TemporaryPunchTestStatus = $"{point.Name} 执行表面探测";
                _logger?.LogInformation("临时双点冲孔测试 {PointName} 开始表面探测: FastDistance={FastDistance:F3}, FastSpeed={FastSpeed:F3}, SlowDistance={SlowDistance:F3}, SlowSpeed={SlowSpeed:F3}, Mode={Mode}",
                    point.Name,
                    fastApproachDistance,
                    fastApproachSpeed,
                    slowDetectDistance,
                    slowDetectSpeed,
                    detectionOptions.Mode);

                var probeResult = await Task.Run(
                    () => _hardwareController!.ProbeSurface(safeZ, safeZSpeed, fastApproachDistance, fastApproachSpeed, slowDetectDistance, slowDetectSpeed, detectionOptions),
                    cancellationToken);

                if (!probeResult.Detected)
                {
                    throw new InvalidOperationException($"{point.Name} 在设定搜索距离内未检测到表面信号。");
                }

                double surfaceZ = probeResult.SurfaceZ;
                _logger?.LogInformation("临时双点冲孔测试 {PointName} 表面探测完成: SurfaceZ={SurfaceZ:F3}", point.Name, surfaceZ);

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() => _hardwareController!.LiftZ(safeZ, safeZSpeed), cancellationToken);

                for (int punchIndex = 1; punchIndex <= 3; punchIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double absoluteTargetZ = surfaceZ - singlePunchDepth * punchIndex;
                    TemporaryPunchTestStatus = $"{point.Name} 第{punchIndex}/3次下探，SurfaceZ={surfaceZ:F3}，TargetZ={absoluteTargetZ:F3}";
                    _logger?.LogInformation("临时双点冲孔测试 {PointName} 第{PunchIndex}/3次下探: SurfaceZ={SurfaceZ:F3}, StepDepth={StepDepth:F3}, TargetZ={TargetZ:F3}, SafeZ={SafeZ:F3}",
                        point.Name,
                        punchIndex,
                        surfaceZ,
                        singlePunchDepth,
                        absoluteTargetZ,
                        safeZ);

                    await Task.Run(() => _hardwareController!.PunchDown(absoluteTargetZ, isAbsoluteTarget: true, detectSurface: false, detectionOptions: null, speed: punchDownSpeed), cancellationToken);
                    await Task.Run(() => _hardwareController!.WaitForZStop(), cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Run(() => _hardwareController!.LiftZ(safeZ, safeZSpeed), cancellationToken);
                }
            }
        }

        private static double ResolveAxisVelocity(AxisParam axis)
        {
            return axis.Velocity > 0d ? axis.Velocity : 1d;
        }

        private static List<int> BuildPunchPointIndices(int totalCount, int? startIndex = null, int? endIndex = null)
        {
            if (totalCount <= 0)
            {
                return new List<int>();
            }

            int start = Math.Clamp(startIndex ?? 1, 1, totalCount);
            int end = Math.Clamp(endIndex ?? totalCount, 1, totalCount);

            if (end < start)
            {
                return new List<int>();
            }

            return Enumerable.Range(start - 1, end - start + 1).ToList();
        }

        private void ResetPunchProgress(IReadOnlyList<int> activePunchPointIndices)
        {
            _activePunchPointIndices = activePunchPointIndices.ToList();
            _completedPunchPointIndices.Clear();
            _completedPunchPlanCount = 0;
            CurrentRecipeViewModel?.SetCompletedIndices(Array.Empty<int>());
            UpdateRecipeStatus();
        }

        private void ResetPunchTracking()
        {
            _activePunchPointIndices = null;
            _completedPunchPointIndices.Clear();
            _completedPunchPlanCount = 0;
            _customPunchStartIndex = null;
            _customPunchEndIndex = null;
            Status.PunchedHoles = 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Status.PropertyChanged -= Status_PropertyChanged;

            if (_hardwareStateService is not null)
            {
                _hardwareStateService.StateChanged -= HardwareStateService_StateChanged;
            }

            if (_secondPassAlignmentContext is not null)
            {
                _secondPassAlignmentContext.AlignmentChanged -= SecondPassAlignmentContext_AlignmentChanged;
            }

            _cameraPreviewCancellationTokenSource?.Cancel();
            _temporaryPunchTestCancellationTokenSource?.Cancel();
            _temporaryPunchTestCancellationTokenSource?.Dispose();
            _temporaryPunchTestCancellationTokenSource = null;
            if (_punchAuditWindow is { IsLoaded: true })
            {
                _punchAuditWindow.Close();
            }
            GC.SuppressFinalize(this);
        }
    }
}