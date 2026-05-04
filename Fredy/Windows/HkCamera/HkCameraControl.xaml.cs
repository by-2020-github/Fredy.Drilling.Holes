using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.ViewModels;
using HAL;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MvCamCtrl.NET;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fredy.Drilling.Holes.Views;

public partial class HkCameraControl : UserControl
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
    private static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

    private readonly object _frameLock = new();
    private MyCamera.MV_CC_DEVICE_INFO_LIST _deviceList = new() { pDeviceInfo = new IntPtr[MyCamera.MV_MAX_DEVICE_NUM] };

    private readonly HkCamera? _camera;
    private CameraArgs? _lastFrame;
    private string? _currentConnectionString;
    private bool _sdkInitialized;
    private bool _isGrabbing;
    private readonly SemaphoreSlim _releaseLock = new(1, 1);
    private Window? _ownerWindow;

    private bool _sdkAcquired;

    public HkCameraControl()
    {
        InitializeComponent();

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            _camera = App.ServiceProvider.GetService<ICamera>() as HkCamera;
        }

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;

        bnEnum.Click += (_, _) => DeviceListAcq();
        bnOpen.Click += BnOpen_Click;
        bnClose.Click += BnClose_Click;
        bnBrowseSaveFolder.Click += BnBrowseSaveFolder_Click;
        tbSaveDirectory.LostKeyboardFocus += TbSaveDirectory_LostKeyboardFocus;

        bnContinuesMode.Checked += BnContinuesMode_Checked;
        bnTriggerMode.Checked += BnTriggerMode_Checked;

        bnStartGrab.Click += BnStartGrab_Click;
        bnStopGrab.Click += BnStopGrab_Click;

        cbSoftTrigger.Checked += CbSoftTrigger_CheckedChanged;
        cbSoftTrigger.Unchecked += CbSoftTrigger_CheckedChanged;
        bnTriggerExec.Click += BnTriggerExec_Click;

        bnGetParam.Click += BnGetParam_Click;
        bnSetParam.Click += BnSetParam_Click;

        bnSaveBmp.Click += (_, _) => SaveImage(MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Bmp, ".bmp");
        bnSaveJpg.Click += (_, _) => SaveImage(MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Jpeg, ".jpg");
        bnSaveTiff.Click += (_, _) => SaveImage(MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Tif, ".tif");
        bnSavePng.Click += (_, _) => SaveImage(MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Png, ".png");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookOwnerWindow();
        EnsureSdkInitialized();
        _ = TryAttachConfiguredCameraAsync();
    }

    private static void PrepareNativeLibrarySearchPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            baseDir,
            Path.Combine(baseDir, "DllImport"),
            Path.Combine(baseDir, "x64"),
            Path.Combine(baseDir, "x86")
        };

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in candidates)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            if (path.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            path = dir + ";" + path;
        }

        Environment.SetEnvironmentVariable("PATH", path);
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnhookOwnerWindow();
        await ReleaseCameraResourcesAsync();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this) || !IsLoaded)
        {
            return;
        }

        if (e.NewValue is true)
        {
            HookOwnerWindow();
            EnsureSdkInitialized();
            return;
        }

        _ = ReleaseCameraResourcesAsync();
    }

    private void HookOwnerWindow()
    {
        var window = Window.GetWindow(this);
        if (ReferenceEquals(_ownerWindow, window))
        {
            return;
        }

        UnhookOwnerWindow();
        _ownerWindow = window;
        if (_ownerWindow is not null)
        {
            _ownerWindow.Closed += OwnerWindow_Closed;
        }
    }

    private void UnhookOwnerWindow()
    {
        if (_ownerWindow is null)
        {
            return;
        }

        _ownerWindow.Closed -= OwnerWindow_Closed;
        _ownerWindow = null;
    }

    private async void OwnerWindow_Closed(object? sender, EventArgs e)
    {
        await ReleaseCameraResourcesAsync();
    }

    private void EnsureSdkInitialized()
    {
        if (DesignerProperties.GetIsInDesignMode(this) || _sdkInitialized)
        {
            return;
        }

        try
        {
            HkSdkManager.Acquire();
            _sdkAcquired = true;
            _sdkInitialized = true;
            LoadSaveDirectoryFromConfig();
            DeviceListAcq();
        }
        catch (DllNotFoundException ex)
        {
            ShowError($"初始化SDK失败，未找到海康运行库: {ex.Message}", MyCamera.MV_E_LOAD_LIBRARY);
        }
        catch (BadImageFormatException ex)
        {
            ShowError($"初始化SDK失败，运行库位数不匹配: {ex.Message}", MyCamera.MV_E_LOAD_LIBRARY);
        }
        catch (Exception ex)
        {
            ShowError($"初始化SDK异常: {ex.Message}", MyCamera.MV_E_UNKNOW);
        }
    }

    private async Task ReleaseCameraResourcesAsync()
    {
        await _releaseLock.WaitAsync();
        try
        {
            await StopGrabbingAsync();

            _currentConnectionString = null;

            if (_sdkAcquired)
            {
                try
                {
                    HkSdkManager.Release();
                }
                catch
                {
                }

                _sdkAcquired = false;
                _sdkInitialized = false;
            }

            _deviceList.nDeviceNum = 0;
            cbDeviceList.Items.Clear();
            displayArea.ImageSource = null;
            ClearCurrentDeviceInfo();
            SetCtrlWhenClose();
        }
        finally
        {
            _releaseLock.Release();
        }
    }

    private void DeviceListAcq()
    {
        cbDeviceList.Items.Clear();

        _deviceList.nDeviceNum = 0;
        _deviceList.pDeviceInfo ??= new IntPtr[MyCamera.MV_MAX_DEVICE_NUM];

        int nRet;
        try
        {
            nRet = MyCamera.MV_CC_EnumDevices_NET(
                (uint)(MyCamera.MV_GIGE_DEVICE
                    | MyCamera.MV_USB_DEVICE
                    | MyCamera.MV_GENTL_GIGE_DEVICE
                    | MyCamera.MV_GENTL_CAMERALINK_DEVICE
                    | MyCamera.MV_GENTL_CXP_DEVICE
                    | MyCamera.MV_GENTL_XOF_DEVICE),
                ref _deviceList);
        }
        catch (Exception ex)
        {
            ShowError($"枚举设备异常: {ex.Message}", MyCamera.MV_E_UNKNOW);
            return;
        }

        if (nRet != MyCamera.MV_OK)
        {
            ShowError("枚举设备失败", nRet);
            return;
        }

        for (int i = 0; i < _deviceList.nDeviceNum; i++)
        {
            var device = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(_deviceList.pDeviceInfo[i]);
            cbDeviceList.Items.Add(GetDeviceDisplayName(device));
        }

        if (_deviceList.nDeviceNum > 0)
        {
            cbDeviceList.SelectedIndex = 0;
        }
    }

    private void BnBrowseSaveFolder_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择默认保存文件夹"
        };

        if (!string.IsNullOrWhiteSpace(tbSaveDirectory.Text) && Directory.Exists(tbSaveDirectory.Text))
        {
            dialog.InitialDirectory = tbSaveDirectory.Text;
        }

        if (dialog.ShowDialog() == true)
        {
            tbSaveDirectory.Text = dialog.FolderName;
            PersistSaveDirectoryToConfig();
        }
    }

    private void TbSaveDirectory_LostKeyboardFocus(object? sender, RoutedEventArgs e)
    {
        PersistSaveDirectoryToConfig();
    }

    private async void BnOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (_deviceList.nDeviceNum == 0 || cbDeviceList.SelectedIndex < 0)
        {
            ShowError("请先选择设备", 0);
            return;
        }

        var index = cbDeviceList.SelectedIndex;
        var device = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(_deviceList.pDeviceInfo[index]);
        var connectionString = GetCameraConnectionString(device);

        await CloseCurrentCameraAsync();

        if (_camera is null || !_camera.Open(connectionString))
        {
            ShowError("打开设备失败", 0);
            return;
        }

        _currentConnectionString = connectionString;

        bnContinuesMode.IsChecked = true;
        SetCtrlWhenOpen();
        ReadParam();
        UpdateCurrentDeviceInfo(device);
        SyncOpenedCameraToConfig(device);
    }

    private async void BnClose_Click(object? sender, RoutedEventArgs e)
    {
        await CloseCurrentCameraAsync();
    }

    private async void BnStartGrab_Click(object? sender, RoutedEventArgs e)
    {
        if (_camera is null)
        {
            ShowError("设备未打开", 0);
            return;
        }

        if (_isGrabbing)
        {
            return;
        }

        var previewCamera = EnsurePreviewCamera();
        if (previewCamera is null)
        {
            ShowError("开始采集失败", 0);
            return;
        }

        try
        {
            previewCamera.ImageGrabbed -= PreviewCamera_ImageGrabbed;
            previewCamera.ImageGrabbed += PreviewCamera_ImageGrabbed;

            if (!previewCamera.IsContinuousGrabbing)
            {
                await Task.Run(() => previewCamera.StartContinuousGrab());
            }

            _isGrabbing = true;
        }
        catch (Exception ex)
        {
            previewCamera.ImageGrabbed -= PreviewCamera_ImageGrabbed;
            ShowError($"开始采集失败: {ex.Message}", 0);
            return;
        }

        SetCtrlWhenStartGrab();
    }

    private async void BnStopGrab_Click(object? sender, RoutedEventArgs e)
    {
        await StopGrabbingAsync();
        SetCtrlWhenStopGrab();
    }

    private void BnContinuesMode_Checked(object? sender, RoutedEventArgs e)
    {
        if (_camera is null || !_camera.IsConnected || bnContinuesMode.IsChecked != true)
        {
            return;
        }

        cbSoftTrigger.IsEnabled = false;
        bnTriggerExec.IsEnabled = false;
    }

    private void BnTriggerMode_Checked(object? sender, RoutedEventArgs e)
    {
        if (_camera is null || !_camera.IsConnected || bnTriggerMode.IsChecked != true)
        {
            return;
        }

        cbSoftTrigger.IsEnabled = true;

        if (cbSoftTrigger.IsChecked == true)
        {
            bnTriggerExec.IsEnabled = _isGrabbing;
        }
        else
        {
            bnTriggerExec.IsEnabled = false;
        }
    }

    private void CbSoftTrigger_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_camera is null || !_camera.IsConnected || bnTriggerMode.IsChecked != true)
        {
            return;
        }

        if (cbSoftTrigger.IsChecked == true)
        {
            bnTriggerExec.IsEnabled = _isGrabbing;
        }
        else
        {
            bnTriggerExec.IsEnabled = false;
        }
    }

    private void BnTriggerExec_Click(object? sender, RoutedEventArgs e)
    {
        if (_camera is null || !_camera.IsConnected)
        {
            return;
        }

        _ = TriggerPreviewFrameAsync();
    }

    private void BnGetParam_Click(object? sender, RoutedEventArgs e)
    {
        ReadParam();
    }

    private void BnSetParam_Click(object? sender, RoutedEventArgs e)
    {
        if (_camera is null || !_camera.IsConnected)
        {
            return;
        }

        var exposure = Convert.ToSingle(tbExposure.Value);
        var gain = Convert.ToSingle(tbGain.Value);
        var frameRate = Convert.ToSingle(tbFrameRate.Value);

        if (float.IsNaN(exposure) || float.IsNaN(gain) || float.IsNaN(frameRate))
        {
            ShowError("参数格式错误", 0);
            return;
        }

        var nRet = _camera.TrySetExposureTime(exposure);
        if (nRet != MyCamera.MV_OK)
        {
            ShowError("设置曝光失败", nRet);
            return;
        }

        nRet = _camera.TrySetGain(gain);
        if (nRet != MyCamera.MV_OK)
        {
            ShowError("设置增益失败", nRet);
            return;
        }

        nRet = _camera.TrySetFrameRate(frameRate);
        if (nRet != MyCamera.MV_OK)
        {
            ShowError("设置帧率失败", nRet);
        }
    }

    private async Task StopGrabbingAsync()
    {
        if (!_isGrabbing)
        {
            return;
        }

        _isGrabbing = false;

        if (_camera is not null)
        {
            try
            {
                if (_camera.IsContinuousGrabbing)
                {
                    await Task.Run(() => _camera.StopContinuousGrab());
                }
            }
            catch
            {
            }
            finally
            {
                _camera.ImageGrabbed -= PreviewCamera_ImageGrabbed;
            }
        }

        await Task.CompletedTask;
    }

    private async Task CloseCurrentCameraAsync()
    {
        await StopGrabbingAsync();

        _camera?.Close();

        _currentConnectionString = null;

        ClearCurrentDeviceInfo();
        SetCtrlWhenClose();
    }

    private void ReadParam()
    {
        if (_camera is null || !_camera.IsConnected)
        {
            return;
        }

        if (_camera.TryGetExposureTime(out var exposure))
        {
            tbExposure.Value = exposure;
        }

        if (_camera.TryGetGain(out var gain))
        {
            tbGain.Value = gain;
        }

        if (_camera.TryGetResultingFrameRate(out var frameRate))
        {
            tbFrameRate.Value = frameRate;
        }
    }

    private void SaveImage(MyCamera.MV_SAVE_IAMGE_TYPE imageType, string extension)
    {
        if (_camera is null || !_camera.IsConnected || !_isGrabbing)
        {
            ShowError("请先开始采集", 0);
            return;
        }

        string? savePath;
        try
        {
            savePath = GetSavePath(extension);
        }
        catch (Exception ex)
        {
            ShowError($"保存路径无效: {ex.Message}", 0);
            return;
        }

        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        CameraArgs? frame;
        lock (_frameLock)
        {
            frame = _lastFrame;
        }

        if (frame?.Data is null || frame.Width <= 0 || frame.Height <= 0)
        {
            ShowError("没有可保存的图像", 0);
            return;
        }

        try
        {
            using var mat = Tools.VisionUIHelper.CameraArgsToMat(frame);
            if (mat.Empty())
            {
                ShowError("没有可保存的图像", 0);
                return;
            }

            mat.SaveImage(savePath);
        }
        catch (Exception ex)
        {
            ShowError($"保存图像失败: {ex.Message}", 0);
            return;
        }

        ShowError($"保存成功\n{savePath}", 0);
    }

    private string? GetSavePath(string extension)
    {
        var timestampFileName = BuildTimestampedFileName(extension);
        var defaultDirectory = tbSaveDirectory.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(defaultDirectory))
        {
            Directory.CreateDirectory(defaultDirectory);
            return Path.Combine(defaultDirectory, timestampFileName);
        }

        var dialog = new SaveFileDialog
        {
            Title = "保存图像",
            FileName = timestampFileName,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true,
            Filter = BuildImageFilter(extension)
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var selectedDirectory = Path.GetDirectoryName(dialog.FileName);
        if (!string.IsNullOrWhiteSpace(selectedDirectory))
        {
            tbSaveDirectory.Text = selectedDirectory;
            PersistSaveDirectoryToConfig();
        }

        return dialog.FileName;
    }

    private static string BuildTimestampedFileName(string extension)
    {
        return $"{DateTime.Now:yyyyMMdd_HHmmss_fff}{extension}";
    }

    private static string BuildImageFilter(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".bmp" => "BMP 图像|*.bmp|所有文件|*.*",
            ".jpg" => "JPEG 图像|*.jpg|所有文件|*.*",
            ".tif" => "TIFF 图像|*.tif|所有文件|*.*",
            ".png" => "PNG 图像|*.png|所有文件|*.*",
            _ => "图像文件|*.*"
        };
    }

    private void SyncOpenedCameraToConfig(MyCamera.MV_CC_DEVICE_INFO device)
    {
        if (DataContext is not ConfigViewModel viewModel)
        {
            return;
        }

        var detectedCamera = BuildDetectedCameraConfig(device, viewModel.Camera);
        viewModel.ApplyDetectedCamera(detectedCamera);
    }

    private void LoadSaveDirectoryFromConfig()
    {
        if (DataContext is ConfigViewModel viewModel)
        {
            tbSaveDirectory.Text = viewModel.Camera.SaveDirectory;
        }
    }

    private void PersistSaveDirectoryToConfig()
    {
        if (DataContext is ConfigViewModel viewModel)
        {
            viewModel.ApplyCameraSaveDirectory(tbSaveDirectory.Text);
        }
    }

    private CameraConfig BuildDetectedCameraConfig(MyCamera.MV_CC_DEVICE_INFO device, CameraConfig currentCamera)
    {
        var width = GetCameraIntValue("Width", currentCamera.FovWidth);
        var height = GetCameraIntValue("Height", currentCamera.FovHeight);

        return new CameraConfig
        {
            CameraType = GetCameraType(device),
            ConnectionString = GetCameraConnectionString(device),
            PixelSizeX = currentCamera.PixelSizeX,
            PixelSizeY = currentCamera.PixelSizeY,
            FovWidth = width,
            FovHeight = height,
            SaveDirectory = currentCamera.SaveDirectory
        };
    }

    private int GetCameraIntValue(string key, int fallback)
    {
        return fallback;
    }

    private static string GetCameraType(MyCamera.MV_CC_DEVICE_INFO device)
    {
        return device.nTLayerType switch
        {
            MyCamera.MV_GIGE_DEVICE => "海康 GigE",
            MyCamera.MV_USB_DEVICE => "海康 USB",
            _ => "海康 GigE"
        };
    }

    private string GetCameraConnectionString(MyCamera.MV_CC_DEVICE_INFO device)
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

        return cbDeviceList.SelectedItem?.ToString() ?? "Unknown";
    }

    private static string FormatIpAddress(uint ipAddress)
    {
        var b1 = (ipAddress & 0xff000000) >> 24;
        var b2 = (ipAddress & 0x00ff0000) >> 16;
        var b3 = (ipAddress & 0x0000ff00) >> 8;
        var b4 = ipAddress & 0x000000ff;
        return $"{b1}.{b2}.{b3}.{b4}";
    }

    private void UpdateCurrentDeviceInfo(MyCamera.MV_CC_DEVICE_INFO device)
    {
        txtDeviceModel.Text = GetDeviceModel(device);
        txtDeviceSerial.Text = GetDeviceSerial(device);
        txtDeviceIp.Text = GetDeviceIp(device);
        txtDeviceConnection.Text = GetCameraConnectionString(device);
    }

    private void ClearCurrentDeviceInfo()
    {
        txtDeviceModel.Text = "-";
        txtDeviceSerial.Text = "-";
        txtDeviceIp.Text = "-";
        txtDeviceConnection.Text = "-";
    }

    private static string GetDeviceModel(MyCamera.MV_CC_DEVICE_INFO device)
    {
        if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
        {
            var info = (MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(device.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
            return $"{info.chManufacturerName} {info.chModelName}".Trim();
        }

        if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
        {
            var info = (MyCamera.MV_USB3_DEVICE_INFO_EX)MyCamera.ByteToStruct(device.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO_EX));
            return $"{info.chManufacturerName} {info.chModelName}".Trim();
        }

        return "-";
    }

    private static string GetDeviceSerial(MyCamera.MV_CC_DEVICE_INFO device)
    {
        if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
        {
            var info = (MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(device.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
            return info.chSerialNumber;
        }

        if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
        {
            var info = (MyCamera.MV_USB3_DEVICE_INFO_EX)MyCamera.ByteToStruct(device.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO_EX));
            return info.chSerialNumber;
        }

        return "-";
    }

    private static string GetDeviceIp(MyCamera.MV_CC_DEVICE_INFO device)
    {
        if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
        {
            var info = (MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(device.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
            return FormatIpAddress(info.nCurrentIp);
        }

        return "-";
    }

    private static bool IsMonoPixel(MyCamera.MvGvspPixelType pixelType)
    {
        return pixelType is MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8
            or MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10
            or MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono10_Packed
            or MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12
            or MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono12_Packed
            or MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono14
            or MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono16;
    }

    private static string GetDeviceDisplayName(MyCamera.MV_CC_DEVICE_INFO device)
    {
        if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
        {
            var info = (MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(device.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
            return BuildDisplayName("GEV", info.chUserDefinedName, info.chManufacturerName, info.chModelName, info.chSerialNumber);
        }

        if (device.nTLayerType == MyCamera.MV_USB_DEVICE)
        {
            var info = (MyCamera.MV_USB3_DEVICE_INFO_EX)MyCamera.ByteToStruct(device.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO_EX));
            return BuildDisplayName("U3V", info.chUserDefinedName, info.chManufacturerName, info.chModelName, info.chSerialNumber);
        }

        return $"TL:{device.nTLayerType}";
    }

    private static string BuildDisplayName(string prefix, byte[] userNameBytes, string manufacturer, string model, string serial)
    {
        var userName = DecodeUserName(userNameBytes);
        if (!string.IsNullOrWhiteSpace(userName))
        {
            return $"{prefix}: {userName} ({serial})";
        }

        return $"{prefix}: {manufacturer} {model} ({serial})";
    }

    private static string DecodeUserName(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes[0] == 0)
        {
            return string.Empty;
        }

        var text = MyCamera.IsTextUTF8(bytes)
            ? Encoding.UTF8.GetString(bytes)
            : Encoding.Default.GetString(bytes);

        text = Regex.Unescape(text);
        var index = text.IndexOf('\0');
        if (index >= 0)
        {
            text = text[..index];
        }

        return text.Trim();
    }

    private async Task TryAttachConfiguredCameraAsync()
    {
        if (_camera is null)
        {
            return;
        }

        var configConnectionString = _camera.ConnectionString
            ?? (DataContext as ConfigViewModel)?.Camera.ConnectionString;

        if (string.IsNullOrWhiteSpace(configConnectionString))
        {
            return;
        }

        var targetIndex = FindDeviceIndexByConnectionString(configConnectionString);
        if (targetIndex < 0)
        {
            return;
        }

        cbDeviceList.SelectedIndex = targetIndex;
        await OpenConfiguredCameraAsync(targetIndex, configConnectionString);
    }

    private async Task OpenConfiguredCameraAsync(int index, string connectionString)
    {
        await CloseCurrentCameraAsync();

        var device = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(_deviceList.pDeviceInfo[index]);

        if (_camera is null || !_camera.Open(connectionString))
        {
            return;
        }

        _currentConnectionString = connectionString;

        SetCtrlWhenOpen();
        ReadParam();
        UpdateCurrentDeviceInfo(device);
    }

    private int FindDeviceIndexByConnectionString(string connectionString)
    {
        for (int i = 0; i < _deviceList.nDeviceNum; i++)
        {
            var device = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(_deviceList.pDeviceInfo[i]);
            if (string.Equals(GetCameraConnectionString(device), connectionString, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private HkCamera? EnsurePreviewCamera()
    {
        if (_camera is null)
        {
            return null;
        }

        if (_camera.IsConnected)
        {
            return _camera;
        }

        if (string.IsNullOrWhiteSpace(_currentConnectionString) || !_camera.Open(_currentConnectionString))
        {
            return null;
        }

        return _camera;
    }

    private async Task TriggerPreviewFrameAsync()
    {
        if (_camera is null || !_camera.IsConnected)
        {
            return;
        }

        try
        {
            if (_camera.IsContinuousGrabbing)
            {
                var triggerCamera = _camera as HkCamera;
                if (triggerCamera is null)
                {
                    return;
                }

                var triggerRet = triggerCamera.TriggerSoftware();
                if (triggerRet != MyCamera.MV_OK)
                {
                    throw new InvalidOperationException($"软触发失败，错误码: 0x{triggerRet:X8}");
                }

                return;
            }

            var frame = await _camera.GrabAsync();
            PreviewCamera_ImageGrabbed(_camera, frame);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => ShowError($"软触发失败: {ex.Message}", 0));
        }
    }

    private void PreviewCamera_ImageGrabbed(object? sender, CameraArgs e)
    {
        lock (_frameLock)
        {
            _lastFrame = new CameraArgs
            {
                Data = e.Data is null ? null : (byte[])e.Data.Clone(),
                Width = e.Width,
                Height = e.Height,
                Stride = e.Stride,
                Format = e.Format,
                FrameId = e.FrameId,
                Timestamp = e.Timestamp
            };
        }

        Dispatcher.Invoke(() =>
        {
            using var mat = Tools.VisionUIHelper.CameraArgsToMat(e);
            if (mat.Empty())
            {
                return;
            }

            displayArea.ImageSource = Tools.VisionUIHelper.MatToBitmapSource(mat);
        });
    }

    private void SetCtrlWhenOpen()
    {
        bnOpen.IsEnabled = false;
        bnClose.IsEnabled = true;

        bnStartGrab.IsEnabled = true;
        bnStopGrab.IsEnabled = false;

        bnContinuesMode.IsEnabled = true;
        bnTriggerMode.IsEnabled = true;
        cbSoftTrigger.IsEnabled = false;
        bnTriggerExec.IsEnabled = false;

        tbExposure.IsEnabled = true;
        tbGain.IsEnabled = true;
        tbFrameRate.IsEnabled = true;
        bnGetParam.IsEnabled = true;
        bnSetParam.IsEnabled = true;
    }

    private void SetCtrlWhenClose()
    {
        bnOpen.IsEnabled = true;
        bnClose.IsEnabled = false;

        bnStartGrab.IsEnabled = false;
        bnStopGrab.IsEnabled = false;

        bnContinuesMode.IsEnabled = false;
        bnTriggerMode.IsEnabled = false;
        cbSoftTrigger.IsEnabled = false;
        bnTriggerExec.IsEnabled = false;

        tbExposure.IsEnabled = false;
        tbGain.IsEnabled = false;
        tbFrameRate.IsEnabled = false;
        bnGetParam.IsEnabled = false;
        bnSetParam.IsEnabled = false;

        bnSaveBmp.IsEnabled = false;
        bnSaveJpg.IsEnabled = false;
        bnSaveTiff.IsEnabled = false;
        bnSavePng.IsEnabled = false;
    }

    private void SetCtrlWhenStartGrab()
    {
        bnStartGrab.IsEnabled = false;
        bnStopGrab.IsEnabled = true;

        bnSaveBmp.IsEnabled = true;
        bnSaveJpg.IsEnabled = true;
        bnSaveTiff.IsEnabled = true;
        bnSavePng.IsEnabled = true;

        if (bnTriggerMode.IsChecked == true && cbSoftTrigger.IsChecked == true)
        {
            bnTriggerExec.IsEnabled = true;
        }
    }

    private void SetCtrlWhenStopGrab()
    {
        bnStartGrab.IsEnabled = true;
        bnStopGrab.IsEnabled = false;

        bnTriggerExec.IsEnabled = false;

        bnSaveBmp.IsEnabled = false;
        bnSaveJpg.IsEnabled = false;
        bnSaveTiff.IsEnabled = false;
        bnSavePng.IsEnabled = false;
    }

    private static void ShowError(string message, int errorCode)
    {
        var text = errorCode == 0 ? message : $"{message}，错误码: 0x{errorCode:X8}";
        MessageBox.Show(text, "提示", MessageBoxButton.OK, errorCode == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static string? DiagnoseHikvisionDlls()
    {
        var baseDir = AppContext.BaseDirectory;
        var searchDirs = new List<string>
        {
            baseDir,
            Path.Combine(baseDir, "DllImport"),
            Path.Combine(baseDir, "x64"),
            Path.Combine(baseDir, "x86")
        };

        var requiredDlls = new[]
        {
            "MvCameraControl.dll"
        };

        var missing = new List<string>();
        var located = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in requiredDlls)
        {
            var foundPath = searchDirs
                .Where(Directory.Exists)
                .Select(dir => Path.Combine(dir, dll))
                .FirstOrDefault(File.Exists);

            if (string.IsNullOrWhiteSpace(foundPath))
            {
                missing.Add(dll);
                continue;
            }

            located[dll] = foundPath;
        }

        if (missing.Count > 0)
        {
            return "启动诊断：以下 DLL 缺失\n"
                + string.Join("\n", missing.Select(x => $"- {x}"))
                + $"\n\n程序目录: {baseDir}";
        }

        var nativePath = located["MvCameraControl.dll"];
        var module = LoadLibrary(nativePath);
        if (module == IntPtr.Zero)
        {
            var errorCode = Marshal.GetLastWin32Error();
            return $"启动诊断：`MvCameraControl.dll` 文件存在但加载失败。\n"
                 + $"- 路径: {nativePath}\n"
                 + $"- Win32Error: {errorCode}\n"
                 + "这通常表示其依赖库缺失（如 VC++ 运行库）或位数不匹配。";
        }

        FreeLibrary(module);
        return null;
    }
}
