using MvCamCtrl.NET;
using Serilog;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HAL
{
    public sealed class HkCamera : ICamera
    {
        private const int DefaultContinuousGrabMaxFps = 20;
        private const int SingleGrabTimeoutMilliseconds = 3000;
        private static readonly ILogger Logger = Log.ForContext<HkCamera>();

        private readonly object _syncRoot = new();

        private readonly AutoResetEvent _singleGrabLock = new(true);
        private readonly ManualResetEventSlim _noActiveGrabOperations = new(true);
        private MyCamera? _camera;
        private bool _isGrabbing;
        private bool _isContinuousGrabbing;
        private volatile bool _isClosing;
        private bool _sdkAcquired;
        private long _lastCallbackFrameId;
        private DateTime? _lastCallbackTimestamp;
        private CancellationTokenSource? _continuousGrabCancellationTokenSource;
        private Task? _continuousGrabTask;
        private int _activeGrabOperations;
        private TimeSpan _continuousGrabInterval = TimeSpan.FromMilliseconds(1000.0 / DefaultContinuousGrabMaxFps);

        public HkCamera()
        {
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
            Task? continuousGrabTask;
            CancellationTokenSource? continuousGrabCancellationTokenSource;

            lock (_syncRoot)
            {
                _isContinuousGrabbing = false;
                _continuousGrabCancellationTokenSource?.Cancel();
                continuousGrabCancellationTokenSource = _continuousGrabCancellationTokenSource;
                _continuousGrabCancellationTokenSource = null;
                continuousGrabTask = _continuousGrabTask;
                _continuousGrabTask = null;

                ConnectionString = null;
                IsConnected = false;
                _isGrabbing = false;
            }

            WaitForContinuousGrabTaskToStop(continuousGrabTask);
            WaitForActiveGrabOperationsToComplete();

            lock (_syncRoot)
            {
                if (_camera is not null)
                {
                    _camera.MV_CC_StopGrabbing_NET();
                    _camera.MV_CC_CloseDevice_NET();
                    _camera.MV_CC_DestroyDevice_NET();
                    _camera = null;
                }

                ReleaseSdkIfNeeded();
            }

            continuousGrabCancellationTokenSource?.Dispose();
        }

        public CameraArgs Grab()
        {
            _singleGrabLock.WaitOne();

            try
            {
                CameraArgs result;

                try
                {
                    result = GrabSingleFrameSynchronously(SingleGrabTimeoutMilliseconds);
                }
                catch (TimeoutException firstTimeoutException)
                {
                    Logger.Warning(firstTimeoutException,
                        "Camera single grab timed out during synchronous frame acquisition. Attempting automatic recovery. Last frame id: {LastFrameId}, last frame timestamp: {LastFrameTimestamp}, connection: {IsConnected}, grabbing: {IsGrabbing}",
                        _lastCallbackFrameId,
                        _lastCallbackTimestamp,
                        IsConnected,
                        _isGrabbing);

                    var connectionString = ConnectionString;
                    if (!TryRecoverAfterGrabFailure(connectionString))
                    {
                        throw;
                    }

                    result = GrabSingleFrameSynchronously(SingleGrabTimeoutMilliseconds);
                }

                return result;
            }
            finally
            {
                _singleGrabLock.Set();
            }
        }

        public Task<CameraArgs> GrabAsync()
        {
            return Task.Run(() =>
            {
                var result = Grab();
                QueueImageGrabbed(result);
                return result;
            });
        }

        public void StartContinuousGrab()
        {
            CancellationTokenSource continuousGrabCancellationTokenSource;

            lock (_syncRoot)
            {
                EnsureCameraReady();

                if (_isContinuousGrabbing)
                {
                    throw new InvalidOperationException("Camera is already in continuous grab mode.");
                }

                var triggerModeRet = _camera!.MV_CC_SetEnumValue_NET("TriggerMode", (uint)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_ON);
                if (triggerModeRet != MyCamera.MV_OK)
                {
                    throw new InvalidOperationException($"Set trigger mode failed: 0x{triggerModeRet:X8}");
                }

                var triggerSourceRet = _camera.MV_CC_SetEnumValue_NET("TriggerSource", (uint)MyCamera.MV_CAM_TRIGGER_SOURCE.MV_TRIGGER_SOURCE_SOFTWARE);
                if (triggerSourceRet != MyCamera.MV_OK)
                {
                    throw new InvalidOperationException($"Set trigger source failed: 0x{triggerSourceRet:X8}");
                }

                _continuousGrabCancellationTokenSource?.Cancel();
                _continuousGrabCancellationTokenSource?.Dispose();
                continuousGrabCancellationTokenSource = new CancellationTokenSource();
                _continuousGrabCancellationTokenSource = continuousGrabCancellationTokenSource;
                _isContinuousGrabbing = true;
                _continuousGrabTask = Task.Run(() => ContinuousGrabLoopAsync(continuousGrabCancellationTokenSource.Token));
            }
        }

        public void StopContinuousGrab()
        {
            Task? continuousGrabTask;
            CancellationTokenSource? continuousGrabCancellationTokenSource;

            lock (_syncRoot)
            {
                EnsureCameraReady();

                if (!_isContinuousGrabbing)
                {
                    throw new InvalidOperationException("Camera is not in continuous grab mode.");
                }

                _isContinuousGrabbing = false;
                _continuousGrabCancellationTokenSource?.Cancel();
                continuousGrabCancellationTokenSource = _continuousGrabCancellationTokenSource;
                _continuousGrabCancellationTokenSource = null;
                continuousGrabTask = _continuousGrabTask;
                _continuousGrabTask = null;
            }

            WaitForContinuousGrabTaskToStop(continuousGrabTask);
            continuousGrabCancellationTokenSource?.Dispose();
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
            var ret = SetFloatValue("AcquisitionFrameRate", (float)frameRate);
            if (ret == MyCamera.MV_OK)
            {
                UpdateContinuousGrabInterval(frameRate);
            }

            return ret;
        }

        public void Dispose()
        {
            Close();
            _noActiveGrabOperations.Dispose();
            _singleGrabLock.Dispose();
            GC.SuppressFinalize(this);
        }

        public int TriggerSoftware()
        {
            lock (_syncRoot)
            {
                EnsureCameraReady();
                return _camera!.MV_CC_SetCommandValue_NET("TriggerSoftware");
            }
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

        private CameraArgs GrabSingleFrameSynchronously(int timeoutMilliseconds)
        {
            MyCamera camera;
            uint payloadSize;

            lock (_syncRoot)
            {
                EnsureCameraReady();

                if (_isContinuousGrabbing)
                {
                    //Logger.Verbose("GrabSingleFrameSynchronously is running while continuous grab loop is active.");
                }

                camera = _camera!;
                payloadSize = GetPayloadSizeUnsafe(camera);
                BeginGrabOperationUnsafe();
            }

            var frameBuffer = Marshal.AllocHGlobal((int)payloadSize);

            try
            {
                var triggerRet = camera.MV_CC_SetCommandValue_NET("TriggerSoftware");
                if (triggerRet != MyCamera.MV_OK)
                {
                    if (IsCameraUnavailableForGrab(camera))
                    {
                        throw new OperationCanceledException("Camera trigger canceled because camera is closing.");
                    }

                    throw new InvalidOperationException($"Trigger failed: 0x{triggerRet:X8}");
                }

                var frameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();
                var grabRet = camera.MV_CC_GetOneFrameTimeout_NET(frameBuffer, payloadSize, ref frameInfo, timeoutMilliseconds);
                if (grabRet != MyCamera.MV_OK)
                {
                    if (IsCameraUnavailableForGrab(camera))
                    {
                        throw new OperationCanceledException("Camera grab canceled because camera is closing.");
                    }

                    Logger.Warning("Camera single grab timed out during synchronous acquisition. SDK ret: 0x{GrabRet:X8}, last callback frame id: {LastCallbackFrameId}, last callback timestamp: {LastCallbackTimestamp}, connection: {IsConnected}, grabbing: {IsGrabbing}",
                        grabRet,
                        _lastCallbackFrameId,
                        _lastCallbackTimestamp,
                        IsConnected,
                        _isGrabbing);

                    throw new TimeoutException($"Grab timed out waiting for image data. SDK ret: 0x{grabRet:X8}");
                }

                var result = BuildFrameArgs(camera, frameBuffer, frameInfo.nFrameLen, frameInfo);

                lock (_syncRoot)
                {
                    _lastCallbackFrameId = frameInfo.nFrameNum;
                    _lastCallbackTimestamp = DateTime.Now;
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(frameBuffer);
                EndGrabOperation();
            }
        }

        private uint GetPayloadSizeUnsafe(MyCamera camera)
        {
            var payload = new MyCamera.MVCC_INTVALUE();
            var ret = camera.MV_CC_GetIntValue_NET("PayloadSize", ref payload);
            if (ret != MyCamera.MV_OK || payload.nCurValue == 0)
            {
                throw new InvalidOperationException($"Get payload size failed: 0x{ret:X8}");
            }

            return payload.nCurValue;
        }

        private bool TryRecoverAfterGrabFailure(string? connectionString)
        {
            try
            {
                lock (_syncRoot)
                {
                    if (_isClosing || !IsConnected || _camera is null)
                    {
                        return false;
                    }

                    if (_isGrabbing)
                    {
                        var stopRet = _camera.MV_CC_StopGrabbing_NET();
                        if (stopRet == MyCamera.MV_OK)
                        {
                            _isGrabbing = false;
                        }
                        else
                        {
                            Logger.Warning("Camera stop grabbing during recovery failed: 0x{StopRet:X8}", stopRet);
                        }
                    }

                    var startRet = _camera.MV_CC_StartGrabbing_NET();
                    if (startRet == MyCamera.MV_OK)
                    {
                        _isGrabbing = true;
                        Logger.Information("Camera grabbing recovered by restarting stream.");
                        return true;
                    }

                    Logger.Warning("Camera start grabbing during recovery failed: 0x{StartRet:X8}", startRet);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Camera stream restart recovery failed. Will try reopen camera.");
            }

            try
            {
                Close();
                var reopened = Open(connectionString);
                if (reopened)
                {
                    Logger.Information("Camera recovered by reopening device.");
                }
                else
                {
                    Logger.Warning("Camera reopen recovery failed.");
                }

                return reopened;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Camera reopen recovery failed with exception.");
                return false;
            }
        }

        private bool IsCameraHandleValid()
        {
            return _camera is not null && _camera.MV_CC_IsDeviceConnected_NET();
        }

        private void BeginGrabOperationUnsafe()
        {
            _activeGrabOperations++;
            _noActiveGrabOperations.Reset();
        }

        private void EndGrabOperation()
        {
            lock (_syncRoot)
            {
                if (_activeGrabOperations <= 0)
                {
                    return;
                }

                _activeGrabOperations--;
                if (_activeGrabOperations == 0)
                {
                    _noActiveGrabOperations.Set();
                }
            }
        }

        private void WaitForActiveGrabOperationsToComplete()
        {
            try
            {
                _noActiveGrabOperations.Wait();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Waiting for active grab operations to stop failed.");
            }
        }

        private bool IsCameraUnavailableForGrab(MyCamera camera)
        {
            lock (_syncRoot)
            {
                return _isClosing || !IsConnected || !ReferenceEquals(_camera, camera) || !_isGrabbing;
            }
        }

        private async Task ContinuousGrabLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TimeSpan continuousGrabInterval;
                    lock (_syncRoot)
                    {
                        continuousGrabInterval = _continuousGrabInterval;
                    }

                    var stopwatch = Stopwatch.StartNew();

                    try
                    {
                        await GrabAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Continuous grab loop failed.");
                    }

                    var remainingDelay = continuousGrabInterval - stopwatch.Elapsed;
                    if (remainingDelay > TimeSpan.Zero)
                    {
                        try
                        {
                            await Task.Delay(remainingDelay, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (_continuousGrabCancellationTokenSource is not null && _continuousGrabCancellationTokenSource.Token == cancellationToken)
                    {
                        _continuousGrabCancellationTokenSource = null;
                    }

                    _isContinuousGrabbing = false;
                }
            }
        }

        private void UpdateContinuousGrabInterval(double frameRate)
        {
            if (double.IsNaN(frameRate) || double.IsInfinity(frameRate) || frameRate <= 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                _continuousGrabInterval = TimeSpan.FromSeconds(1d / frameRate);
            }
        }

        private void WaitForContinuousGrabTaskToStop(Task? continuousGrabTask)
        {
            if (continuousGrabTask is null)
            {
                return;
            }

            try
            {
                continuousGrabTask.Wait();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1 && ex.InnerException is OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Waiting for continuous grab task to stop failed.");
            }
        }

        private void QueueImageGrabbed(CameraArgs args)
        {
            var imageGrabbed = ImageGrabbed;
            if (imageGrabbed is null)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    imageGrabbed.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "ImageGrabbed event handler failed.");
                }
            });
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
            IsConnected = false;
            _continuousGrabCancellationTokenSource?.Cancel();
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

        private CameraArgs BuildFrameArgs(MyCamera camera, IntPtr sourceBuffer, uint sourceBufferLength, MyCamera.MV_FRAME_OUT_INFO_EX frameInfo)
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

            return ConvertToBgr(camera, sourceBuffer, sourceBufferLength, frameInfo);
        }

        private CameraArgs ConvertToBgr(MyCamera camera, IntPtr sourceBuffer, uint sourceBufferLength, MyCamera.MV_FRAME_OUT_INFO_EX frameInfo)
        {
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

                var nRet = camera.MV_CC_ConvertPixelType_NET(ref convertParam);
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
