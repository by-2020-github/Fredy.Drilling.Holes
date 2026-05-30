using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BLL;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using MvCamCtrl.NET;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ConfigViewModel : ObservableObject
    {
        private static readonly IReadOnlyList<string> SimulatedCameraConnectionDefaults =
        [
            "Index=0"
        ];

        private static readonly IReadOnlyList<string> HikvisionCameraConnectionDefaults =
        [
        ];

        private static readonly IReadOnlyList<string> BaslerCameraConnectionDefaults =
        [
        ];

        private readonly ConfigService _configService;
            private readonly IMotionService _motionService;
        private string _statusMessage = string.Empty;
        private AppConfig _originalConfig = new();

        public ConfigViewModel(ConfigService configService, IMotionService motionService)
        {
            _configService = configService;
            _motionService = motionService;
            LoadFromConfig(_configService.CurrentConfig);
            RefreshCameraConnectionOptions();
            _originalConfig = BuildConfig();
            RefreshModifiedParametersInfo();
        }

        public IReadOnlyList<string> MotionControllerTypes { get; } =
        new[]
        {
            "模拟运动控制卡",
            "ADT8940",
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
            "海康",
            "Basler"
        };

        [ObservableProperty] private IReadOnlyList<string> _cameraConnectionOptions = SimulatedCameraConnectionDefaults;

        [ObservableProperty] private MotionControllerConfig _motionController = new();
        [ObservableProperty] private CameraConfig _camera = new();

        [ObservableProperty] private MotionParams _xyDrive = new();
        [ObservableProperty] private MotionParams _zAxisBase = new();
        [ObservableProperty] private MotionParams _firstPass = new();
        [ObservableProperty] private MotionParams _secondPass = new();
        [ObservableProperty] private AxisParamConfig _xAxis = new() { AxisNo = 1, PulsesPerMillimeter = 1d, UseActualPositionFeedback = false };
        [ObservableProperty] private AxisParamConfig _yAxis = new() { AxisNo = 2, PulsesPerMillimeter = 1d, UseActualPositionFeedback = false };
        [ObservableProperty] private AxisParamConfig _zAxis = new() { AxisNo = 3, PulsesPerMillimeter = 1d, UseActualPositionFeedback = false };

        [ObservableProperty] private double _fastMovePos = -22.0;
        [ObservableProperty] private double _fastMoveSpeed = 9.0;
        [ObservableProperty] private double _slowMoveDist = -12.0;
        [ObservableProperty] private double _slowMoveSpeed = 0.7;

        [ObservableProperty] private double _homeSearchSpeed = 3.0;
        [ObservableProperty] private bool _isIoHome;
        [ObservableProperty] private bool _isLatch;
        [ObservableProperty] private bool _isGratingHome;

        [ObservableProperty] private PortItem _xLimitPort = new() { PortIndex = 14 };
        [ObservableProperty] private PortItem _yLimitPort = new() { PortIndex = 14 };
        private AdtHomingConfig _adtHoming = new();
        [ObservableProperty] private int _redLightPort = 14;

        [ObservableProperty] private bool _isDebugMode;

        [ObservableProperty] private int _detectionOffsetThreshold;

        [ObservableProperty] private int _secondPassOffsetThreshold = 35;

        [ObservableProperty] private bool _scanUseBrightFieldDetector = true;
        [ObservableProperty] private int _scanDetectMinArea = 3;
        [ObservableProperty] private int _scanDetectMaxArea = 600;
        [ObservableProperty] private int _scanDetectThreshold = 95;
        [ObservableProperty] private double _scanDetectCircularity = 0.5;
        [ObservableProperty] private int _scanDetectMorphologySize = 13;
        [ObservableProperty] private double _scanDeduplicateToleranceMm = 0.08;

        public ObservableCollection<DetectionRingItem> DetectionRingItems { get; } = new();

        public ObservableCollection<SecondPassDetectionItem> SecondPassDetectionItems { get; } = new();

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public AdtHomingConfig AdtHoming
        {
            get => _adtHoming;
            set => SetProperty(ref _adtHoming, value);
        }

        public string ModifiedParametersInfo => BuildModifiedParametersInfo();

        [RelayCommand]
        private void SaveConfig()
        {
            try
            {
                PersistCurrentConfig("配置已保存，并已更新运行时配置。");
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败：{ex.Message}";
            }
        }

        public void ApplyDetectedCamera(CameraConfig detectedCamera)
        {
            ArgumentNullException.ThrowIfNull(detectedCamera);

            Camera.CameraType = detectedCamera.CameraType;
            Camera.ConnectionString = detectedCamera.ConnectionString;
            Camera.PixelSizeX = detectedCamera.PixelSizeX;
            Camera.PixelSizeY = detectedCamera.PixelSizeY;
            Camera.FovWidth = detectedCamera.FovWidth;
            Camera.FovHeight = detectedCamera.FovHeight;
            Camera.SaveDirectory = detectedCamera.SaveDirectory;
            RefreshCameraConnectionOptions();

            PersistCurrentConfig($"已同步相机配置：{detectedCamera.CameraType} {detectedCamera.FovWidth}x{detectedCamera.FovHeight}");
        }

        public void ApplyCameraSaveDirectory(string? saveDirectory)
        {
            Camera.SaveDirectory = saveDirectory?.Trim() ?? string.Empty;
            PersistCurrentConfig("已更新默认保存目录。");
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
                    SaveDirectory = Camera.SaveDirectory
                },
                XyDrive = CloneMotionParams(XyDrive),
                ZAxisBase = CloneMotionParams(ZAxisBase),
                FirstPass = CloneMotionParams(FirstPass),
                SecondPass = CloneMotionParams(SecondPass),
                XAxis = CloneAxisParamConfig(XAxis),
                YAxis = CloneAxisParamConfig(YAxis),
                ZAxis = CloneAxisParamConfig(ZAxis),
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
                AdtHoming = CloneAdtHomingConfig(AdtHoming),
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
                    .ToList(),
                ScanUseBrightFieldDetector = ScanUseBrightFieldDetector,
                ScanDetectMinArea = ScanDetectMinArea,
                ScanDetectMaxArea = ScanDetectMaxArea,
                ScanDetectThreshold = ScanDetectThreshold,
                ScanDetectCircularity = ScanDetectCircularity,
                ScanDetectMorphologySize = ScanDetectMorphologySize,
                ScanDeduplicateToleranceMm = ScanDeduplicateToleranceMm
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
                SaveDirectory = config.Camera.SaveDirectory
            };

            XyDrive = CloneMotionParams(config.XyDrive);
            ZAxisBase = CloneMotionParams(config.ZAxisBase);
            FirstPass = CloneMotionParams(config.FirstPass);
            SecondPass = CloneMotionParams(config.SecondPass);
            XAxis = CloneAxisParamConfig(config.XAxis);
            YAxis = CloneAxisParamConfig(config.YAxis);
            ZAxis = CloneAxisParamConfig(config.ZAxis);

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
            AdtHoming = CloneAdtHomingConfig(config.AdtHoming);
            RedLightPort = config.RedLightPort;
            IsDebugMode = config.IsDebugMode;

            DetectionOffsetThreshold = config.DetectionOffsetThreshold;
            SecondPassOffsetThreshold = config.SecondPassOffsetThreshold;
            ScanUseBrightFieldDetector = config.ScanUseBrightFieldDetector;
            ScanDetectMinArea = config.ScanDetectMinArea;
            ScanDetectMaxArea = config.ScanDetectMaxArea;
            ScanDetectThreshold = config.ScanDetectThreshold;
            ScanDetectCircularity = config.ScanDetectCircularity;
            ScanDetectMorphologySize = config.ScanDetectMorphologySize;
            ScanDeduplicateToleranceMm = config.ScanDeduplicateToleranceMm;

            ReplaceDetectionRingItems(config.DetectionRingItems);
            ReplaceSecondPassDetectionItems(config.SecondPassDetectionItems);

            SubscribeNestedPropertyChanged();
            RefreshModifiedParametersInfo();
        }

        private void PersistCurrentConfig(string successMessage)
        {
            var config = BuildConfig();
            _configService.SaveWithArchive(config);
            ApplyMotionConfig(config);
            _originalConfig = config;
            RefreshModifiedParametersInfo();
            StatusMessage = successMessage;
        }

        private void ApplyMotionConfig(AppConfig config)
        {
            _motionService.ConfigureAxes(
                BuildAxisParam(config.XAxis),
                BuildAxisParam(config.YAxis),
                BuildAxisParam(config.ZAxis));

            if (_motionService.Hardware is HAL.MotionAdt8940 adt8940)
            {
                adt8940.ConfigureHoming(BuildAdtHomingOptions(config));
            }
        }

        private static HAL.AxisParam BuildAxisParam(AxisParamConfig axisConfig)
        {
            return new HAL.AxisParam(
                axisConfig.AxisNo,
                axisConfig.Velocity,
                axisConfig.Acceleration,
                axisConfig.Deceleration,
                axisConfig.LeftLimit,
                axisConfig.RightLimit,
                axisConfig.PulsesPerMillimeter > 0 ? axisConfig.PulsesPerMillimeter : 1d,
                axisConfig.UseActualPositionFeedback,
                axisConfig.InPositionTolerance);
        }

        private static HAL.MotionAdt8940.HomingOptions BuildAdtHomingOptions(AppConfig config)
        {
            var homing = config.AdtHoming ?? new AdtHomingConfig();
            return new HAL.MotionAdt8940.HomingOptions(
                config.HomeSearchSpeed,
                config.IsIoHome,
                config.IsLatch,
                config.IsGratingHome,
                BuildHomingPort(config.XLimitPort),
                BuildHomingPort(config.YLimitPort),
                BuildHomingPort(homing.ZLimitPort),
                BuildHomingPort(homing.XGratingPort),
                BuildHomingPort(homing.YGratingPort),
                homing.HomeTimeoutMs,
                homing.HomeBackoffMm,
                homing.ZHomeLiftMm,
                homing.ZHomeTowardPositiveDirection,
                homing.SlowHomeStartSpeed,
                homing.SlowHomeSpeed,
                homing.SlowHomeAcceleration,
                homing.GratingHomeStartSpeed,
                homing.GratingHomeSpeed,
                homing.GratingHomeAcceleration);
        }

        private static HAL.MotionAdt8940.HomingPort BuildHomingPort(PortItem port)
        {
            return new HAL.MotionAdt8940.HomingPort(port.PortIndex, ResolvePortIsNegative(port), port.IsLowLevelActive);
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
            XAxis.PropertyChanged += NestedObject_PropertyChanged;
            YAxis.PropertyChanged += NestedObject_PropertyChanged;
            ZAxis.PropertyChanged += NestedObject_PropertyChanged;
            XLimitPort.PropertyChanged += NestedObject_PropertyChanged;
            YLimitPort.PropertyChanged += NestedObject_PropertyChanged;
            AdtHoming.PropertyChanged += NestedObject_PropertyChanged;
            AdtHoming.ZLimitPort.PropertyChanged += NestedObject_PropertyChanged;
            AdtHoming.XGratingPort.PropertyChanged += NestedObject_PropertyChanged;
            AdtHoming.YGratingPort.PropertyChanged += NestedObject_PropertyChanged;

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
            XAxis.PropertyChanged -= NestedObject_PropertyChanged;
            YAxis.PropertyChanged -= NestedObject_PropertyChanged;
            ZAxis.PropertyChanged -= NestedObject_PropertyChanged;
            XLimitPort.PropertyChanged -= NestedObject_PropertyChanged;
            YLimitPort.PropertyChanged -= NestedObject_PropertyChanged;
            AdtHoming.PropertyChanged -= NestedObject_PropertyChanged;
            AdtHoming.ZLimitPort.PropertyChanged -= NestedObject_PropertyChanged;
            AdtHoming.XGratingPort.PropertyChanged -= NestedObject_PropertyChanged;
            AdtHoming.YGratingPort.PropertyChanged -= NestedObject_PropertyChanged;

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
            if (sender == Camera && e.PropertyName == nameof(Camera.CameraType))
            {
                RefreshCameraConnectionOptions();
            }

            RefreshModifiedParametersInfo();
        }

        private void RefreshCameraConnectionOptions()
        {
            this.CameraConnectionOptions = Camera.CameraType switch
            {
                "海康" => GetHikvisionCameraConnectionOptions(),
                "Basler" => BaslerCameraConnectionDefaults,
                _ => SimulatedCameraConnectionDefaults
            };

            if (!CameraConnectionOptions.Contains(Camera.ConnectionString, StringComparer.OrdinalIgnoreCase))
            {
                Camera.ConnectionString = CameraConnectionOptions.FirstOrDefault() ?? string.Empty;
            }
        }

        private IReadOnlyList<string> GetHikvisionCameraConnectionOptions()
        {
            var options = new List<string>();
            var sdkInitialized = false;

            try
            {
                var initRet = MyCamera.MV_CC_Initialize_NET();
                if (initRet != MyCamera.MV_OK)
                {
                    return HikvisionCameraConnectionDefaults;
                }

                sdkInitialized = true;

                var deviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST
                {
                    pDeviceInfo = new IntPtr[MyCamera.MV_MAX_DEVICE_NUM]
                };

                var enumRet = MyCamera.MV_CC_EnumDevices_NET(
                    (uint)(MyCamera.MV_GIGE_DEVICE
                        | MyCamera.MV_USB_DEVICE
                        | MyCamera.MV_GENTL_GIGE_DEVICE
                        | MyCamera.MV_GENTL_CAMERALINK_DEVICE
                        | MyCamera.MV_GENTL_CXP_DEVICE
                        | MyCamera.MV_GENTL_XOF_DEVICE),
                    ref deviceList);

                if (enumRet != MyCamera.MV_OK)
                {
                    return HikvisionCameraConnectionDefaults;
                }

                for (int i = 0; i < deviceList.nDeviceNum; i++)
                {
                    var device = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(deviceList.pDeviceInfo[i]);
                    var connectionString = GetHikvisionConnectionString(device);
                    if (!string.IsNullOrWhiteSpace(connectionString) && !options.Contains(connectionString, StringComparer.OrdinalIgnoreCase))
                    {
                        options.Add(connectionString);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (sdkInitialized)
                {
                    try
                    {
                        MyCamera.MV_CC_Finalize_NET();
                    }
                    catch
                    {
                    }
                }
            }

            return options
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() is { Count: > 0 } discoveredOptions
                    ? discoveredOptions
                    : HikvisionCameraConnectionDefaults;
        }

        private static string GetHikvisionConnectionString(MyCamera.MV_CC_DEVICE_INFO device)
        {
            if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
            {
                var info = (MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(device.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
                return $"GigE://{FormatIpAddress(info.nCurrentIp)};Serial={info.chSerialNumber}";
            }

            if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
            {
                var info = (MyCamera.MV_USB3_DEVICE_INFO_EX)MyCamera.ByteToStruct(device.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO_EX));
                return $"USB://{info.chSerialNumber}";
            }

            return string.Empty;
        }

        private static string FormatIpAddress(uint ipAddress)
        {
            var b1 = (ipAddress & 0xff000000) >> 24;
            var b2 = (ipAddress & 0x00ff0000) >> 16;
            var b3 = (ipAddress & 0x0000ff00) >> 8;
            var b4 = ipAddress & 0x000000ff;
            return $"{b1}.{b2}.{b3}.{b4}";
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

            AddMotionDiff(lines, "XY", _originalConfig.XyDrive, current.XyDrive);
            AddMotionDiff(lines, "Z轴基础", _originalConfig.ZAxisBase, current.ZAxisBase);
            AddMotionDiff(lines, "头道", _originalConfig.FirstPass, current.FirstPass);
            AddMotionDiff(lines, "二道", _originalConfig.SecondPass, current.SecondPass);
            AddAxisDiff(lines, "X轴", _originalConfig.XAxis, current.XAxis);
            AddAxisDiff(lines, "Y轴", _originalConfig.YAxis, current.YAxis);
            AddAxisDiff(lines, "Z轴", _originalConfig.ZAxis, current.ZAxis);

            AddIfChanged(lines, "快速移动位置", _originalConfig.FastMovePos, current.FastMovePos);
            AddIfChanged(lines, "快速速度", _originalConfig.FastMoveSpeed, current.FastMoveSpeed);
            AddIfChanged(lines, "慢速移动距离", _originalConfig.SlowMoveDist, current.SlowMoveDist);
            AddIfChanged(lines, "慢速速度", _originalConfig.SlowMoveSpeed, current.SlowMoveSpeed);
            AddIfChanged(lines, "搜索速度", _originalConfig.HomeSearchSpeed, current.HomeSearchSpeed);

            AddIfChanged(lines, "IO回零", _originalConfig.IsIoHome, current.IsIoHome);
            AddIfChanged(lines, "锁存", _originalConfig.IsLatch, current.IsLatch);
            AddIfChanged(lines, "光栅尺回零", _originalConfig.IsGratingHome, current.IsGratingHome);

            AddIfChanged(lines, "X机械零位端口", _originalConfig.XLimitPort.PortIndex, current.XLimitPort.PortIndex);
            AddIfChanged(lines, "X机械零位低电平有效", _originalConfig.XLimitPort.IsLowLevelActive, current.XLimitPort.IsLowLevelActive);
            AddIfChanged(lines, "X机械零位安装负方向", ResolvePortIsNegative(_originalConfig.XLimitPort), ResolvePortIsNegative(current.XLimitPort));
            AddIfChanged(lines, "Y机械零位端口", _originalConfig.YLimitPort.PortIndex, current.YLimitPort.PortIndex);
            AddIfChanged(lines, "Y机械零位低电平有效", _originalConfig.YLimitPort.IsLowLevelActive, current.YLimitPort.IsLowLevelActive);
            AddIfChanged(lines, "Y机械零位安装负方向", ResolvePortIsNegative(_originalConfig.YLimitPort), ResolvePortIsNegative(current.YLimitPort));
            AddIfChanged(lines, "Z机械零位端口", _originalConfig.AdtHoming.ZLimitPort.PortIndex, current.AdtHoming.ZLimitPort.PortIndex);
            AddIfChanged(lines, "Z机械零位低电平有效", _originalConfig.AdtHoming.ZLimitPort.IsLowLevelActive, current.AdtHoming.ZLimitPort.IsLowLevelActive);
            AddIfChanged(lines, "Z机械零位安装负方向", ResolvePortIsNegative(_originalConfig.AdtHoming.ZLimitPort), ResolvePortIsNegative(current.AdtHoming.ZLimitPort));
            AddIfChanged(lines, "X光栅零位端口", _originalConfig.AdtHoming.XGratingPort.PortIndex, current.AdtHoming.XGratingPort.PortIndex);
            AddIfChanged(lines, "X光栅零位低电平有效", _originalConfig.AdtHoming.XGratingPort.IsLowLevelActive, current.AdtHoming.XGratingPort.IsLowLevelActive);
            AddIfChanged(lines, "X光栅零位安装负方向", ResolvePortIsNegative(_originalConfig.AdtHoming.XGratingPort), ResolvePortIsNegative(current.AdtHoming.XGratingPort));
            AddIfChanged(lines, "Y光栅零位端口", _originalConfig.AdtHoming.YGratingPort.PortIndex, current.AdtHoming.YGratingPort.PortIndex);
            AddIfChanged(lines, "Y光栅零位低电平有效", _originalConfig.AdtHoming.YGratingPort.IsLowLevelActive, current.AdtHoming.YGratingPort.IsLowLevelActive);
            AddIfChanged(lines, "Y光栅零位安装负方向", ResolvePortIsNegative(_originalConfig.AdtHoming.YGratingPort), ResolvePortIsNegative(current.AdtHoming.YGratingPort));
            AddIfChanged(lines, "回零超时", _originalConfig.AdtHoming.HomeTimeoutMs, current.AdtHoming.HomeTimeoutMs);
            AddIfChanged(lines, "回零脱离距离 (mm)", _originalConfig.AdtHoming.HomeBackoffMm, current.AdtHoming.HomeBackoffMm);
            AddIfChanged(lines, "Z回零抬起距离 (mm)", _originalConfig.AdtHoming.ZHomeLiftMm, current.AdtHoming.ZHomeLiftMm);
            AddIfChanged(lines, "Z机械回零朝正方向", _originalConfig.AdtHoming.ZHomeTowardPositiveDirection, current.AdtHoming.ZHomeTowardPositiveDirection);
            AddIfChanged(lines, "慢速回零初速度", _originalConfig.AdtHoming.SlowHomeStartSpeed, current.AdtHoming.SlowHomeStartSpeed);
            AddIfChanged(lines, "慢速回零速度", _originalConfig.AdtHoming.SlowHomeSpeed, current.AdtHoming.SlowHomeSpeed);
            AddIfChanged(lines, "慢速回零加速度", _originalConfig.AdtHoming.SlowHomeAcceleration, current.AdtHoming.SlowHomeAcceleration);
            AddIfChanged(lines, "光栅回零初速度", _originalConfig.AdtHoming.GratingHomeStartSpeed, current.AdtHoming.GratingHomeStartSpeed);
            AddIfChanged(lines, "光栅回零速度", _originalConfig.AdtHoming.GratingHomeSpeed, current.AdtHoming.GratingHomeSpeed);
            AddIfChanged(lines, "光栅回零加速度", _originalConfig.AdtHoming.GratingHomeAcceleration, current.AdtHoming.GratingHomeAcceleration);
            AddIfChanged(lines, "红灯端口", _originalConfig.RedLightPort, current.RedLightPort);
            AddIfChanged(lines, "调试模式", _originalConfig.IsDebugMode, current.IsDebugMode);

            AddIfChanged(lines, "一道探测偏移阈值", _originalConfig.DetectionOffsetThreshold, current.DetectionOffsetThreshold);
            AddIfChanged(lines, "二道探测偏移阈值", _originalConfig.SecondPassOffsetThreshold, current.SecondPassOffsetThreshold);
            AddIfChanged(lines, "扫描检测-明场模式", _originalConfig.ScanUseBrightFieldDetector, current.ScanUseBrightFieldDetector);
            AddIfChanged(lines, "扫描检测-最小面积", _originalConfig.ScanDetectMinArea, current.ScanDetectMinArea);
            AddIfChanged(lines, "扫描检测-最大面积", _originalConfig.ScanDetectMaxArea, current.ScanDetectMaxArea);
            AddIfChanged(lines, "扫描检测-阈值", _originalConfig.ScanDetectThreshold, current.ScanDetectThreshold);
            AddIfChanged(lines, "扫描检测-圆度", _originalConfig.ScanDetectCircularity, current.ScanDetectCircularity);
            AddIfChanged(lines, "扫描检测-形态学核", _originalConfig.ScanDetectMorphologySize, current.ScanDetectMorphologySize);
            AddIfChanged(lines, "扫描检测-去重容差(mm)", _originalConfig.ScanDeduplicateToleranceMm, current.ScanDeduplicateToleranceMm);

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
        }

        private static void AddAxisDiff(List<string> lines, string prefix, AxisParamConfig oldValue, AxisParamConfig currentValue)
        {
            AddIfChanged(lines, $"{prefix}-轴号", oldValue.AxisNo, currentValue.AxisNo);
            AddIfChanged(lines, $"{prefix}-速度", oldValue.Velocity, currentValue.Velocity);
            AddIfChanged(lines, $"{prefix}-加速度", oldValue.Acceleration, currentValue.Acceleration);
            AddIfChanged(lines, $"{prefix}-减速度", oldValue.Deceleration, currentValue.Deceleration);
            AddIfChanged(lines, $"{prefix}-左限位", oldValue.LeftLimit, currentValue.LeftLimit);
            AddIfChanged(lines, $"{prefix}-右限位", oldValue.RightLimit, currentValue.RightLimit);
            AddIfChanged(lines, $"{prefix}-每mm脉冲数", oldValue.PulsesPerMillimeter, currentValue.PulsesPerMillimeter);
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
            };
        }

        private static AxisParamConfig CloneAxisParamConfig(AxisParamConfig source)
        {
            return new AxisParamConfig
            {
                AxisNo = source.AxisNo,
                Velocity = source.Velocity,
                Acceleration = source.Acceleration,
                Deceleration = source.Deceleration,
                LeftLimit = source.LeftLimit,
                RightLimit = source.RightLimit,
                PulsesPerMillimeter = source.PulsesPerMillimeter,
                UseActualPositionFeedback = source.UseActualPositionFeedback,
                InPositionTolerance = source.InPositionTolerance
            };
        }

        private static bool ResolvePortIsNegative(PortItem? port)
        {
            return port?.IsNegative ?? port?.IsLowLevelActive ?? false;
        }

        private static PortItem ClonePort(PortItem source)
        {
            return new PortItem
            {
                PortIndex = source.PortIndex,
                IsLowLevelActive = source.IsLowLevelActive,
                IsNegative = ResolvePortIsNegative(source)
            };
        }

        private static AdtHomingConfig CloneAdtHomingConfig(AdtHomingConfig? source)
        {
            source ??= new AdtHomingConfig();
            return new AdtHomingConfig
            {
                ZLimitPort = ClonePort(source.ZLimitPort),
                XGratingPort = ClonePort(source.XGratingPort),
                YGratingPort = ClonePort(source.YGratingPort),
                HomeTimeoutMs = source.HomeTimeoutMs,
                HomeBackoffMm = source.HomeBackoffMm,
                ZHomeLiftMm = source.ZHomeLiftMm,
                ZHomeTowardPositiveDirection = source.ZHomeTowardPositiveDirection,
                SlowHomeStartSpeed = source.SlowHomeStartSpeed,
                SlowHomeSpeed = source.SlowHomeSpeed,
                SlowHomeAcceleration = source.SlowHomeAcceleration,
                GratingHomeStartSpeed = source.GratingHomeStartSpeed,
                GratingHomeSpeed = source.GratingHomeSpeed,
                GratingHomeAcceleration = source.GratingHomeAcceleration
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