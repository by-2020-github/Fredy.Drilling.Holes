using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using Common.Services;

namespace Fredy.Drilling.Holes.Tools
{
    public sealed class CenterRoiBinaryPreviewResult : IDisposable
    {
        public CenterRoiBinaryPreviewResult(Mat roiImage, Mat binaryImage, OpenCvSharp.Rect roiRect, int sourceWidth, int sourceHeight)
        {
            RoiImage = roiImage;
            BinaryImage = binaryImage;
            RoiRect = roiRect;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
        }

        public Mat RoiImage { get; }

        public Mat BinaryImage { get; }

        public OpenCvSharp.Rect RoiRect { get; }

        public int SourceWidth { get; }

        public int SourceHeight { get; }

        public void Dispose()
        {
            RoiImage.Dispose();
            BinaryImage.Dispose();
        }
    }

    public static class VisionUIHelper
    {
        /// <summary>
        /// 将 OpenCV 的 Mat 转换为 WPF 的 BitmapSource
        /// </summary>
        public static BitmapSource MatToBitmapSource(Mat image)
        {
            if (image == null || image.Empty()) return null;

            int width = image.Width;
            int height = image.Height;
            PixelFormat pixelFormat = PixelFormats.Bgr24;

            if (image.Channels() == 1)
            {
                pixelFormat = PixelFormats.Gray8;
            }
            else if (image.Channels() == 4)
            {
                pixelFormat = PixelFormats.Bgra32;
            }

            int step = (int)image.Step();
            int size = step * height;

            var bitmap = BitmapSource.Create(
                width, height, 96, 96, pixelFormat, null, image.Data, size, step);
            bitmap.Freeze(); // 冻结以便跨线程使用
            return bitmap;
        }

        /// <summary>
        /// 从相机原始字节流构建 OpenCV Mat 图像
        /// </summary>
        public static Mat CameraArgsToMat(HAL.CameraArgs frame)
        {
            if (frame?.Data == null || frame.Width <= 0 || frame.Height <= 0) return new Mat();

            MatType matType = MatType.CV_8UC3;
            switch (frame.Format)
            {
                case HAL.PixelFormat.Mono8: matType = MatType.CV_8UC1; break;
                case HAL.PixelFormat.RGB8: matType = MatType.CV_8UC3; break;
                case HAL.PixelFormat.BGR8: matType = MatType.CV_8UC3; break;
                case HAL.PixelFormat.RGBA8: matType = MatType.CV_8UC4; break;
                case HAL.PixelFormat.BGRA8: matType = MatType.CV_8UC4; break;
            }

            int channels = matType == MatType.CV_8UC1 ? 1 : (matType == MatType.CV_8UC4 ? 4 : 3);
            int stride = frame.Stride > 0 ? frame.Stride : frame.Width * channels;
            int size = frame.Height * stride;

            var mat = new Mat(frame.Height, frame.Width, matType);

            // Copy memory
            if (size <= frame.Data.Length)
            {
                // Stride match
                System.Runtime.InteropServices.Marshal.Copy(frame.Data, 0, mat.Data, size);
            }
            else
            {
                // Fallback copy if sizes mismatch slightly
                System.Runtime.InteropServices.Marshal.Copy(frame.Data, 0, mat.Data, frame.Data.Length);
            }

            // Convert RGB to BGR for OpenCV
            if (frame.Format == HAL.PixelFormat.RGB8)
            {
                Cv2.CvtColor(mat, mat, ColorConversionCodes.RGB2BGR);
            }

            return mat;
        }

