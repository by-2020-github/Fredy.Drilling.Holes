using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ConfigViewModel : ObservableObject
    {
        private readonly ConfigService _configService;
        private string _statusMessage = string.Empty;
        private AppConfig _originalConfig = new();

        public ConfigViewModel(ConfigService configService)
        {
            _configService = configService;
            LoadFromConfig(_configService.CurrentConfig);
            _originalConfig = BuildConfig();
            RefreshModifiedParametersInfo();
        }

        public IReadOnlyList<string> MotionControllerTypes { get; } =
        new[]
        {
            "模拟运动控制卡",
            "雷赛运动控制卡",
            "固高运动控制卡",
            "PCI 运动控制卡"
        };

        public IReadOnlyList<string> MotionConnectionOptions { get; } =
        new[]
        {
            "127.0.0.1:5000",
            "COM1;115200;8;None;1",
            "192.168.1.10:5000"
        };

        public IReadOnlyList<string> CameraTypes { get; } =
        new[]
        {
            "模拟相机",
            "海康 GigE",
            "海康 USB",
            "Basler GigE"
        };

        public IReadOnlyList<string> CameraConnectionOptions { get; } =
        new[]
        {
            "rtsp://127.0.0.1/live",
            "GigE://192.168.1.64"
        };

        [ObservableProperty] private MotionControllerConfig _motionController = new();
        [ObservableProperty] private CameraConfig _camera = new();

        [ObservableProperty] private MotionParams _xyDrive = new();
        [ObservableProperty] private MotionParams _zAxisBase = new();
        [ObservableProperty] private MotionParams _firstPass = new();
        [ObservableProperty] private MotionParams _secondPass = new();

        [ObservableProperty] private double _fastMovePos = -22.0;
        [ObservableProperty] private int _fastMoveSpeed = 9000;
        [ObservableProperty] private double _slowMoveDist = -12.0;
        [ObservableProperty] private int _slowMoveSpeed = 700;

        [ObservableProperty] private int _homeSearchSpeed = 3000;
        [ObservableProperty] private bool _isIoHome;
        [ObservableProperty] private bool _isLatch;
        [ObservableProperty] private bool _isGratingHome;

        [ObservableProperty] private PortItem _xLimitPort = new() { PortIndex = 14 };
        [ObservableProperty] private PortItem _yLimitPort = new() { PortIndex = 14 };
        [ObservableProperty] private int _redLightPort = 14;

        [ObservableProperty] private bool _isDebugMode;

        [ObservableProperty] private int _detectionOffsetThreshold;

        [ObservableProperty] private int _secondPassOffsetThreshold = 35;

        public ObservableCollection<DetectionRingItem> DetectionRingItems { get; } = new();

        public ObservableCollection<SecondPassDetectionItem> SecondPassDetectionItems { get; } = new();

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string ModifiedParametersInfo => BuildModifiedParametersInfo();

        [RelayCommand]
        private void SaveConfig()
        {
            try
            {
                _configService.SaveWithArchive(BuildConfig());
                _originalConfig = BuildConfig();
                RefreshModifiedParametersInfo();
                StatusMessage = "配置已保存，并已更新运行时配置。";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private void CancelChanges()
        {
            LoadFromConfig(_configService.Reload());
            _originalConfig = BuildConfig();
            RefreshModifiedParametersInfo();
            StatusMessage = "已取消当前修改并恢复到本地配置。";
        }

        [RelayCommand]
        private void ApplyAndClose(System.Windows.Window window)
        {
            SaveConfig();
            if (!StatusMessage.StartsWith("保存失败", StringComparison.Ordinal))
            {
                window?.Close();
            }
        }

        [RelayCommand]
        private void ResetDetection()
        {
            foreach (var item in DetectionRingItems)
            {
                item.DetectionCount = 0;
            }

            DetectionOffsetThreshold = 0;
            RefreshModifiedParametersInfo();
        }

        [RelayCommand]
        private void ResetSecondPassDetection()
        {
            foreach (var item in SecondPassDetectionItems)
            {
                item.DetectionCount = 0;
            }

            SecondPassOffsetThreshold = 0;
            RefreshModifiedParametersInfo();
        }

        private AppConfig BuildConfig()
        {
            return new AppConfig
            {
                MotionController = new MotionControllerConfig
                {
                    ControllerType = MotionController.ControllerType,
                    ConnectionString = MotionController.ConnectionString
                },
                Camera = new CameraConfig
                {
                    CameraType = Camera.CameraType,
                    ConnectionString = Camera.ConnectionString,
                    PixelSizeX = Camera.PixelSizeX,
                    PixelSizeY = Camera.PixelSizeY,
                    FovWidth = Camera.FovWidth,
                    FovHeight = Camera.FovHeight,
                    ResolutionWidth = Camera.ResolutionWidth,
                    ResolutionHeight = Camera.ResolutionHeight
                },
                XyDrive = CloneMotionParams(XyDrive),
                ZAxisBase = CloneMotionParams(ZAxisBase),
                FirstPass = CloneMotionParams(FirstPass),
                SecondPass = CloneMotionParams(SecondPass),
                FastMovePos = FastMovePos,
                FastMoveSpeed = FastMoveSpeed,
                SlowMoveDist = SlowMoveDist,
                SlowMoveSpeed = SlowMoveSpeed,
                HomeSearchSpeed = HomeSearchSpeed,
                IsIoHome = IsIoHome,
                IsLatch = IsLatch,
                IsGratingHome = IsGratingHome,
                XLimitPort = ClonePort(XLimitPort),
                YLimitPort = ClonePort(YLimitPort),
                RedLightPort = RedLightPort,
                IsDebugMode = IsDebugMode,
                DetectionOffsetThreshold = DetectionOffsetThreshold,
                DetectionRingItems = DetectionRingItems
                    .OrderBy(x => x.Index)
                    .Select(CloneDetectionRingItem)
                    .ToList(),
                SecondPassOffsetThreshold = SecondPassOffsetThreshold,
                SecondPassDetectionItems = SecondPassDetectionItems
                    .OrderBy(x => x.Index)
                    .Select(CloneSecondPassDetectionItem)
                    .ToList()
            };
        }

        private void LoadFromConfig(AppConfig config)
        {
            UnsubscribeNestedPropertyChanged();

            MotionController = new MotionControllerConfig
            {
                ControllerType = config.MotionController.ControllerType,
                ConnectionString = config.MotionController.ConnectionString
            };

            Camera = new CameraConfig
            {
                CameraType = config.Camera.CameraType,
                ConnectionString = config.Camera.ConnectionString,
                PixelSizeX = config.Camera.PixelSizeX,
                PixelSizeY = config.Camera.PixelSizeY,
                FovWidth = config.Camera.FovWidth,
                FovHeight = config.Camera.FovHeight,
                ResolutionWidth = config.Camera.ResolutionWidth,
                ResolutionHeight = config.Camera.ResolutionHeight
            };

            XyDrive = CloneMotionParams(config.XyDrive);
            ZAxisBase = CloneMotionParams(config.ZAxisBase);
            FirstPass = CloneMotionParams(config.FirstPass);
            SecondPass = CloneMotionParams(config.SecondPass);

            FastMovePos = config.FastMovePos;
            FastMoveSpeed = config.FastMoveSpeed;
            SlowMoveDist = config.SlowMoveDist;
            SlowMoveSpeed = config.SlowMoveSpeed;

            HomeSearchSpeed = config.HomeSearchSpeed;
            IsIoHome = config.IsIoHome;
            IsLatch = config.IsLatch;
            IsGratingHome = config.IsGratingHome;

            XLimitPort = ClonePort(config.XLimitPort);
            YLimitPort = ClonePort(config.YLimitPort);
            RedLightPort = config.RedLightPort;
            IsDebugMode = config.IsDebugMode;

            DetectionOffsetThreshold = config.DetectionOffsetThreshold;
            SecondPassOffsetThreshold = config.SecondPassOffsetThreshold;

            ReplaceDetectionRingItems(config.DetectionRingItems);
            ReplaceSecondPassDetectionItems(config.SecondPassDetectionItems);

            SubscribeNestedPropertyChanged();
            RefreshModifiedParametersInfo();
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(ModifiedParametersInfo))
            {
                return;
            }

            RefreshModifiedParametersInfo();
        }

        private void SubscribeNestedPropertyChanged()
        {
            MotionController.PropertyChanged += NestedObject_PropertyChanged;
            Camera.PropertyChanged += NestedObject_PropertyChanged;
            XyDrive.PropertyChanged += NestedObject_PropertyChanged;
            ZAxisBase.PropertyChanged += NestedObject_PropertyChanged;
            FirstPass.PropertyChanged += NestedObject_PropertyChanged;
            SecondPass.PropertyChanged += NestedObject_PropertyChanged;
            XLimitPort.PropertyChanged += NestedObject_PropertyChanged;
            YLimitPort.PropertyChanged += NestedObject_PropertyChanged;

            DetectionRingItems.CollectionChanged += DetectionRingItems_CollectionChanged;
            SecondPassDetectionItems.CollectionChanged += SecondPassDetectionItems_CollectionChanged;

            SubscribeDetectionItems(DetectionRingItems);
            SubscribeSecondPassItems(SecondPassDetectionItems);
        }

        private void UnsubscribeNestedPropertyChanged()
        {
            MotionController.PropertyChanged -= NestedObject_PropertyChanged;
            Camera.PropertyChanged -= NestedObject_PropertyChanged;
            XyDrive.PropertyChanged -= NestedObject_PropertyChanged;
            ZAxisBase.PropertyChanged -= NestedObject_PropertyChanged;
            FirstPass.PropertyChanged -= NestedObject_PropertyChanged;
            SecondPass.PropertyChanged -= NestedObject_PropertyChanged;
            XLimitPort.PropertyChanged -= NestedObject_PropertyChanged;
            YLimitPort.PropertyChanged -= NestedObject_PropertyChanged;

            DetectionRingItems.CollectionChanged -= DetectionRingItems_CollectionChanged;
            SecondPassDetectionItems.CollectionChanged -= SecondPassDetectionItems_CollectionChanged;

            UnsubscribeDetectionItems(DetectionRingItems);
            UnsubscribeSecondPassItems(SecondPassDetectionItems);
        }

        private void DetectionRingItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<DetectionRingItem>())
                {
                    item.PropertyChanged -= NestedObject_PropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<DetectionRingItem>())
                {
                    item.PropertyChanged += NestedObject_PropertyChanged;
                }
            }

            RefreshModifiedParametersInfo();
        }

        private void SecondPassDetectionItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<SecondPassDetectionItem>())
                {
                    item.PropertyChanged -= NestedObject_PropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<SecondPassDetectionItem>())
                {
                    item.PropertyChanged += NestedObject_PropertyChanged;
                }
            }

            RefreshModifiedParametersInfo();
        }

        private void SubscribeDetectionItems(IEnumerable<DetectionRingItem> items)
        {
            foreach (var item in items)
            {
                item.PropertyChanged += NestedObject_PropertyChanged;
            }
        }

        private void UnsubscribeDetectionItems(IEnumerable<DetectionRingItem> items)
        {
            foreach (var item in items)
            {
                item.PropertyChanged -= NestedObject_PropertyChanged;
            }
        }

        private void SubscribeSecondPassItems(IEnumerable<SecondPassDetectionItem> items)
        {
            foreach (var item in items)
            {
                item.PropertyChanged += NestedObject_PropertyChanged;
            }
        }

        private void UnsubscribeSecondPassItems(IEnumerable<SecondPassDetectionItem> items)
        {
            foreach (var item in items)
            {
                item.PropertyChanged -= NestedObject_PropertyChanged;
            }
        }

        private void NestedObject_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RefreshModifiedParametersInfo();
        }

        private void RefreshModifiedParametersInfo()
        {
            OnPropertyChanged(nameof(ModifiedParametersInfo));
        }

        private string BuildModifiedParametersInfo()
        {
            var current = BuildConfig();
            var lines = new List<string>();

            AddIfChanged(lines, "控制卡类型", _originalConfig.MotionController.ControllerType, current.MotionController.ControllerType);
            AddIfChanged(lines, "控制卡连接", _originalConfig.MotionController.ConnectionString, current.MotionController.ConnectionString);

            AddIfChanged(lines, "相机类型", _originalConfig.Camera.CameraType, current.Camera.CameraType);
            AddIfChanged(lines, "相机连接", _originalConfig.Camera.ConnectionString, current.Camera.ConnectionString);
            AddIfChanged(lines, "像素尺寸X", _originalConfig.Camera.PixelSizeX, current.Camera.PixelSizeX);
            AddIfChanged(lines, "像素尺寸Y", _originalConfig.Camera.PixelSizeY, current.Camera.PixelSizeY);
            AddIfChanged(lines, "视野宽", _originalConfig.Camera.FovWidth, current.Camera.FovWidth);
            AddIfChanged(lines, "视野高", _originalConfig.Camera.FovHeight, current.Camera.FovHeight);
            AddIfChanged(lines, "分辨率宽", _originalConfig.Camera.ResolutionWidth, current.Camera.ResolutionWidth);
            AddIfChanged(lines, "分辨率高", _originalConfig.Camera.ResolutionHeight, current.Camera.ResolutionHeight);

            AddMotionDiff(lines, "XY", _originalConfig.XyDrive, current.XyDrive);
            AddMotionDiff(lines, "Z轴基础", _originalConfig.ZAxisBase, current.ZAxisBase);
            AddMotionDiff(lines, "头道", _originalConfig.FirstPass, current.FirstPass);
            AddMotionDiff(lines, "二道", _originalConfig.SecondPass, current.SecondPass);

            AddIfChanged(lines, "快速移动位置", _originalConfig.FastMovePos, current.FastMovePos);
            AddIfChanged(lines, "快速速度", _originalConfig.FastMoveSpeed, current.FastMoveSpeed);
            AddIfChanged(lines, "慢速移动距离", _originalConfig.SlowMoveDist, current.SlowMoveDist);
            AddIfChanged(lines, "慢速速度", _originalConfig.SlowMoveSpeed, current.SlowMoveSpeed);
            AddIfChanged(lines, "搜索速度", _originalConfig.HomeSearchSpeed, current.HomeSearchSpeed);

            AddIfChanged(lines, "IO回零", _originalConfig.IsIoHome, current.IsIoHome);
            AddIfChanged(lines, "锁存", _originalConfig.IsLatch, current.IsLatch);
            AddIfChanged(lines, "光栅尺回零", _originalConfig.IsGratingHome, current.IsGratingHome);

            AddIfChanged(lines, "X机械零位端口", _originalConfig.XLimitPort.PortIndex, current.XLimitPort.PortIndex);
            AddIfChanged(lines, "X机械零位低电平", _originalConfig.XLimitPort.IsLowLevelActive, current.XLimitPort.IsLowLevelActive);
            AddIfChanged(lines, "Y机械零位端口", _originalConfig.YLimitPort.PortIndex, current.YLimitPort.PortIndex);
            AddIfChanged(lines, "Y机械零位低电平", _originalConfig.YLimitPort.IsLowLevelActive, current.YLimitPort.IsLowLevelActive);
            AddIfChanged(lines, "红灯端口", _originalConfig.RedLightPort, current.RedLightPort);
            AddIfChanged(lines, "调试模式", _originalConfig.IsDebugMode, current.IsDebugMode);

            AddIfChanged(lines, "一道探测偏移阈值", _originalConfig.DetectionOffsetThreshold, current.DetectionOffsetThreshold);
            AddIfChanged(lines, "二道探测偏移阈值", _originalConfig.SecondPassOffsetThreshold, current.SecondPassOffsetThreshold);

            AddDetectionListDiff(lines, "一道", _originalConfig.DetectionRingItems, current.DetectionRingItems);
            AddSecondPassListDiff(lines, "二道", _originalConfig.SecondPassDetectionItems, current.SecondPassDetectionItems);

            return lines.Count == 0 ? "当前没有已修改参数。" : string.Join(Environment.NewLine, lines);
        }

        private static void AddMotionDiff(List<string> lines, string prefix, MotionParams oldValue, MotionParams currentValue)
        {
            AddIfChanged(lines, $"{prefix}-初速度", oldValue.StartSpeed, currentValue.StartSpeed);
            AddIfChanged(lines, $"{prefix}-驱动速度", oldValue.DriveSpeed, currentValue.DriveSpeed);
            AddIfChanged(lines, $"{prefix}-加速度", oldValue.Acceleration, currentValue.Acceleration);
            AddIfChanged(lines, $"{prefix}-延时", oldValue.Delay, currentValue.Delay);
            AddIfChanged(lines, $"{prefix}-脉冲当量", oldValue.PulseEquivalent, currentValue.PulseEquivalent);
        }

        private static void AddDetectionListDiff(ICollection<string> lines, string prefix, IReadOnlyList<DetectionRingItem> oldValues, IReadOnlyList<DetectionRingItem> currentValues)
        {
            var maxCount = Math.Max(oldValues.Count, currentValues.Count);
            for (int i = 0; i < maxCount; i++)
            {
                var oldValue = i < oldValues.Count ? oldValues[i].DetectionCount : 0;
                var currentValue = i < currentValues.Count ? currentValues[i].DetectionCount : 0;
                if (oldValue != currentValue)
                {
                    lines.Add($"{prefix}第{i + 1}圈探测次数: {oldValue} -> {currentValue}");
                }
            }
        }

        private static void AddSecondPassListDiff(ICollection<string> lines, string prefix, IReadOnlyList<SecondPassDetectionItem> oldValues, IReadOnlyList<SecondPassDetectionItem> currentValues)
        {
            var maxCount = Math.Max(oldValues.Count, currentValues.Count);
            for (int i = 0; i < maxCount; i++)
            {
                var oldValue = i < oldValues.Count ? oldValues[i].DetectionCount : 0;
                var currentValue = i < currentValues.Count ? currentValues[i].DetectionCount : 0;
                if (oldValue != currentValue)
                {
                    lines.Add($"{prefix}第{i + 1}圈探测次数: {oldValue} -> {currentValue}");
                }
            }
        }

        private static void AddIfChanged<T>(ICollection<string> lines, string name, T oldValue, T currentValue)
        {
            if (EqualityComparer<T>.Default.Equals(oldValue, currentValue))
            {
                return;
            }

            lines.Add($"{name}: {oldValue} -> {currentValue}");
        }

        private void ReplaceDetectionRingItems(IEnumerable<DetectionRingItem>? items)
        {
            DetectionRingItems.Clear();
            var source = items?.ToList();
            if (source is null || source.Count == 0)
            {
                for (int i = 1; i <= 32; i++)
                {
                    DetectionRingItems.Add(new DetectionRingItem { Index = i, DetectionCount = 0 });
                }

                return;
            }

            foreach (var item in source.OrderBy(x => x.Index))
            {
                DetectionRingItems.Add(CloneDetectionRingItem(item));
            }
        }

        private void ReplaceSecondPassDetectionItems(IEnumerable<SecondPassDetectionItem>? items)
        {
            SecondPassDetectionItems.Clear();
            var source = items?.ToList();
            if (source is null || source.Count == 0)
            {
                for (int i = 1; i <= 32; i++)
                {
                    SecondPassDetectionItems.Add(new SecondPassDetectionItem { Index = i, DetectionCount = 0 });
                }

                return;
            }

            foreach (var item in source.OrderBy(x => x.Index))
            {
                SecondPassDetectionItems.Add(CloneSecondPassDetectionItem(item));
            }
        }

        private static MotionParams CloneMotionParams(MotionParams source)
        {
            return new MotionParams
            {
                StartSpeed = source.StartSpeed,
                DriveSpeed = source.DriveSpeed,
                Acceleration = source.Acceleration,
                Delay = source.Delay,
                PulseEquivalent = source.PulseEquivalent
            };
        }

        private static PortItem ClonePort(PortItem source)
        {
            return new PortItem
            {
                PortIndex = source.PortIndex,
                IsLowLevelActive = source.IsLowLevelActive
            };
        }

        private static DetectionRingItem CloneDetectionRingItem(DetectionRingItem source)
        {
            return new DetectionRingItem
            {
                Index = source.Index,
                DetectionCount = source.DetectionCount
            };
        }

        private static SecondPassDetectionItem CloneSecondPassDetectionItem(SecondPassDetectionItem source)
        {
            return new SecondPassDetectionItem
            {
                Index = source.Index,
                DetectionCount = source.DetectionCount
            };
        }
    }
}