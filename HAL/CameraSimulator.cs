using System;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
namespace HAL
{
    public sealed class CameraSimulator : ICamera
    {
        private bool _isConnected;
        private double _exposureTime;
        private double _gain;
        private long _frameCounter = 0;
        private readonly int _width = 1920;
        private readonly int _height = 1080;
        private readonly Random _random = new();

        public bool IsConnected => _isConnected;

        public event EventHandler<CameraArgs>? ImageGrabbed;

        public bool Open()
        {
            // 模拟初始化耗时
            Task.Delay(100).Wait();
            _isConnected = true;
            Console.WriteLine("Simulator Camera Opened.");
            return true;
        }

        public void Close()
        {
            _isConnected = false;
            Console.WriteLine("Simulator Camera Closed.");
        }

        /// <summary>
        /// 同步抓图：模拟生成一张 OpenCV 图像并转为原始字节流
        /// </summary>
        public CameraArgs Grab()
        {
            if (!_isConnected) throw new InvalidOperationException("Camera not connected.");

            _frameCounter++;

            using (Mat canvas = new Mat(_height, _width, MatType.CV_8UC3, new Scalar(64, 64, 64)))
            {
                Cv2.PutText(canvas, $"Simulated Frame: {++_frameCounter}", new Point(50, 100),
                 HersheyFonts.HersheyComplex, 2.0, Scalar.White, 3);
                Cv2.PutText(canvas, DateTime.Now.ToString("HH:mm:ss.fff"), new Point(50, 200),
                    HersheyFonts.HersheyComplex, 1.5, Scalar.Green, 2);
                var pointCount = _random.Next(10, 20);
                for (int i = 0; i < pointCount; i++)
                {
                    var x = _random.Next(0, _width);
                    var y = _random.Next(0, _height);
                    var radius = _random.Next(20, 100);
                    Cv2.Circle(canvas, new Point(x, y), radius, Scalar.White, -1, LineTypes.AntiAlias);
                }

                // 2. 将 Mat 转换为 CameraArgs 需要的原始数据
                var args = MatToCameraArgs(canvas);

                // 3. 触发事件 (模拟连续采集回调)
                ImageGrabbed?.Invoke(this, args);

                return args;
            }
        }

        public async Task<CameraArgs> GrabAsync()
        {
            // 模拟硬件触发延迟（如曝光时间）
            await Task.Delay(30);
            return await Task.Run(() => Grab());
        }

        public void SetExposureTime(double exposureTime) => _exposureTime = exposureTime;

        public void SetGain(double gain) => _gain = gain;

        /// <summary>
        /// 核心转换逻辑：从 Mat 提取原始字节流
        /// </summary>
        private CameraArgs MatToCameraArgs(Mat mat)
        {
            int size = (int)(mat.DataEnd - mat.DataStart);
            byte[] rawData = new byte[size];

            // 将内存数据拷贝到 byte[]
            Marshal.Copy(mat.Data, rawData, 0, size);

            return new CameraArgs
            {
                Data = rawData,
                Width = mat.Width,
                Height = mat.Height,
                Stride = (int)mat.Step(),
                Format = PixelFormat.BGR8, // OpenCV 默认是 BGR 顺序
                FrameId = _frameCounter,
                Timestamp = DateTime.Now
            };
        }

        public void Dispose()
        {
            Close();
        }
    }
}