        /// <summary>
        /// 将 WPF 的 BitmapSource 转换为 OpenCV 的 Mat 图像
        /// </summary>
        public static Mat BitmapSourceToMat(BitmapSource source)
        {
            if (source == null) return new Mat();

            int width = source.PixelWidth;
            int height = source.PixelHeight;

            Mat mat;
            if (source.Format == PixelFormats.Bgr24)
            {
                mat = new Mat(height, width, MatType.CV_8UC3);
            }
            else if (source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32)
            {
                mat = new Mat(height, width, MatType.CV_8UC4);
            }
            else if (source.Format == PixelFormats.Gray8)
            {
                mat = new Mat(height, width, MatType.CV_8UC1);
            }
            else
            {
                source = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
                mat = new Mat(height, width, MatType.CV_8UC3);
            }

            int step = (int)mat.Step();
            int size = height * step;
            byte[] bytes = new byte[size];

            // 拷贝像素到数组中，stride 使用 OpenCV 产生的 step 保证对齐
            source.CopyPixels(bytes, step, 0);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, mat.Data, size);

            return mat;
        }

        /// <summary>
        /// 依据界面交互绘制的 ROI 方框，换算到实际图像像素并剪裁保存为模板图片 
        /// </summary>
        public static void ExportROI(BitmapSource source, double uiImageWidth, double uiImageHeight, System.Windows.Rect roiRect, string savePath)
        {
            if (source == null || uiImageWidth <= 0 || uiImageHeight <= 0) return;

            using Mat mat = BitmapSourceToMat(source);
            if (mat.Empty()) return;

            double uniformScale = Math.Min(uiImageWidth / mat.Width, uiImageHeight / mat.Height);
            double renderedWidth = uniformScale * mat.Width;
            double renderedHeight = uniformScale * mat.Height;
            double offsetX = (uiImageWidth - renderedWidth) / 2.0;
            double offsetY = (uiImageHeight - renderedHeight) / 2.0;

            int x = (int)((roiRect.X - offsetX) / uniformScale);
            int y = (int)((roiRect.Y - offsetY) / uniformScale);
            int w = (int)(roiRect.Width / uniformScale);
            int h = (int)(roiRect.Height / uniformScale);

            x = Math.Max(0, Math.Min(x, mat.Width - 1));
            y = Math.Max(0, Math.Min(y, mat.Height - 1));
            w = Math.Max(1, Math.Min(w, mat.Width - x));
            h = Math.Max(1, Math.Min(h, mat.Height - y));

            if (w > 0 && h > 0)
            {
                using Mat roiMat = new Mat(mat, new OpenCvSharp.Rect(x, y, w, h));
                roiMat.SaveImage(savePath);
            }
        }

        public static CenterRoiBinaryPreviewResult? BuildCenterRoiBinaryPreview(BitmapSource source, int roiWidth, int roiHeight, int threshold, bool invert)
        {
            if (source == null) return null;

            using Mat mat = BitmapSourceToMat(source);
            return BuildCenterRoiBinaryPreview(mat, roiWidth, roiHeight, threshold, invert);
        }

        public static CenterRoiBinaryPreviewResult? BuildCenterRoiBinaryPreview(Mat source, int roiWidth, int roiHeight, int threshold, bool invert)
        {
            if (source == null || source.Empty()) return null;

            int width = Math.Clamp(roiWidth, 1, source.Width);
            int height = Math.Clamp(roiHeight, 1, source.Height);
            int x = Math.Max(0, (source.Width - width) / 2);
            int y = Math.Max(0, (source.Height - height) / 2);
            var roiRect = new OpenCvSharp.Rect(x, y, width, height);

            using Mat roi = new Mat(source, roiRect);
            var roiImage = roi.Clone();

            using var gray = new Mat();
            switch (roiImage.Channels())
            {
                case 1:
                    roiImage.CopyTo(gray);
                    break;
                case 3:
                    Cv2.CvtColor(roiImage, gray, ColorConversionCodes.BGR2GRAY);
                    break;
                case 4:
                    Cv2.CvtColor(roiImage, gray, ColorConversionCodes.BGRA2GRAY);
                    break;
                default:
                    throw new OpenCvSharpException($"Unsupported channel count: {roiImage.Channels()}");
            }

            var binaryImage = new Mat();
            Cv2.Threshold(gray, binaryImage, Math.Clamp(threshold, 0, 255), 255, invert ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary);

            return new CenterRoiBinaryPreviewResult(roiImage, binaryImage, roiRect, source.Width, source.Height);
        }

