 
using System;
using System.Threading.Tasks;

namespace HAL
{
        /// <summary>
        /// 相机图像采集回调参数，保持轻量化，不引用 UI 或图像库
        /// </summary>
        public class CameraArgs : EventArgs
        {
            /// <summary>
            /// 原始像素数据数据
            /// </summary>
            public byte[]? Data { get; set; }

            /// <summary>
            /// 图像宽度
            /// </summary>
            public int Width { get; set; }

            /// <summary>
            /// 图像高度
            /// </summary>
            public int Height { get; set; }

            /// <summary>
            /// 像素格式（例如：Mono8, RGB24, BayerBG8 等）
            /// 通常建议定义一个 Enum 或者直接记录 SDK 返回的 String
            /// </summary>
            public PixelFormat Format { get; set; } = PixelFormat.Mono8;

            /// <summary>
            /// 时间戳（用于同步或检测丢帧）
            /// </summary>
            public DateTime Timestamp { get; set; } = DateTime.Now;

            /// <summary>
            /// 帧 ID（相机硬件生成的序列号）
            /// </summary>
            public long FrameId { get; set; }

            /// <summary>
            /// 图像跨度（Stride/Step），即每一行像素占用的字节数
            /// 处理对齐（Alignment）问题时非常关键
            /// </summary>
            public int Stride { get; set; }
        }

        /// <summary>
        /// 图像像素格式枚举（参考 GenICam 标准）
        /// 用于标识 byte[] 数据的排列方式
        /// </summary>
        public enum PixelFormat
        {
            Unknown = 0,

            // --- 黑白格式 ---
            Mono8,           // 8位灰度
            Mono10,          // 10位灰度（通常占用2字节）
            Mono12,          // 12位灰度
            Mono16,          // 16位灰度

            // --- 彩色格式 (打包格式) ---
            RGB8,            // R-G-B 顺序
            BGR8,            // B-G-R 顺序 (Windows/OpenCV 常用)
            RGBA8,           // 带 Alpha 通道
            BGRA8,

            // --- Bayer 阵列 (原始马赛克数据，需转码后才能显示彩色) ---
            BayerRG8,
            BayerBG8,
            BayerGB8,
            BayerGR8,

            // --- YUV 格式 (常见于 USB 相机或视频流) ---
            YUV422Packed,
            YUYV
        }

    public interface ICamera : IDisposable
    {
        /// <summary>
        /// 连接状态
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 是否处于连续采集模式
        /// </summary>
        bool IsContinuousGrabbing { get; }

        /// <summary>
        /// 打开相机
        /// </summary>
        bool Open();

        /// <summary>
        /// 关闭相机
        /// </summary>
        void Close();

        /// <summary>
        /// 软触发拍照（核心需求）
        /// </summary>
        /// <returns>返回 OpenCvSharp 的 Mat 对象</returns>
        CameraArgs Grab();

        /// <summary>
        /// 异步软触发拍照，防止 UI 卡顿
        /// </summary>
        Task<CameraArgs> GrabAsync();

        /// <summary>
        /// 启动连续采集
        /// </summary>
        void StartContinuousGrab();

        /// <summary>
        /// 停止连续采集
        /// </summary>
        void StopContinuousGrab();

        /// <summary>
        /// 设置曝光时间
        /// </summary>
        /// <param name="exposureTime">微秒 us</param>
        void SetExposureTime(double exposureTime);

        /// <summary>
        /// 设置增益
        /// </summary>
        void SetGain(double gain);

        /// <summary>
        /// 图像抓取完成后的事件回调（用于连续采集模式）
        /// </summary>
        event EventHandler<CameraArgs> ImageGrabbed;
    }
}