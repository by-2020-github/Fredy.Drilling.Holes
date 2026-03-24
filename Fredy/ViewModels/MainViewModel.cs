using BLL;
using Common.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using Fredy.Drilling.Holes.Views;
using Fredy.Drilling.Holes.Windows.Recipe;
using HAL;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IAppLogExportService? _logExportService;
        private readonly IAppLogStore? _logStore;
        private readonly ILogger<MainViewModel>? _logger;
        private readonly ICamera? _camera;
        private readonly RecipeService? _recipeService;
        private CancellationTokenSource? _cameraPreviewCancellationTokenSource;
        private Task? _cameraPreviewTask;
        private PunchStateMachine? _punchStateMachine;
        private CancellationTokenSource? _punchingCancellationTokenSource;
        private Task? _punchingTask;

        [ObservableProperty] private MachineStatus _status = new();
        [ObservableProperty] private bool _isSimulate;
        [ObservableProperty] private bool _isFirstPass = true; // 默认选中头道
        [ObservableProperty] private bool _onlyShowWarningsAndErrors;

        private ImageSource? _currentCameraImage;
        private RecipeViewModel? _currentRecipeViewModel;

        public ObservableCollection<string> RecipeNames { get; } = new();

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

        public bool CanUseCustomAction => !IsPunchingActive;

        public bool ShowPausePunchingButton => !IsPunchingPaused;

        public bool ShowContinuePunchingButton => IsPunchingPaused;

        public MainViewModel()
        {
            Logs = new ReadOnlyObservableCollection<AppLogEntry>(new ObservableCollection<AppLogEntry>());
            FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
            FilteredLogs.Filter = FilterLogEntry;
            Status.PropertyChanged += Status_PropertyChanged;
        }

        public MainViewModel(IAppLogStore logStore, IAppLogExportService logExportService, ILogger<MainViewModel> logger, RecipeService recipeService, ICamera camera)
        {
            _logStore = logStore;
            _logExportService = logExportService;
            _logger = logger;
            _recipeService = recipeService;
            _camera = camera;
            Logs = logStore.Entries;
            FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
            FilteredLogs.Filter = FilterLogEntry;
            Status.PropertyChanged += Status_PropertyChanged;
            RefreshRecipes();
            StartCameraPreview();

            _logger.LogInformation("主界面视图模型已初始化");
        }

        partial void OnOnlyShowWarningsAndErrorsChanged(bool value)
        {
            FilteredLogs.Refresh();
            //_logger.LogInformation("日志筛选已切换为: {FilterMode}", value ? "仅看告警/错误" : "显示全部");
        }

        [RelayCommand(CanExecute = nameof(CanStartPunching))]
        private async Task StartPunchingAsync()
        {
            if (CurrentRecipeViewModel?.PunchPoints is not { Count: > 0 } punchPoints)
            {
                _logger?.LogWarning("未加载有效配方，无法启动冲孔流程");
                return;
            }

            if (_punchingTask is { IsCompleted: false })
            {
                _logger?.LogWarning("冲孔流程正在执行中");
                return;
            }

            _logger?.LogInformation("收到启动冲孔命令，模式: {Mode}，工序: {Pass}", IsSimulate ? "模拟" : "实际", IsFirstPass ? "头道" : "二道");

            Status.PunchedHoles = 0;
            CurrentRecipeViewModel.UpdateCompletedCount(0);

            _punchingCancellationTokenSource?.Cancel();
            _punchingCancellationTokenSource = new CancellationTokenSource();

            _punchStateMachine = CreatePunchStateMachine();
            _punchStateMachine.IsSimulationChecked = IsSimulate;
            _punchStateMachine.IsDetectionEnabled = !IsSimulate;
            _punchStateMachine.StateChanged += PunchStateMachine_StateChanged;
            _punchStateMachine.MessageReported += PunchStateMachine_MessageReported;
            NotifyProcessCommandStateChanged();

            var processType = IsFirstPass ? PunchProcessType.FirstPass : PunchProcessType.SecondPass;
            _punchStateMachine.StartProcess(processType);

            try
            {
                _punchingTask = RunPunchingProcessAsync(CurrentRecipeViewModel, _punchStateMachine, processType, _punchingCancellationTokenSource.Token);
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

            Status.WorkpieceType = selectedRecipeName;
            LoadSelectedRecipe();
            _logger?.LogInformation("已刷新配方列表，共 {RecipeCount} 个", RecipeNames.Count);
        }

        [RelayCommand]
        private void Navigate(string winName)
        {
            Window? window = winName switch
            {
                "Manual" => new ManualControlView(),
                "Config" => new ConfigWindow(),
                "PartScan" => new ScanWindow(),
                "Calibration" => new CalibrationWindow(),
                "Compensation" => new PunchingCompensationView(),
                "Detection" => new DetectionView(),
                "SecondPassDetection" => new SecondPassDetectionView(),
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

            //var existedWindow = Application.Current?.Windows
            //    .OfType<Window>()
            //    .FirstOrDefault(x => x.GetType() == window.GetType());

            //if (existedWindow is not null)
            //{
            //    existedWindow.WindowState = WindowState.Normal;
            //    existedWindow.Activate();
            //    _logger?.LogInformation("激活窗口: {WinName}", winName);
            //    return;
            //}

            window.Owner = Application.Current?.MainWindow;
            window.ShowDialog();
            _logger?.LogInformation("弹出窗口: {WinName}", winName);
        }

        private bool FilterLogEntry(object item)
        {
            if (item is not AppLogEntry entry)
            {
                return false;
            }

            if (!OnlyShowWarningsAndErrors)
            {
                return true;
            }

            return entry.Level is Serilog.Events.LogEventLevel.Warning
                or Serilog.Events.LogEventLevel.Error
                or Serilog.Events.LogEventLevel.Fatal;
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
                CurrentRecipeViewModel = null;
                UpdateRecipeStatus();
                NotifyProcessCommandStateChanged();
                return;
            }

            if (!_recipeService.Load(Status.WorkpieceType))
            {
                CurrentRecipeViewModel = null;
                UpdateRecipeStatus();
                NotifyProcessCommandStateChanged();
                _logger?.LogWarning("未找到配方: {RecipeName}", Status.WorkpieceType);
                return;
            }

            Status.PunchedHoles = 0;
            CurrentRecipeViewModel = _recipeService.CurrentRecipe is null ? null : new RecipeViewModel(_recipeService.CurrentRecipe);
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

            Status.TotalHoles = punchPoints.Count;

            var completedCount = Math.Clamp(Status.PunchedHoles, 0, punchPoints.Count);
            if (completedCount != Status.PunchedHoles)
            {
                Status.PunchedHoles = completedCount;
                return;
            }

            CurrentRecipeViewModel.UpdateCompletedCount(completedCount);
            Status.RemainingHoles = CurrentRecipeViewModel.RemainingCount;

            if (completedCount >= punchPoints.Count)
            {
                var lastPoint = punchPoints[^1];
                Status.CurrentRing = lastPoint.RingNumber;
                Status.CurrentHole = 0;
                return;
            }

            var nextPoint = punchPoints[completedCount];
            Status.CurrentRing = nextPoint.RingNumber;
            Status.CurrentHole = nextPoint.SequenceIndex;
        }

        private PunchStateMachine CreatePunchStateMachine()
        {
            IHardwareController hardwareController = IsSimulate
                ? new MockHardwareController()
                : new HardwareSimulation();

            return new PunchStateMachine(hardwareController);
        }

        private async Task RunPunchingProcessAsync(RecipeViewModel recipeViewModel, PunchStateMachine punchStateMachine, PunchProcessType processType, CancellationToken cancellationToken)
        {
            for (int index = 0; index < recipeViewModel.PunchPoints.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var point = recipeViewModel.PunchPoints[index];
                punchStateMachine.IsLastHole = index == recipeViewModel.PunchPoints.Count - 1;

                _logger?.LogInformation("开始检测孔位: 圈 {RingNumber}, 序号 {SequenceIndex}, 坐标 ({X}, {Y})",
                    point.RingNumber,
                    point.SequenceIndex,
                    point.X,
                    point.Y);

                while (punchStateMachine.CurrentState != PunchState.Finished
                    && punchStateMachine.CurrentHoleIndex == index + 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (punchStateMachine.CurrentState == PunchState.Paused)
                    {
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }

                    punchStateMachine.ExecuteNextStep(processType);
                    await Task.Delay(50, cancellationToken);
                }

                if (punchStateMachine.CompletionStatus is PunchCompletionStatus.AbnormalFinished or PunchCompletionStatus.Cancelled)
                {
                    break;
                }

                point.Complete = true;
                Status.PunchedHoles = index + 1;

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
            NotifyProcessCommandStateChanged();
            _logger?.LogInformation("状态切换: {OldState} -> {NewState}, 当前孔位索引: {HoleIndex}", e.OldState, e.NewState, e.CurrentHoleIndex);
        }

        private void PunchStateMachine_MessageReported(object? sender, MessageEventArgs e)
        {
            if (e.IsAlarm)
            {
                _logger?.LogWarning("{Message}", e.Message);
                return;
            }

            _logger?.LogInformation("{Message}", e.Message);
        }

        private void StartCameraPreview()
        {
            if (_camera is null)
            {
                return;
            }

            _cameraPreviewCancellationTokenSource?.Cancel();
            _cameraPreviewCancellationTokenSource = new CancellationTokenSource();
            _cameraPreviewTask = Task.Run(() => CameraPreviewLoopAsync(_cameraPreviewCancellationTokenSource.Token));
        }

        private async Task CameraPreviewLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!_camera!.IsConnected)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => Status.IsCameraConnected = _camera.Open());
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => Status.IsCameraConnected = true);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await _camera.GrabAsync().ConfigureAwait(false);
                    var bitmap = CreateBitmapSource(frame);

                    if (bitmap is not null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => CurrentCameraImage = bitmap);
                    }

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => Status.IsCameraConnected = false);
                _logger?.LogError(ex, "主界面相机预览启动失败");
            }
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
                && !IsPunchingActive;
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
            return !IsPunchingActive;
        }

        private bool IsPunchingActive => _punchingTask is { IsCompleted: false };

        private bool IsPunchingPaused => _punchStateMachine?.CurrentState == PunchState.Paused;

        private void NotifyProcessCommandStateChanged()
        {
            OnPropertyChanged(nameof(CanUseCustomAction));
            OnPropertyChanged(nameof(ShowPausePunchingButton));
            OnPropertyChanged(nameof(ShowContinuePunchingButton));
            StartPunchingCommand.NotifyCanExecuteChanged();
            PausePunchingCommand.NotifyCanExecuteChanged();
            ContinuePunchingCommand.NotifyCanExecuteChanged();
            StopPunchingCommand.NotifyCanExecuteChanged();
            ResetMachineCommand.NotifyCanExecuteChanged();
        }

        
    }
}