        /// <summary>
        /// 调用底层 CircleDetector，提取圆孔并在界面上的无损浮层 (Canvas) 映射出原比例标记。
        /// </summary>
        public static DetectionResult DetectAndDrawCircles(BitmapSource source, double uiImageWidth, double uiImageHeight, Canvas overlayCanvas, bool isDarkHole = true, double minRadius = 15, double maxRadius = 25, double param1 = 50, double param2 = 25)
        {
            if (source == null || uiImageWidth <= 0 || uiImageHeight <= 0) return null;

            using Mat mat = BitmapSourceToMat(source);
            if (mat.Empty()) return null;

            return DetectAndDrawCircles(mat, uiImageWidth, uiImageHeight, overlayCanvas, isDarkHole, minRadius, maxRadius, param1, param2);
        }

        public static DetectionResult DetectAndDrawCircles(Mat mat, double uiImageWidth, double uiImageHeight, Canvas overlayCanvas, bool isDarkHole = true, double minRadius = 15, double maxRadius = 25, double param1 = 50, double param2 = 25)
        {
            if (mat == null || mat.Empty() || uiImageWidth <= 0 || uiImageHeight <= 0) return null;

            using Mat processMat = new Mat();
            if (mat.Channels() == 1)
            {
                Cv2.CvtColor(mat, processMat, ColorConversionCodes.GRAY2BGR);
            }
            else if (mat.Channels() == 4)
            {
                Cv2.CvtColor(mat, processMat, ColorConversionCodes.BGRA2BGR);
            }
            else
            {
                mat.CopyTo(processMat);
            }

            var detector = new CircleDetector();
            var result = detector.ProcessMetalHoles(processMat, isDarkHole: isDarkHole, minRadius: (int)minRadius, maxRadius: (int)maxRadius, param1: (int)param1, param2: (int)param2);

            double uniformScale = Math.Min(uiImageWidth / mat.Width, uiImageHeight / mat.Height);
            double renderedWidth = uniformScale * mat.Width;
            double renderedHeight = uniformScale * mat.Height;
            double offsetX = (uiImageWidth - renderedWidth) / 2.0;
            double offsetY = (uiImageHeight - renderedHeight) / 2.0;

            overlayCanvas.Children.Clear();

            if (result == null || result.Circles == null) return result;

            foreach (var circle in result.Circles)
            {
                double uiX = offsetX + circle.Center.X * uniformScale;
                double uiY = offsetY + circle.Center.Y * uniformScale;
                double uiRadius = circle.Radius * uniformScale;

                // 绘制外围矩形框（不能填充）
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Stroke = Brushes.LimeGreen,
                    StrokeThickness = 1.5,
                    Width = uiRadius * 2,
                    Height = uiRadius * 2,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(rect, uiX - uiRadius);
                Canvas.SetTop(rect, uiY - uiRadius);
                overlayCanvas.Children.Add(rect);

                // 绘制圆心十字
                double crossSize = 5;
                var hLine = new System.Windows.Shapes.Line
                {
                    X1 = uiX - crossSize, Y1 = uiY,
                    X2 = uiX + crossSize, Y2 = uiY,
                    Stroke = Brushes.Red, StrokeThickness = 1.5
                };
                var vLine = new System.Windows.Shapes.Line
                {
                    X1 = uiX, Y1 = uiY - crossSize,
                    X2 = uiX, Y2 = uiY + crossSize,
                    Stroke = Brushes.Red, StrokeThickness = 1.5
                };
                overlayCanvas.Children.Add(hLine);
                overlayCanvas.Children.Add(vLine);
            }

            return result;
        }
    }
}