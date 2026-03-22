using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using Fredy.Drilling.Holes.Views;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Fredy.Drilling.Holes.Views;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IAppLogExportService _logExportService;
        private readonly IAppLogStore _logStore;
        private readonly ILogger<MainViewModel> _logger;

        [ObservableProperty] private MachineStatus _status = new();
        [ObservableProperty] private bool _isSimulate;
        [ObservableProperty] private bool _isFirstPass = true; // 默认选中头道
        [ObservableProperty] private bool _onlyShowWarningsAndErrors;

        public int LogCapacity => _logStore.Capacity;

        public ICollectionView FilteredLogs { get; }

        public ReadOnlyObservableCollection<AppLogEntry> Logs { get; }
        public MainViewModel() { }
        public MainViewModel(IAppLogStore logStore, IAppLogExportService logExportService, ILogger<MainViewModel> logger)
        {
            _logStore = logStore;
            _logExportService = logExportService;
            _logger = logger;
            Logs = logStore.Entries;
            FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
            FilteredLogs.Filter = FilterLogEntry;

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
            _logger.LogInformation("收到启动冲孔命令，模式: {Mode}，工序: {Pass}", IsSimulate ? "模拟" : "实际", IsFirstPass ? "头道" : "二道");
        }

        [RelayCommand]
        private void StopPunching()
        {
            _logger.LogWarning("收到停止冲孔命令");
        }

        [RelayCommand]
        private void PausePunching()
        {
            _logger.LogInformation("收到暂停冲孔命令");
        }

        [RelayCommand]
        private void ResetMachine()
        {
            _logger.LogInformation("收到机台复位命令");
        }

        [RelayCommand]
        private void ClearLogs()
        {
            _logStore.Clear();
            //_logger.LogInformation("日志已清空");
        }

        [RelayCommand]
        private async Task ExportLogsAsync()
        {
            var filePath = await _logExportService.ExportAsync(Logs);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogInformation("日志导出已取消");
                return;
            }

            _logger.LogInformation("日志已导出到: {FilePath}", filePath);
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

        
    }
}