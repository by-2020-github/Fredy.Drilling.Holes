using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Fredy.Drilling.Holes.Models
{
    // 通用运动参数 (初速度, 驱动速度, 加速度, 延时)
    public partial class MotionParams : ObservableObject
    {
        [ObservableProperty] private int _startSpeed;
        [ObservableProperty] private int _driveSpeed;
        [ObservableProperty] private int _acceleration;
        [ObservableProperty] private int _delay;
        [ObservableProperty] private int _pulseEquivalent; // 脉冲当量
    }

    // 端口项 (编号 + 低电平取反)
    public partial class PortItem : ObservableObject
    {
        [ObservableProperty] private int _portIndex;
        [ObservableProperty] private bool _isLowLevelActive;
    }

    public partial class MotionControllerConfig : ObservableObject
    {
        [ObservableProperty] private string _controllerType = "模拟运动控制卡";
        [ObservableProperty] private string _connectionString = "127.0.0.1:5000";
    }

    public partial class CameraConfig : ObservableObject
    {
        [ObservableProperty] private string _cameraType = "模拟相机";
        [ObservableProperty] private string _connectionString = "Index=0";
        [ObservableProperty] private double _pixelSizeX = 3.45;
        [ObservableProperty] private double _pixelSizeY = 3.45;
        [ObservableProperty] private int _fovWidth = 2000;
        [ObservableProperty] private int _fovHeight = 2000;
        [ObservableProperty] private int _resolutionWidth = 2448;
        [ObservableProperty] private int _resolutionHeight = 2048;
    }

    public class AppConfig
    {
        public MotionControllerConfig MotionController { get; set; } = new();

        public CameraConfig Camera { get; set; } = new();

        public MotionParams XyDrive { get; set; } = new();

        public MotionParams ZAxisBase { get; set; } = new();

        public MotionParams FirstPass { get; set; } = new();

        public MotionParams SecondPass { get; set; } = new();

        public double FastMovePos { get; set; } = -22.0;

        public int FastMoveSpeed { get; set; } = 9000;

        public double SlowMoveDist { get; set; } = -12.0;

        public int SlowMoveSpeed { get; set; } = 700;

        public int HomeSearchSpeed { get; set; } = 3000;

        public bool IsIoHome { get; set; }

        public bool IsLatch { get; set; }

        public bool IsGratingHome { get; set; }

        public PortItem XLimitPort { get; set; } = new() { PortIndex = 14 };

        public PortItem YLimitPort { get; set; } = new() { PortIndex = 14 };

        public int RedLightPort { get; set; } = 14;

        public bool IsDebugMode { get; set; }

        public List<DetectionRingItem> DetectionRingItems { get; set; } = CreateDefaultDetectionRingItems();

        public int DetectionOffsetThreshold { get; set; }

        public List<SecondPassDetectionItem> SecondPassDetectionItems { get; set; } = CreateDefaultSecondPassDetectionItems();

        public int SecondPassOffsetThreshold { get; set; } = 35;

        private static List<DetectionRingItem> CreateDefaultDetectionRingItems()
        {
            var items = new List<DetectionRingItem>();
            for (int i = 1; i <= 32; i++)
            {
                items.Add(new DetectionRingItem { Index = i, DetectionCount = 0 });
            }

            return items;
        }

        private static List<SecondPassDetectionItem> CreateDefaultSecondPassDetectionItems()
        {
            var items = new List<SecondPassDetectionItem>();
            for (int i = 1; i <= 32; i++)
            {
                items.Add(new SecondPassDetectionItem { Index = i, DetectionCount = 0 });
            }

            return items;
        }
    }
}