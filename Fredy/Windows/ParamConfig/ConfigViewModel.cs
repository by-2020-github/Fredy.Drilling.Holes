using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ConfigViewModel : ObservableObject
    {
        // XY 驱动参数
        [ObservableProperty] private MotionParams _xyDrive = new();

        // Z 轴参数及其嵌套子项
        [ObservableProperty] private MotionParams _zAxisBase = new();
        [ObservableProperty] private MotionParams _firstPass = new();
        [ObservableProperty] private MotionParams _secondPass = new();

        // 探测参数
        [ObservableProperty] private double _fastMovePos = -22.0;
        [ObservableProperty] private int _fastMoveSpeed = 9000;
        [ObservableProperty] private double _slowMoveDist = -12.0;
        [ObservableProperty] private int _slowMoveSpeed = 700;

        // 回零参数
        [ObservableProperty] private int _homeSearchSpeed = 3000;
        [ObservableProperty] private bool _isIoHome;
        [ObservableProperty] private bool _isLatch;
        [ObservableProperty] private bool _isGratingHome;

        // 端口配置 (仅展示部分示例，实际可按此扩展)
        [ObservableProperty] private PortItem _xLimitPort = new() { PortIndex = 14 };
        [ObservableProperty] private PortItem _yLimitPort = new() { PortIndex = 14 };
        [ObservableProperty] private int _redLightPort = 14;

        [ObservableProperty] private bool _isDebugMode;

        [RelayCommand]
        private void ApplyAndClose(System.Windows.Window window)
        {
            // TODO: 保存参数到本地 Config 文件
            window?.Close();
        }
    }
}