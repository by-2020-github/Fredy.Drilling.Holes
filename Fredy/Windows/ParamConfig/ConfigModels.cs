using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Fredy.Drilling.Holes.Models
{
    // 通用运动参数 (初速度 mm/s, 驱动速度 mm/s, 加速度 mm/s², 延时 ms)
    public partial class MotionParams : ObservableObject
    {
        [ObservableProperty] private double _startSpeed;
        [ObservableProperty] private double _driveSpeed;
        [ObservableProperty] private double _acceleration;
        [ObservableProperty] private int _delay;
    }

    public partial class AxisParamConfig : ObservableObject
    {
        [ObservableProperty] private int _axisNo;
        [ObservableProperty] private double _velocity;
        [ObservableProperty] private double _acceleration;
        [ObservableProperty] private double _deceleration;
        [ObservableProperty] private double? _leftLimit;
        [ObservableProperty] private double? _rightLimit;
        [ObservableProperty] private double _pulsesPerMillimeter = 1d;
        [ObservableProperty] private bool _useActualPositionFeedback;
        [ObservableProperty] private double? _inPositionTolerance;
    }

    // 端口项 (编号 + 低电平取反)
    public partial class PortItem : ObservableObject
    {
        [ObservableProperty] private int _portIndex;
        [ObservableProperty] private bool _isLowLevelActive;
    }

    public class AdtHomingConfig : ObservableObject
    {
        public PortItem ZLimitPort { get; set; } = new() { PortIndex = 14 };

        public PortItem XGratingPort { get; set; } = new() { PortIndex = -1, IsLowLevelActive = true };

        public PortItem YGratingPort { get; set; } = new() { PortIndex = -1, IsLowLevelActive = true };

        private int _homeTimeoutMs = 10000;

        private double _homeBackoffMm = 0.2;

        private double _zHomeLiftMm = 0.0;

        private double _slowHomeStartSpeed = 0.1;

        private double _slowHomeSpeed = 0.5;

        private double _slowHomeAcceleration = 1.0;

        private double _gratingHomeStartSpeed = 0.5;

        private double _gratingHomeSpeed = 2.0;

        private double _gratingHomeAcceleration = 2.0;

        private bool _zHomeTowardPositiveDirection;

        public int HomeTimeoutMs
        {
            get => _homeTimeoutMs;
            set => SetProperty(ref _homeTimeoutMs, value);
        }

        // 脱离距离 (mm)
        public double HomeBackoffMm
        {
            get => _homeBackoffMm;
            set => SetProperty(ref _homeBackoffMm, value);
        }

        // Z轴回零后抬起距离 (mm)
        public double ZHomeLiftMm
        {
            get => _zHomeLiftMm;
            set => SetProperty(ref _zHomeLiftMm, value);
        }

        // 慢速回零初速度 (mm/s)
        public double SlowHomeStartSpeed
        {
            get => _slowHomeStartSpeed;
            set => SetProperty(ref _slowHomeStartSpeed, value);
        }

        // 慢速回零速度 (mm/s)
        public double SlowHomeSpeed
        {
            get => _slowHomeSpeed;
            set => SetProperty(ref _slowHomeSpeed, value);
        }

        // 慢速回零加速度 (mm/s²)
        public double SlowHomeAcceleration
        {
            get => _slowHomeAcceleration;
            set => SetProperty(ref _slowHomeAcceleration, value);
        }

        // 光栅回零初速度 (mm/s)
        public double GratingHomeStartSpeed
        {
            get => _gratingHomeStartSpeed;
            set => SetProperty(ref _gratingHomeStartSpeed, value);
        }

        // 光栅回零速度 (mm/s)
        public double GratingHomeSpeed
        {
            get => _gratingHomeSpeed;
            set => SetProperty(ref _gratingHomeSpeed, value);
        }

        // 光栅回零加速度 (mm/s²)
        public double GratingHomeAcceleration
        {
            get => _gratingHomeAcceleration;
            set => SetProperty(ref _gratingHomeAcceleration, value);
        }

        public bool ZHomeTowardPositiveDirection
        {
            get => _zHomeTowardPositiveDirection;
            set => SetProperty(ref _zHomeTowardPositiveDirection, value);
        }
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
        [ObservableProperty] private string _saveDirectory = string.Empty;
    }

    public class AppConfig
    {
        public MotionControllerConfig MotionController { get; set; } = new();

        public CameraConfig Camera { get; set; } = new();

        public MotionParams XyDrive { get; set; } = new();

        public MotionParams ZAxisBase { get; set; } = new();

        public MotionParams FirstPass { get; set; } = new();

        public MotionParams SecondPass { get; set; } = new();

        public AxisParamConfig XAxis { get; set; } = new() { AxisNo = 1, PulsesPerMillimeter = 1d, UseActualPositionFeedback = false };

        public AxisParamConfig YAxis { get; set; } = new() { AxisNo = 2, PulsesPerMillimeter = 1d, UseActualPositionFeedback = false };

        public AxisParamConfig ZAxis { get; set; } = new() { AxisNo = 3, PulsesPerMillimeter = 1d, UseActualPositionFeedback = false };

        public double FastMovePos { get; set; } = -22.0;

        // Z轴快速移动速度 (mm/s)
        public double FastMoveSpeed { get; set; } = 9.0;

        public double SlowMoveDist { get; set; } = -12.0;

        // Z轴慢速移动速度 (mm/s)
        public double SlowMoveSpeed { get; set; } = 0.7;

        // 回零搜索速度 (mm/s)
        public double HomeSearchSpeed { get; set; } = 3.0;

        public bool IsIoHome { get; set; }

        public bool IsLatch { get; set; }

        public bool IsGratingHome { get; set; }

        public PortItem XLimitPort { get; set; } = new() { PortIndex = 14 };

        public PortItem YLimitPort { get; set; } = new() { PortIndex = 14 };

        public AdtHomingConfig AdtHoming { get; set; } = new();

        public int RedLightPort { get; set; } = 14;

        public bool IsDebugMode { get; set; }

        public List<DetectionRingItem> DetectionRingItems { get; set; } = CreateDefaultDetectionRingItems();

        public int DetectionOffsetThreshold { get; set; }

        public List<SecondPassDetectionItem> SecondPassDetectionItems { get; set; } = CreateDefaultSecondPassDetectionItems();

        public int SecondPassOffsetThreshold { get; set; } = 35;

        public bool ScanUseBrightFieldDetector { get; set; } = true;

        public int ScanDetectMinArea { get; set; } = 3;

        public int ScanDetectMaxArea { get; set; } = 600;

        public int ScanDetectThreshold { get; set; } = 95;

        public double ScanDetectCircularity { get; set; } = 0.5;

        public int ScanDetectMorphologySize { get; set; } = 13;

        public double ScanDeduplicateToleranceMm { get; set; } = 0.08;

        public double CircleMinRadius { get; set; } = 15;

        public double CircleMaxRadius { get; set; } = 25;

        public double CircleParam1 { get; set; } = 50;

        public double CircleParam2 { get; set; } = 25;

        public bool CircleIsDarkTarget { get; set; } = true;

        public int CenterRoiWidth { get; set; } = 100;

        public int CenterRoiHeight { get; set; } = 100;

        public int CenterRoiThreshold { get; set; } = 128;

        public bool CenterRoiBinaryInvert { get; set; }

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