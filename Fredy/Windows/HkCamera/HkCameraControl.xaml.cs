using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.ViewModels;
using Microsoft.Win32;
using MvCamCtrl.NET;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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

    private MyCamera? _camera;
    private bool _sdkInitialized;
    private bool _isGrabbing;
    private CancellationTokenSource? _grabCts;
    private Task? _grabTask;

    private IntPtr _grabBuffer = IntPtr.Zero;
    private uint _grabBufferSize;

    private IntPtr _convertBuffer = IntPtr.Zero;
    private uint _convertBufferSize;

    private IntPtr _lastFrameBuffer = IntPtr.Zero;
    private uint _lastFrameBufferSize;
    private MyCamera.MV_FRAME_OUT_INFO_EX _lastFrameInfo;

    public HkCameraControl()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

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
        if (DesignerProperties.GetIsInDesignMode(this))
        {
            return;
        }

        if (_sdkInitialized)
        {
            return;
        }

        try
        {
            //PrepareNativeLibrarySearchPath();

            //var diagnoseMessage = DiagnoseHikvisionDlls();
            //if (!string.IsNullOrWhiteSpace(diagnoseMessage))
            //{
            //    ShowError(diagnoseMessage, MyCamera.MV_E_LOAD_LIBRARY);
            //    return;
            //}

            var ret = MyCamera.MV_CC_Initialize_NET();
            if (ret != MyCamera.MV_OK)
            {
                if (ret == MyCamera.MV_E_LOAD_LIBRARY)
                {
                    ShowError($"初始化SDK失败: 0x{ret:X8}\n请确认 `MvCameraControl.dll` 及其依赖已放在程序根目录或 `DllImport` 目录。\n当前程序目录: {AppContext.BaseDirectory}", ret);
                }
                else
                {
                    ShowError("初始化SDK失败", ret);
                }

                return;
            }

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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CloseCurrentCamera();
        ReleaseBuffers();

        if (_sdkInitialized)
        {
            try
            {
                MyCamera.MV_CC_Finalize_NET();
            }
            catch
            {
            }

            _sdkInitialized = false;
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

    private void BnOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (_deviceList.nDeviceNum == 0 || cbDeviceList.SelectedIndex < 0)
        {
            ShowError("请先选择设备", 0);
            return;
        }

        CloseCurrentCamera();

        var index = cbDeviceList.SelectedIndex;
        var device = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(_deviceList.pDeviceInfo[index]);

        _camera = new MyCamera();

        var nRet = _camera.MV_CC_CreateDevice_NET(ref device);
        if (nRet != MyCamera.MV_OK)
        {
            ShowError("创建设备失败", nRet);
            _camera = null;
            return;
        }

        nRet = _camera.MV_CC_OpenDevice_NET();
        if (nRet != MyCamera.MV_OK)
        {
            _camera.MV_CC_DestroyDevice_NET();
            _camera = null;
            ShowError("打开设备失败", nRet);
            return;
        }

        if (device.nTLayerType == MyCamera.MV_GIGE_DEVICE)
        {
            var packetSize = _camera.MV_CC_GetOptimalPacketSize_NET();
            if (packetSize > 0)
            {
                _camera.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize", packetSize);
            }
        }

        _camera.MV_CC_SetEnumValue_NET("AcquisitionMode", (uint)MyCamera.MV_CAM_ACQUISITION_MODE.MV_ACQ_MODE_CONTINUOUS);
        _camera.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);

        bnContinuesMode.IsChecked = true;
        SetCtrlWhenOpen();
        ReadParam();
        UpdateCurrentDeviceInfo(device);
        SyncOpenedCameraToConfig(device);
    }

    private void BnClose_Click(object? sender, RoutedEventArgs e)
    {
        CloseCurrentCamera();
    }

    private void BnStartGrab_Click(object? sender, RoutedEventArgs e)
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

        var payload = new MyCamera.MVCC_INTVALUE_EX();
        var nRet = _camera.MV_CC_GetIntValueEx_NET("PayloadSize", ref payload);
        if (nRet != MyCamera.MV_OK || payload.nCurValue <= 0)
        {
            ShowError("获取PayloadSize失败", nRet);
            return;
        }

        EnsureGrabBuffer((uint)payload.nCurValue);

        nRet = _camera.MV_CC_StartGrabbing_NET();
        if (nRet != MyCamera.MV_OK)
        {
            ShowError("开始采集失败", nRet);
            return;
        }

        _isGrabbing = true;
        _grabCts = new CancellationTokenSource();
        _grabTask = Task.Run(() => GrabLoop(_grabCts.Token));

        SetCtrlWhenStartGrab();
    }

    private async void BnStopGrab_Click(object? sender, RoutedEventArgs e)
    {
        await StopGrabbingAsync();
        SetCtrlWhenStopGrab();
    }

    private void BnContinuesMode_Checked(object? sender, RoutedEventArgs e)
    {
        if (_camera is null || bnContinuesMode.IsChecked != true)
        {
            return;
        }

        _camera.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);
        cbSoftTrigger.IsEnabled = false;
        bnTriggerExec.IsEnabled = false;
    }

    private void BnTriggerMode_Checked(object? sender, RoutedEventArgs e)
    {
        if (_camera is null || bnTriggerMode.IsChecked != true)
        {
            return;
        }

        _camera.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_ON);
        cbSoftTrigger.IsEnabled = true;

        if (cbSoftTrigger.IsChecked == true)
        {
            _camera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_SOFTWARE);
            bnTriggerExec.IsEnabled = _isGrabbing;
        }
        else
        {
            _camera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_LINE0);
            bnTriggerExec.IsEnabled = false;
        }
    }

    private void CbSoftTrigger_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_camera is null || bnTriggerMode.IsChecked != true)
        {
            return;
        }

        if (cbSoftTrigger.IsChecked == true)
        {
            _camera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_SOFTWARE);
            bnTriggerExec.IsEnabled = _isGrabbing;
        }
        else
        {
            _camera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_LINE0);
            bnTriggerExec.IsEnabled = false;
        }
    }

    private void BnTriggerExec_Click(object? sender, RoutedEventArgs e)
    {
        if (_camera is null)
        {
            return;
        }

        var nRet = _camera.MV_CC_SetCommandValue_NET("TriggerSoftware");
        if (nRet != MyCamera.MV_OK)
        {
            ShowError("软触发失败", nRet);
        }
    }

    private void BnGetParam_Click(object? sender, RoutedEventArgs e)
    {
        ReadParam();
    }

    private void BnSetParam_Click(object? sender, RoutedEventArgs e)
    {
        if (_camera is null)
        {
            return;
        }

        if (!float.TryParse(tbExposure.Text, out var exposure)
            || !float.TryParse(tbGain.Text, out var gain)
            || !float.TryParse(tbFrameRate.Text, out var frameRate))
        {
            ShowError("参数格式错误", 0);
            return;
        }

        _camera.MV_CC_SetEnumValue_NET("ExposureAuto", 0);
        var nRet = _camera.MV_CC_SetFloatValue_NET("ExposureTime", exposure);
        if (nRet != MyCamera.MV_OK)
        {
            ShowError("设置曝光失败", nRet);
            return;
        }

        _camera.MV_CC_SetEnumValue_NET("GainAuto", 0);
        nRet = _camera.MV_CC_SetFloatValue_NET("Gain", gain);
        if (nRet != MyCamera.MV_OK)
        {
            ShowError("设置增益失败", nRet);
            return;
        }

        nRet = _camera.MV_CC_SetFloatValue_NET("AcquisitionFrameRate", frameRate);
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

        if (_grabCts is not null)
        {
            _grabCts.Cancel();
        }

        if (_grabTask is not null)
        {
            try
            {
                await _grabTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }

            _grabTask = null;
        }

        _grabCts?.Dispose();
        _grabCts = null;

        if (_camera is not null)
        {
            _camera.MV_CC_StopGrabbing_NET();
        }
    }

    private async void CloseCurrentCamera()
    {
        await StopGrabbingAsync();

        if (_camera is not null)
        {
            _camera.MV_CC_CloseDevice_NET();
            _camera.MV_CC_DestroyDevice_NET();
            _camera = null;
        }

        ClearCurrentDeviceInfo();
        SetCtrlWhenClose();
    }

    private void GrabLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_camera is null)
            {
                return;
            }

            var frameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();
            var nRet = _camera.MV_CC_GetOneFrameTimeout_NET(_grabBuffer, _grabBufferSize, ref frameInfo, 500);
            if (nRet == MyCamera.MV_E_NODATA)
            {
                continue;
            }

            if (nRet != MyCamera.MV_OK)
            {
                continue;
            }

            lock (_frameLock)
            {
                if (_lastFrameBuffer == IntPtr.Zero || frameInfo.nFrameLen > _lastFrameBufferSize)
                {
                    if (_lastFrameBuffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(_lastFrameBuffer);
                    }

                    _lastFrameBuffer = Marshal.AllocHGlobal((int)frameInfo.nFrameLen);
                    _lastFrameBufferSize = frameInfo.nFrameLen;
                }

                _lastFrameInfo = frameInfo;
                CopyMemory(_lastFrameBuffer, _grabBuffer, frameInfo.nFrameLen);
            }

            var bitmap = ConvertToBitmapSource(frameInfo);
            if (bitmap is null)
            {
                continue;
            }

            Dispatcher.Invoke(() => displayArea.ImageSource = bitmap);
        }
    }

    private BitmapSource? ConvertToBitmapSource(MyCamera.MV_FRAME_OUT_INFO_EX frameInfo)
    {
        if (frameInfo.nWidth <= 0 || frameInfo.nHeight <= 0 || frameInfo.nFrameLen <= 0)
        {
            return null;
        }

        if (IsMonoPixel(frameInfo.enPixelType))
        {
            var size = frameInfo.nWidth * frameInfo.nHeight;
            var data = new byte[size];
            Marshal.Copy(_grabBuffer, data, 0, data.Length);
            var image = BitmapSource.Create(frameInfo.nWidth, frameInfo.nHeight, 96, 96, PixelFormats.Gray8, null, data, frameInfo.nWidth);
            image.Freeze();
            return image;
        }

        var dstSize = (uint)(frameInfo.nWidth * frameInfo.nHeight * 3);
        EnsureConvertBuffer(dstSize);

        if (_camera is null)
        {
            return null;
        }

        var convert = new MyCamera.MV_PIXEL_CONVERT_PARAM
        {
            nWidth = frameInfo.nWidth,
            nHeight = frameInfo.nHeight,
            enSrcPixelType = frameInfo.enPixelType,
            pSrcData = _grabBuffer,
            nSrcDataLen = frameInfo.nFrameLen,
            enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed,
            pDstBuffer = _convertBuffer,
            nDstBufferSize = dstSize,
            nDstLen = 0,
            nRes = new uint[4]
        };

        var nRet = _camera.MV_CC_ConvertPixelType_NET(ref convert);
        if (nRet != MyCamera.MV_OK || convert.nDstLen <= 0)
        {
            return null;
        }

        var buffer = new byte[convert.nDstLen];
        Marshal.Copy(_convertBuffer, buffer, 0, buffer.Length);
        var bitmap = BitmapSource.Create(frameInfo.nWidth, frameInfo.nHeight, 96, 96, PixelFormats.Bgr24, null, buffer, frameInfo.nWidth * 3);
        bitmap.Freeze();
        return bitmap;
    }

    private void ReadParam()
    {
        if (_camera is null)
        {
            return;
        }

        var param = new MyCamera.MVCC_FLOATVALUE();

        if (_camera.MV_CC_GetFloatValue_NET("ExposureTime", ref param) == MyCamera.MV_OK)
        {
            tbExposure.Text = param.fCurValue.ToString("F1");
        }

        if (_camera.MV_CC_GetFloatValue_NET("Gain", ref param) == MyCamera.MV_OK)
        {
            tbGain.Text = param.fCurValue.ToString("F1");
        }

        if (_camera.MV_CC_GetFloatValue_NET("ResultingFrameRate", ref param) == MyCamera.MV_OK)
        {
            tbFrameRate.Text = param.fCurValue.ToString("F1");
        }
    }

    private void SaveImage(MyCamera.MV_SAVE_IAMGE_TYPE imageType, string extension)
    {
        if (_camera is null || !_isGrabbing)
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

        lock (_frameLock)
        {
            if (_lastFrameInfo.nFrameLen == 0 || _lastFrameBuffer == IntPtr.Zero)
            {
                ShowError("没有可保存的图像", 0);
                return;
            }

            var saveParam = new MyCamera.MV_SAVE_IMG_TO_FILE_PARAM
            {
                enImageType = imageType,
                enPixelType = _lastFrameInfo.enPixelType,
                pData = _lastFrameBuffer,
                nDataLen = _lastFrameInfo.nFrameLen,
                nWidth = _lastFrameInfo.nWidth,
                nHeight = _lastFrameInfo.nHeight,
                nQuality = imageType == MyCamera.MV_SAVE_IAMGE_TYPE.MV_Image_Jpeg ? 80u : 0u,
                iMethodValue = 2,
                nRes = new uint[8],
                pImagePath = savePath
            };

            var nRet = _camera.MV_CC_SaveImageToFile_NET(ref saveParam);
            if (nRet != MyCamera.MV_OK)
            {
                ShowError("保存图像失败", nRet);
                return;
            }
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
        if (_camera is null)
        {
            return fallback;
        }

        var value = new MyCamera.MVCC_INTVALUE_EX();
        return _camera.MV_CC_GetIntValueEx_NET(key, ref value) == MyCamera.MV_OK && value.nCurValue > 0
            ? (int)value.nCurValue
            : fallback;
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

    private void EnsureGrabBuffer(uint required)
    {
        if (_grabBuffer != IntPtr.Zero && _grabBufferSize >= required)
        {
            return;
        }

        if (_grabBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_grabBuffer);
        }

        _grabBuffer = Marshal.AllocHGlobal((int)required);
        _grabBufferSize = required;
    }

    private void EnsureConvertBuffer(uint required)
    {
        if (_convertBuffer != IntPtr.Zero && _convertBufferSize >= required)
        {
            return;
        }

        if (_convertBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_convertBuffer);
        }

        _convertBuffer = Marshal.AllocHGlobal((int)required);
        _convertBufferSize = required;
    }

    private void ReleaseBuffers()
    {
        if (_grabBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_grabBuffer);
            _grabBuffer = IntPtr.Zero;
            _grabBufferSize = 0;
        }

        if (_convertBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_convertBuffer);
            _convertBuffer = IntPtr.Zero;
            _convertBufferSize = 0;
        }

        if (_lastFrameBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_lastFrameBuffer);
            _lastFrameBuffer = IntPtr.Zero;
            _lastFrameBufferSize = 0;
            _lastFrameInfo = default;
        }
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
