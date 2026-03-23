using Common.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using Fredy.Drilling.Holes.Views;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IAppLogExportService? _logExportService;
        private readonly IAppLogStore? _logStore;
        private readonly ILogger<MainViewModel>? _logger;
        private readonly RecipeService? _recipeService;

        [ObservableProperty] private MachineStatus _status = new();
        [ObservableProperty] private bool _isSimulate;
        [ObservableProperty] private bool _isFirstPass = true; // 默认选中头道
        [ObservableProperty] private bool _onlyShowWarningsAndErrors;

        private RecipeViewModel? _currentRecipeViewModel;

        public ObservableCollection<string> RecipeNames { get; } = new();

        public int LogCapacity => _logStore?.Capacity ?? 0;

        public ICollectionView FilteredLogs { get; }

        public ReadOnlyObservableCollection<AppLogEntry> Logs { get; }

        public RecipeViewModel? CurrentRecipeViewModel
        {
            get => _currentRecipeViewModel;
            set => SetProperty(ref _currentRecipeViewModel, value);
        }

        public MainViewModel()
        {
            Logs = new ReadOnlyObservableCollection<AppLogEntry>(new ObservableCollection<AppLogEntry>());
            FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
            FilteredLogs.Filter = FilterLogEntry;
            Status.PropertyChanged += Status_PropertyChanged;
        }

        public MainViewModel(IAppLogStore logStore, IAppLogExportService logExportService, ILogger<MainViewModel> logger, RecipeService recipeService)
        {
            _logStore = logStore;
            _logExportService = logExportService;
            _logger = logger;
            _recipeService = recipeService;
            Logs = logStore.Entries;
            FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
            FilteredLogs.Filter = FilterLogEntry;
            Status.PropertyChanged += Status_PropertyChanged;
            RefreshRecipes();

            _logger.LogInformation("主界面视图模型已初始化");
        }

        partial void OnOnlyShowWarningsAndErrorsChanged(bool value)
        {
            FilteredLogs.Refresh();
            //_logger.LogInformation("日志筛选已切换为: {FilterMode}", value ? "仅看告警/错误" : "显示全部");
        }

        [RelayCommand]
        private void StartPunching()
        {
            _logger?.LogInformation("收到启动冲孔命令，模式: {Mode}，工序: {Pass}", IsSimulate ? "模拟" : "实际", IsFirstPass ? "头道" : "二道");
        }

        [RelayCommand]
        private void StopPunching()
        {
            _logger?.LogWarning("收到停止冲孔命令");
        }

        [RelayCommand]
        private void PausePunching()
        {
            _logger?.LogInformation("收到暂停冲孔命令");
        }

        [RelayCommand]
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
                "ProcessParam" => new ProcessWindow(),
                "Calibration" => new CalibrationWindow(),
                "Compensation" => new PunchingCompensationView(),
                "Detection" => new DetectionView(),
                "SecondPassDetection" => new SecondPassDetectionView(),
                _ => null
            };
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (window is null)
            {
                _logger?.LogWarning("未识别的窗口名称: {WinName}", winName);
                MessageBox.Show($"未识别的窗口名称: {winName}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
                return;
            }

            if (!_recipeService.Load(Status.WorkpieceType))
            {
                CurrentRecipeViewModel = null;
                UpdateRecipeStatus();
                _logger?.LogWarning("未找到配方: {RecipeName}", Status.WorkpieceType);
                return;
            }

            CurrentRecipeViewModel = _recipeService.CurrentRecipe is null ? null : new RecipeViewModel(_recipeService.CurrentRecipe);
            UpdateRecipeStatus();
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

        
    }
}