using MvCamCtrl.NET;
using System;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace HAL
{
    public sealed class HkCamera : ICamera
    {
        private const int SingleGrabTimeoutMilliseconds = 3000;
        private static readonly ILogger Logger = Log.ForContext<HkCamera>();

        private readonly object _syncRoot = new();
        private readonly MyCamera.cbOutputExdelegate _imageCallback;

        private readonly AutoResetEvent _singleGrabLock = new(true);
        private readonly AutoResetEvent _singleGrabSignal = new(false);
        private MyCamera? _camera;
        private bool _isGrabbing;
        private bool _isContinuousGrabbing;
        private bool _isWaitingForSingleGrab;
        private volatile bool _isClosing;
        private CameraArgs? _singleGrabFrame;
        private Exception? _singleGrabException;
        private bool _sdkAcquired;
        private long _lastCallbackFrameId;
        private DateTime? _lastCallbackTimestamp;

        public HkCamera()
        {
            _imageCallback = OnImageGrabbed;
        }

        public bool IsConnected { get; private set; }

        public bool IsContinuousGrabbing => _isContinuousGrabbing;

        public string? ConnectionString { get; private set; }

        public event EventHandler<CameraArgs>? ImageGrabbed;

        public bool Open()
        {
            return Open(null);
        }

        public bool Open(string? connectionString)
        {
            lock (_syncRoot)
            {
                if (IsConnected)
                {
                    if (IsCameraHandleValid())
                    {
                        return true;
                    }

                    Close();
                }

                HkSdkManager.Acquire();
                _sdkAcquired = true;

                var deviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST
                {
                    nDeviceNum = 0,
                    pDeviceInfo = new IntPtr[256]
                };

                var layerType = (uint)(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE);
                var nRet = MyCamera.MV_CC_EnumDevices_NET(layerType, ref deviceList);
                if (nRet != MyCamera.MV_OK || deviceList.nDeviceNum < 1)
                {
                    ReleaseSdkIfNeeded();
                    return false;
                }

                var deviceInfo = GetTargetDeviceInfo(deviceList, connectionString);
                if (deviceInfo is null)
                {
                    ReleaseSdkIfNeeded();
                    return false;
                }

                _camera = new MyCamera();

                try
                {
                    var selectedDevice = deviceInfo.Value;
                    nRet = _camera.MV_CC_CreateDevice_NET(ref selectedDevice);
                    if (nRet != MyCamera.MV_OK)
                    {
                        Close();
                        return false;
                    }

                    nRet = _camera.MV_CC_OpenDevice_NET((uint)MyCamera.MV_ACCESS_Exclusive, 0);
                    if (nRet != MyCamera.MV_OK)
                    {
                        Close();
                        return false;
                    }

                    if (selectedDevice.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                    {
                        var packetSize = _camera.MV_CC_GetOptimalPacketSize_NET();
                        if (packetSize > 0)
                        {
                            _camera.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize", (uint)packetSize);
                        }
                    }

                    _camera.MV_CC_SetEnumValueByString_NET("TriggerMode", "On");
                    _camera.MV_CC_SetEnumValueByString_NET("TriggerSource", "Software");

                    nRet = _camera.MV_CC_RegisterImageCallBackEx_NET(_imageCallback, IntPtr.Zero);
                    if (nRet != MyCamera.MV_OK)
                    {
                        Close();
                        return false;
                    }

                    nRet = _camera.MV_CC_StartGrabbing_NET();
                    if (nRet != MyCamera.MV_OK)
                    {
                        Close();
                        return false;
                    }

                    _isGrabbing = true;

                    _isClosing = false;
                    _isContinuousGrabbing = false;
                    ConnectionString = BuildConnectionString(selectedDevice);
                    IsConnected = true;
                    return true;
                }
                catch
                {
                    Close();
                    throw;
                }
            }
        }

        public void Close()
        {
            _isClosing = true;

            lock (_syncRoot)
            {
                _isContinuousGrabbing = false;

                if (_camera is not null)
                {
                    if (_isGrabbing)
                    {
                        _camera.MV_CC_StopGrabbing_NET();
                        _isGrabbing = false;
                    }

                    _camera.MV_CC_CloseDevice_NET();
                    _camera.MV_CC_DestroyDevice_NET();
                    _camera = null;
                }

                ConnectionString = null;
                IsConnected = false;
                _singleGrabFrame = null;
                _isWaitingForSingleGrab = false;
                _singleGrabSignal.Set();
                ReleaseSdkIfNeeded();
            }
        }

        public CameraArgs Grab()
        {
            _singleGrabLock.WaitOne();

            try
            {
                lock (_syncRoot)
                {
                    EnsureCameraReady();

                    if (_isContinuousGrabbing)
                    {
                        throw new InvalidOperationException("Camera is in continuous grab mode.");
                    }

                    _singleGrabSignal.Reset();
                    _singleGrabFrame = null;
                    _singleGrabException = null;
                    _isWaitingForSingleGrab = true;

                    var triggerRet = _camera!.MV_CC_SetCommandValue_NET("TriggerSoftware");
                    if (triggerRet != MyCamera.MV_OK)
                    {
                        _isWaitingForSingleGrab = false;

                        if (_isClosing || !IsConnected || !_isGrabbing)
                        {
                            throw new OperationCanceledException("Camera trigger canceled because camera is closing.");
                        }

                        throw new InvalidOperationException($"Trigger failed: 0x{triggerRet:X8}");
                    }
                }

                if (!_singleGrabSignal.WaitOne(SingleGrabTimeoutMilliseconds))
                {
                    lock (_syncRoot)
                    {
                        _isWaitingForSingleGrab = false;
                        _singleGrabFrame = null;
                        _singleGrabException = null;

                        if (_isClosing || !IsConnected || _camera is null || !_isGrabbing)
                        {
                            throw new OperationCanceledException("Camera grab canceled because camera is closing.");
                        }

                        Logger.Warning("Camera single grab timed out waiting for image callback. Last callback frame id: {LastCallbackFrameId}, last callback timestamp: {LastCallbackTimestamp}, connection: {IsConnected}, grabbing: {IsGrabbing}",
                            _lastCallbackFrameId,
                            _lastCallbackTimestamp,
                            IsConnected,
                            _isGrabbing);
                    }

                    throw new TimeoutException("Grab timed out waiting for image callback.");
                }

                CameraArgs result;
                lock (_syncRoot)
                {
                    _isWaitingForSingleGrab = false;

                    if (_singleGrabException is not null)
                    {
                        var singleGrabException = _singleGrabException;
                        _singleGrabException = null;
                        _singleGrabFrame = null;
                        ExceptionDispatchInfo.Capture(singleGrabException).Throw();
                    }

                    result = _singleGrabFrame ?? throw new InvalidOperationException("Grab failed because callback frame data is unavailable.");
                    _singleGrabFrame = null;
                    _singleGrabException = null;
                }

                ImageGrabbed?.Invoke(this, result);
                return result;
            }
            finally
            {
                _singleGrabLock.Set();
            }
        }

        public Task<CameraArgs> GrabAsync()
        {
            return Task.Run(Grab);
        }

        public void StartContinuousGrab()
        {
            lock (_syncRoot)
            {
                EnsureCameraReady();

                if (_isContinuousGrabbing)
                {
                    throw new InvalidOperationException("Camera is already in continuous grab mode.");
                }

                var nRet = _camera!.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);
                if (nRet != MyCamera.MV_OK)
                {
                    throw new InvalidOperationException($"Start continuous grab failed: 0x{nRet:X8}");
                }

                _isContinuousGrabbing = true;
            }
        }

        public void StopContinuousGrab()
        {
            lock (_syncRoot)
            {
                EnsureCameraReady();

                if (!_isContinuousGrabbing)
                {
                    throw new InvalidOperationException("Camera is not in continuous grab mode.");
                }

                _camera!.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_ON);
                _camera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_SOFTWARE);
                _isContinuousGrabbing = false;
            }
        }

        public void SetExposureTime(double exposureTime)
        {
            lock (_syncRoot)
            {
                if (_camera is null || !IsConnected || !IsCameraHandleValid())
                {
                    ResetConnectionState();
                    return;
                }

                _camera.MV_CC_SetFloatValue_NET("ExposureTime", (float)exposureTime);
            }
        }

        public void SetGain(double gain)
        {
            lock (_syncRoot)
            {
                if (_camera is null || !IsConnected || !IsCameraHandleValid())
                {
                    ResetConnectionState();
                    return;
                }

                _camera.MV_CC_SetFloatValue_NET("Gain", (float)gain);
            }
        }

        public bool TryGetExposureTime(out double exposureTime)
        {
            return TryGetFloatValue("ExposureTime", out exposureTime);
        }

        public bool TryGetGain(out double gain)
        {
            return TryGetFloatValue("Gain", out gain);
        }

        public bool TryGetResultingFrameRate(out double frameRate)
        {
            return TryGetFloatValue("ResultingFrameRate", out frameRate);
        }

        public int TrySetExposureTime(double exposureTime)
        {
            return SetFloatValue("ExposureTime", (float)exposureTime);
        }

        public int TrySetGain(double gain)
        {
            return SetFloatValue("Gain", (float)gain);
        }

        public int TrySetFrameRate(double frameRate)
        {
            return SetFloatValue("AcquisitionFrameRate", (float)frameRate);
        }

        public void Dispose()
        {
            Close();
            _singleGrabSignal.Dispose();
            _singleGrabLock.Dispose();
            GC.SuppressFinalize(this);
        }

        private void EnsureCameraReady()
        {
            if (_isClosing || !IsConnected || _camera is null)
            {
                throw new OperationCanceledException("Camera is closing.");
            }

            if (!_isGrabbing || !IsCameraHandleValid())
            {
                ResetConnectionState();
                throw new InvalidOperationException("Camera connection is invalid. Please reopen the camera.");
            }
        }

        private bool IsCameraHandleValid()
        {
            return _camera is not null && _camera.MV_CC_IsDeviceConnected_NET();
        }

        private bool TryGetFloatValue(string key, out double value)
        {
            lock (_syncRoot)
            {
                value = 0;

                if (_camera is null || !IsConnected || !IsCameraHandleValid())
                {
                    ResetConnectionState();
                    return false;
                }

                var param = new MyCamera.MVCC_FLOATVALUE();
                if (_camera.MV_CC_GetFloatValue_NET(key, ref param) != MyCamera.MV_OK)
                {
                    return false;
                }

                value = param.fCurValue;
                return true;
            }
        }

        private int SetFloatValue(string key, float value)
        {
            lock (_syncRoot)
            {
                if (_camera is null || !IsConnected || !IsCameraHandleValid())
                {
                    ResetConnectionState();
                    return MyCamera.MV_E_HANDLE;
                }

                return _camera.MV_CC_SetFloatValue_NET(key, value);
            }
        }

        private void ResetConnectionState()
        {
            _isGrabbing = false;
            _isContinuousGrabbing = false;
            _isWaitingForSingleGrab = false;
            IsConnected = false;
            _singleGrabFrame = null;
            _singleGrabSignal.Set();
        }

        private void ReleaseSdkIfNeeded()
        {
            if (!_sdkAcquired)
            {
                return;
            }

            HkSdkManager.Release();
            _sdkAcquired = false;
        }

        private CameraArgs BuildFrameArgs(IntPtr sourceBuffer, uint sourceBufferLength, MyCamera.MV_FRAME_OUT_INFO_EX frameInfo)
        {
            var width = frameInfo.nWidth;
            var height = frameInfo.nHeight;

            if (frameInfo.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8)
            {
                var bytes = CopyRawData(sourceBuffer, (int)(width * height), sourceBufferLength);
                return new CameraArgs
                {
                    Data = bytes,
                    Width = width,
                    Height = height,
                    Stride = width,
                    Format = PixelFormat.Mono8,
                    FrameId = frameInfo.nFrameNum,
                    Timestamp = DateTime.Now
                };
            }

            if (frameInfo.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed)
            {
                var bytes = CopyRawData(sourceBuffer, (int)(width * height * 3), sourceBufferLength);
                return new CameraArgs
                {
                    Data = bytes,
                    Width = width,
                    Height = height,
                    Stride = width * 3,
                    Format = PixelFormat.BGR8,
                    FrameId = frameInfo.nFrameNum,
                    Timestamp = DateTime.Now
                };
            }

            if (frameInfo.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_RGB8_Packed)
            {
                var bytes = CopyRawData(sourceBuffer, (int)(width * height * 3), sourceBufferLength);
                return new CameraArgs
                {
                    Data = bytes,
                    Width = width,
                    Height = height,
                    Stride = width * 3,
                    Format = PixelFormat.RGB8,
                    FrameId = frameInfo.nFrameNum,
                    Timestamp = DateTime.Now
                };
            }

            if (frameInfo.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_BGRA8_Packed)
            {
                var bytes = CopyRawData(sourceBuffer, (int)(width * height * 4), sourceBufferLength);
                return new CameraArgs
                {
                    Data = bytes,
                    Width = width,
                    Height = height,
                    Stride = width * 4,
                    Format = PixelFormat.BGRA8,
                    FrameId = frameInfo.nFrameNum,
                    Timestamp = DateTime.Now
                };
            }

            if (frameInfo.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_RGBA8_Packed)
            {
                var bytes = CopyRawData(sourceBuffer, (int)(width * height * 4), sourceBufferLength);
                return new CameraArgs
                {
                    Data = bytes,
                    Width = width,
                    Height = height,
                    Stride = width * 4,
                    Format = PixelFormat.RGBA8,
                    FrameId = frameInfo.nFrameNum,
                    Timestamp = DateTime.Now
                };
            }

            return ConvertToBgr(sourceBuffer, sourceBufferLength, frameInfo);
        }

        private CameraArgs ConvertToBgr(IntPtr sourceBuffer, uint sourceBufferLength, MyCamera.MV_FRAME_OUT_INFO_EX frameInfo)
        {
            if (_camera is null)
            {
                throw new InvalidOperationException("Camera is not connected.");
            }

            var dstLen = frameInfo.nWidth * frameInfo.nHeight * 3;
            var dstBuffer = Marshal.AllocHGlobal((int)dstLen);

            try
            {
                var convertParam = new MyCamera.MV_PIXEL_CONVERT_PARAM
                {
                    nWidth = frameInfo.nWidth,
                    nHeight = frameInfo.nHeight,
                    enSrcPixelType = frameInfo.enPixelType,
                    pSrcData = sourceBuffer,
                    nSrcDataLen = sourceBufferLength,
                    enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed,
                    pDstBuffer = dstBuffer,
                    nDstBufferSize = (uint)dstLen,
                    nDstLen = 0,
                    nRes = new uint[4]
                };

                var nRet = _camera.MV_CC_ConvertPixelType_NET(ref convertParam);
                if (nRet != MyCamera.MV_OK || convertParam.nDstLen == 0)
                {
                    throw new InvalidOperationException($"Convert pixel type failed: 0x{nRet:X8}");
                }

                var data = new byte[convertParam.nDstLen];
                Marshal.Copy(dstBuffer, data, 0, data.Length);

                return new CameraArgs
                {
                    Data = data,
                    Width = frameInfo.nWidth,
                    Height = frameInfo.nHeight,
                    Stride = frameInfo.nWidth * 3,
                    Format = PixelFormat.BGR8,
                    FrameId = frameInfo.nFrameNum,
                    Timestamp = DateTime.Now
                };
            }
            finally
            {
                Marshal.FreeHGlobal(dstBuffer);
            }
        }

        private byte[] CopyRawData(IntPtr sourceBuffer, int length, uint sourceBufferLength)
        {
            var copyLength = Math.Min(length, (int)sourceBufferLength);
            var data = new byte[copyLength];
            Marshal.Copy(sourceBuffer, data, 0, copyLength);
            return data;
        }

        private void OnImageGrabbed(IntPtr pData, ref MyCamera.MV_FRAME_OUT_INFO_EX pFrameInfo, IntPtr pUser)
        {
            CameraArgs? singleGrabResult = null;
            CameraArgs? continuousResult = null;

            try
            {
                lock (_syncRoot)
                {
                    if (_isClosing || !IsConnected || _camera is null || pData == IntPtr.Zero)
                    {
                        return;
                    }

                    var currentFrame = BuildFrameArgs(pData, pFrameInfo.nFrameLen, pFrameInfo);
                    _lastCallbackFrameId = pFrameInfo.nFrameNum;
                    _lastCallbackTimestamp = DateTime.Now;

                    if (_isWaitingForSingleGrab)
                    {
                        _singleGrabFrame = currentFrame;
                        singleGrabResult = currentFrame;
                    }

                    if (_isContinuousGrabbing)
                    {
                        continuousResult = currentFrame;
                    }
                }

                if (singleGrabResult is not null)
                {
                    _singleGrabSignal.Set();
                }

                if (continuousResult is not null)
                {
                    ImageGrabbed?.Invoke(this, continuousResult);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "HkCamera image callback failed. WaitingForSingleGrab={IsWaitingForSingleGrab}, Continuous={IsContinuousGrabbing}, LastCallbackFrameId={LastCallbackFrameId}",
                    _isWaitingForSingleGrab,
                    _isContinuousGrabbing,
                    _lastCallbackFrameId);

                lock (_syncRoot)
                {
                    if (_isWaitingForSingleGrab)
                    {
                        _singleGrabException = ex;
                        _singleGrabSignal.Set();
                    }
                }
            }
        }

        private static MyCamera.MV_CC_DEVICE_INFO? GetTargetDeviceInfo(MyCamera.MV_CC_DEVICE_INFO_LIST deviceList, string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(deviceList.pDeviceInfo[0]);
            }

            for (int i = 0; i < deviceList.nDeviceNum; i++)
            {
                var deviceInfo = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(deviceList.pDeviceInfo[i]);
                if (string.Equals(BuildConnectionString(deviceInfo), connectionString, StringComparison.OrdinalIgnoreCase))
                {
                    return deviceInfo;
                }
            }

            return null;
        }

        private static string BuildConnectionString(MyCamera.MV_CC_DEVICE_INFO device)
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
    }
}
