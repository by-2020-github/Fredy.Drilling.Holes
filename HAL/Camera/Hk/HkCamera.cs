using MvCamCtrl.NET;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HAL
{
    public sealed class HkCamera : ICamera
    {
        private readonly object _syncRoot = new();
        private MyCamera? _camera;
        private IntPtr _frameBuffer = IntPtr.Zero;
        private uint _payloadSize;
        private bool _isGrabbing;
        private volatile bool _isClosing;

        public bool IsConnected { get; private set; }

        public event EventHandler<CameraArgs>? ImageGrabbed;

        public bool Open()
        {
            lock (_syncRoot)
            {
                if (IsConnected)
                {
                    return true;
                }

                var deviceList = new MyCamera.MV_CC_DEVICE_INFO_LIST
                {
                    nDeviceNum = 0,
                    pDeviceInfo = new IntPtr[256]
                };

                var layerType = (uint)(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE);
                var nRet = MyCamera.MV_CC_EnumDevices_NET(layerType, ref deviceList);
                if (nRet != MyCamera.MV_OK || deviceList.nDeviceNum < 1)
                {
                    return false;
                }

                var deviceInfo = Marshal.PtrToStructure<MyCamera.MV_CC_DEVICE_INFO>(deviceList.pDeviceInfo[0]);
                _camera = new MyCamera();

                try
                {
                    nRet = _camera.MV_CC_CreateDevice_NET(ref deviceInfo);
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

                    if (deviceInfo.nTLayerType == MyCamera.MV_GIGE_DEVICE)
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

                    var payload = new MyCamera.MVCC_INTVALUE_EX();
                    nRet = _camera.MV_CC_GetIntValueEx_NET("PayloadSize", ref payload);
                    if (nRet != MyCamera.MV_OK || payload.nCurValue <= 0)
                    {
                        Close();
                        return false;
                    }

                    _payloadSize = (uint)payload.nCurValue;
                    _frameBuffer = Marshal.AllocHGlobal((int)_payloadSize);

                    _isClosing = false;
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

                if (_frameBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_frameBuffer);
                    _frameBuffer = IntPtr.Zero;
                }

                _payloadSize = 0;
                IsConnected = false;
            }
        }

        public CameraArgs Grab()
        {
            lock (_syncRoot)
            {
                if (_isClosing || !IsConnected || _camera is null || _frameBuffer == IntPtr.Zero)
                {
                    throw new OperationCanceledException("Camera is closing.");
                }

                var triggerRet = _camera.MV_CC_SetCommandValue_NET("TriggerSoftware");
                if (triggerRet != MyCamera.MV_OK)
                {
                    if (_isClosing || !IsConnected || !_isGrabbing)
                    {
                        throw new OperationCanceledException("Camera trigger canceled because camera is closing.");
                    }

                    throw new InvalidOperationException($"Trigger failed: 0x{triggerRet:X8}");
                }

                var frameInfo = new MyCamera.MV_FRAME_OUT_INFO_EX();
                var nRet = _camera.MV_CC_GetOneFrameTimeout_NET(_frameBuffer, _payloadSize, ref frameInfo, 1000);
                if (nRet != MyCamera.MV_OK)
                {
                    if (_isClosing || !IsConnected || !_isGrabbing)
                    {
                        throw new OperationCanceledException("Camera grab canceled because camera is closing.");
                    }

                    throw new InvalidOperationException($"Grab failed: 0x{nRet:X8}");
                }

                var result = BuildFrameArgs(frameInfo);
                ImageGrabbed?.Invoke(this, result);
                return result;
            }
        }

        public Task<CameraArgs> GrabAsync()
        {
            return Task.Run(Grab);
        }

        public void SetExposureTime(double exposureTime)
        {
            lock (_syncRoot)
            {
                if (_camera is null || !IsConnected)
                {
                    return;
                }

                _camera.MV_CC_SetFloatValue_NET("ExposureTime", (float)exposureTime);
            }
        }

        public void SetGain(double gain)
        {
            lock (_syncRoot)
            {
                if (_camera is null || !IsConnected)
                {
                    return;
                }

                _camera.MV_CC_SetFloatValue_NET("Gain", (float)gain);
            }
        }

        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }

        private CameraArgs BuildFrameArgs(MyCamera.MV_FRAME_OUT_INFO_EX frameInfo)
        {
            var width = frameInfo.nWidth;
            var height = frameInfo.nHeight;

            if (frameInfo.enPixelType == MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8)
            {
                var bytes = CopyRawData((int)(width * height));
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
                var bytes = CopyRawData((int)(width * height * 3));
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
                var bytes = CopyRawData((int)(width * height * 3));
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
                var bytes = CopyRawData((int)(width * height * 4));
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
                var bytes = CopyRawData((int)(width * height * 4));
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

            return ConvertToBgr(frameInfo);
        }

        private CameraArgs ConvertToBgr(MyCamera.MV_FRAME_OUT_INFO_EX frameInfo)
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
                    pSrcData = _frameBuffer,
                    nSrcDataLen = frameInfo.nFrameLen,
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

        private byte[] CopyRawData(int length)
        {
            var copyLength = Math.Min(length, (int)_payloadSize);
            var data = new byte[copyLength];
            Marshal.Copy(_frameBuffer, data, 0, copyLength);
            return data;
        }
    }
}
