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

    public sealed class DetectedCircleInfo
    {
        public DetectedCircleInfo(OpenCvSharp.Point2d center, double radius)
        {
            Center = center;
            Radius = radius;
        }

        public OpenCvSharp.Point2d Center { get; }

        public double Radius { get; }
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

        public static bool TryConvertUiPointToImagePixel(int sourceWidth, int sourceHeight, double uiImageWidth, double uiImageHeight, System.Windows.Point uiPoint, out OpenCvSharp.Point2d imagePoint)
        {
            imagePoint = default;

            if (sourceWidth <= 0 || sourceHeight <= 0 || uiImageWidth <= 0 || uiImageHeight <= 0)
            {
                return false;
            }

            double uniformScale = Math.Min(uiImageWidth / sourceWidth, uiImageHeight / sourceHeight);
            double renderedWidth = uniformScale * sourceWidth;
            double renderedHeight = uniformScale * sourceHeight;
            double offsetX = (uiImageWidth - renderedWidth) / 2.0;
            double offsetY = (uiImageHeight - renderedHeight) / 2.0;

            if (uiPoint.X < offsetX || uiPoint.X > offsetX + renderedWidth || uiPoint.Y < offsetY || uiPoint.Y > offsetY + renderedHeight)
            {
                return false;
            }

            imagePoint = new OpenCvSharp.Point2d(
                (uiPoint.X - offsetX) / uniformScale,
                (uiPoint.Y - offsetY) / uniformScale);
            return true;
        }

        public static bool TryConvertUiRectToImageRect(int sourceWidth, int sourceHeight, double uiImageWidth, double uiImageHeight, System.Windows.Rect uiRect, out OpenCvSharp.Rect imageRect)
        {
            imageRect = default;

            if (sourceWidth <= 0 || sourceHeight <= 0 || uiImageWidth <= 0 || uiImageHeight <= 0 || uiRect.Width <= 0 || uiRect.Height <= 0)
            {
                return false;
            }

            double uniformScale = Math.Min(uiImageWidth / sourceWidth, uiImageHeight / sourceHeight);
            double renderedWidth = uniformScale * sourceWidth;
            double renderedHeight = uniformScale * sourceHeight;
            double offsetX = (uiImageWidth - renderedWidth) / 2.0;
            double offsetY = (uiImageHeight - renderedHeight) / 2.0;

            var renderedBounds = new System.Windows.Rect(offsetX, offsetY, renderedWidth, renderedHeight);
            var clampedRect = System.Windows.Rect.Intersect(renderedBounds, uiRect);
            if (clampedRect.IsEmpty || clampedRect.Width <= 0 || clampedRect.Height <= 0)
            {
                return false;
            }

            int x = (int)Math.Floor((clampedRect.X - offsetX) / uniformScale);
            int y = (int)Math.Floor((clampedRect.Y - offsetY) / uniformScale);
            int right = (int)Math.Ceiling((clampedRect.Right - offsetX) / uniformScale);
            int bottom = (int)Math.Ceiling((clampedRect.Bottom - offsetY) / uniformScale);

            x = Math.Clamp(x, 0, sourceWidth - 1);
            y = Math.Clamp(y, 0, sourceHeight - 1);
            right = Math.Clamp(right, x + 1, sourceWidth);
            bottom = Math.Clamp(bottom, y + 1, sourceHeight);

            imageRect = new OpenCvSharp.Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
            return true;
        }

        public static System.Windows.Point ConvertImagePointToUiPoint(int sourceWidth, int sourceHeight, double uiImageWidth, double uiImageHeight, OpenCvSharp.Point2d imagePoint)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || uiImageWidth <= 0 || uiImageHeight <= 0)
            {
                return new System.Windows.Point();
            }

            double uniformScale = Math.Min(uiImageWidth / sourceWidth, uiImageHeight / sourceHeight);
            double renderedWidth = uniformScale * sourceWidth;
            double renderedHeight = uniformScale * sourceHeight;
            double offsetX = (uiImageWidth - renderedWidth) / 2.0;
            double offsetY = (uiImageHeight - renderedHeight) / 2.0;

            return new System.Windows.Point(
                offsetX + imagePoint.X * uniformScale,
                offsetY + imagePoint.Y * uniformScale);
        }

        public static System.Windows.Rect ConvertImageRectToUiRect(int sourceWidth, int sourceHeight, double uiImageWidth, double uiImageHeight, OpenCvSharp.Rect imageRect)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || uiImageWidth <= 0 || uiImageHeight <= 0)
            {
                return System.Windows.Rect.Empty;
            }

            double uniformScale = Math.Min(uiImageWidth / sourceWidth, uiImageHeight / sourceHeight);
            double renderedWidth = uniformScale * sourceWidth;
            double renderedHeight = uniformScale * sourceHeight;
            double offsetX = (uiImageWidth - renderedWidth) / 2.0;
            double offsetY = (uiImageHeight - renderedHeight) / 2.0;

            return new System.Windows.Rect(
                offsetX + imageRect.X * uniformScale,
                offsetY + imageRect.Y * uniformScale,
                imageRect.Width * uniformScale,
                imageRect.Height * uniformScale);
        }

        public static bool TryDetectDarkCircleInRoi(Mat source, OpenCvSharp.Rect roiRect, out DetectedCircleInfo? detectedCircle)
        {
            detectedCircle = null;

            if (source == null || source.Empty() || roiRect.Width <= 0 || roiRect.Height <= 0)
            {
                return false;
            }

            var boundedRect = new OpenCvSharp.Rect(
                Math.Clamp(roiRect.X, 0, source.Width - 1),
                Math.Clamp(roiRect.Y, 0, source.Height - 1),
                Math.Clamp(roiRect.Width, 1, source.Width - Math.Clamp(roiRect.X, 0, source.Width - 1)),
                Math.Clamp(roiRect.Height, 1, source.Height - Math.Clamp(roiRect.Y, 0, source.Height - 1)));

            using var roi = new Mat(source, boundedRect);
            using var gray = new Mat();
            switch (roi.Channels())
            {
                case 1:
                    roi.CopyTo(gray);
                    break;
                case 3:
                    Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
                    break;
                case 4:
                    Cv2.CvtColor(roi, gray, ColorConversionCodes.BGRA2GRAY);
                    break;
                default:
                    return false;
            }

            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);

            using var binary = new Mat();
            Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));
            using var cleaned = new Mat();
            Cv2.MorphologyEx(binary, cleaned, MorphTypes.Open, kernel);
            Cv2.MorphologyEx(cleaned, cleaned, MorphTypes.Close, kernel);

            Cv2.FindContours(cleaned, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var roiCenter = new OpenCvSharp.Point2d(roi.Width / 2.0, roi.Height / 2.0);
            double maxDistance = Math.Max(1.0, Math.Sqrt((roi.Width * roi.Width) + (roi.Height * roi.Height)) / 2.0);
            double bestScore = double.MinValue;

            foreach (var contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < 16 || area > roi.Width * roi.Height * 0.9)
                {
                    continue;
                }

                double perimeter = Cv2.ArcLength(contour, true);
                if (perimeter <= 0)
                {
                    continue;
                }

                double circularity = (4 * Math.PI * area) / (perimeter * perimeter);
                if (circularity < 0.45)
                {
                    continue;
                }

                Cv2.MinEnclosingCircle(contour, out var localCenter, out float radius);
                if (radius < 2 || radius > Math.Min(roi.Width, roi.Height) / 2.0)
                {
                    continue;
                }

                double distance = Math.Sqrt(Math.Pow(localCenter.X - roiCenter.X, 2) + Math.Pow(localCenter.Y - roiCenter.Y, 2));
                double fillRatio = area / Math.Max(1.0, Math.PI * radius * radius);
                double score = (circularity * 2.0) + fillRatio - (distance / maxDistance);
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                detectedCircle = new DetectedCircleInfo(
                    new OpenCvSharp.Point2d(boundedRect.X + localCenter.X, boundedRect.Y + localCenter.Y),
                    radius);
            }

            if (detectedCircle != null)
            {
                return true;
            }

            int minRadius = Math.Max(3, Math.Min(roi.Width, roi.Height) / 12);
            int maxRadius = Math.Max(minRadius + 1, Math.Min(roi.Width, roi.Height) / 2);
            var circles = Cv2.HoughCircles(blurred, HoughModes.Gradient, 1.2, Math.Max(10, minRadius), 120, 18, minRadius, maxRadius);
            if (circles.Length == 0)
            {
                return false;
            }

            var bestCircle = circles
                .OrderBy(circle => Math.Sqrt(Math.Pow(circle.Center.X - roiCenter.X, 2) + Math.Pow(circle.Center.Y - roiCenter.Y, 2)))
                .First();

            detectedCircle = new DetectedCircleInfo(
                new OpenCvSharp.Point2d(boundedRect.X + bestCircle.Center.X, boundedRect.Y + bestCircle.Center.Y),
                bestCircle.Radius);
            return true;
        }

        public static bool TryFindWhiteObjectEdgePoint(Mat source, OpenCvSharp.Point2d approximatePoint, out OpenCvSharp.Point2d edgePoint)
        {
            edgePoint = default;

            if (source == null || source.Empty())
            {
                return false;
            }

            using var gray = new Mat();
            switch (source.Channels())
            {
                case 1:
                    source.CopyTo(gray);
                    break;
                case 3:
                    Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
                    break;
                case 4:
                    Cv2.CvtColor(source, gray, ColorConversionCodes.BGRA2GRAY);
                    break;
                default:
                    return false;
            }

            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(5, 5), 0);

            using var binary = new Mat();
            Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(5, 5));
            using var cleaned = new Mat();
            Cv2.MorphologyEx(binary, cleaned, MorphTypes.Close, kernel);
            Cv2.MorphologyEx(cleaned, cleaned, MorphTypes.Open, kernel);

            var imageCenter = new OpenCvSharp.Point2d(source.Width / 2.0, source.Height / 2.0);
            if (SampleBinary(cleaned, imageCenter) == 0)
            {
                Cv2.BitwiseNot(cleaned, cleaned);
            }

            double dx = approximatePoint.X - imageCenter.X;
            double dy = approximatePoint.Y - imageCenter.Y;
            double clickDistance = Math.Sqrt((dx * dx) + (dy * dy));
            if (clickDistance < 1.0)
            {
                return false;
            }

            double unitX = dx / clickDistance;
            double unitY = dy / clickDistance;
            double searchRange = Math.Clamp(clickDistance * 0.25, 18.0, 60.0);
            int start = Math.Max(1, (int)Math.Floor(clickDistance - searchRange));
            int end = Math.Min((int)Math.Ceiling(clickDistance + searchRange), (int)Math.Ceiling(Math.Sqrt((source.Width * source.Width) + (source.Height * source.Height))));

            double? preferredTransition = null;
            double preferredScore = double.MaxValue;
            double? fallbackTransition = null;
            double fallbackScore = double.MaxValue;

            for (int distance = start; distance <= end; distance++)
            {
                var inwardPoint = new OpenCvSharp.Point2d(imageCenter.X + unitX * (distance - 1), imageCenter.Y + unitY * (distance - 1));
                var outwardPoint = new OpenCvSharp.Point2d(imageCenter.X + unitX * (distance + 1), imageCenter.Y + unitY * (distance + 1));
                byte inwardValue = SampleBinary(cleaned, inwardPoint);
                byte outwardValue = SampleBinary(cleaned, outwardPoint);

                if (inwardValue == outwardValue)
                {
                    continue;
                }

                double score = Math.Abs(distance - clickDistance);
                if (inwardValue > 0 && outwardValue == 0)
                {
                    if (score < preferredScore)
                    {
                        preferredScore = score;
                        preferredTransition = distance;
                    }

                    continue;
                }

                if (score < fallbackScore)
                {
                    fallbackScore = score;
                    fallbackTransition = distance;
                }
            }

            double? targetDistance = preferredTransition ?? fallbackTransition;
            if (!targetDistance.HasValue)
            {
                return false;
            }

            edgePoint = new OpenCvSharp.Point2d(
                imageCenter.X + unitX * targetDistance.Value,
                imageCenter.Y + unitY * targetDistance.Value);
            return true;
        }

        private static byte SampleBinary(Mat binary, OpenCvSharp.Point2d point)
        {
            int x = Math.Clamp((int)Math.Round(point.X), 0, binary.Width - 1);
            int y = Math.Clamp((int)Math.Round(point.Y), 0, binary.Height - 1);
            return binary.At<byte>(y, x);
        }

        public static CenterRoiBinaryPreviewResult? BuildCenterRoiBinaryPreview(BitmapSource source, int roiWidth, int roiHeight, int threshold, bool invert, int circleRadius = 30)
        {
            if (source == null) return null;

            using Mat mat = BitmapSourceToMat(source);
            return BuildCenterRoiBinaryPreview(mat, roiWidth, roiHeight, threshold, invert, circleRadius);
        }

        public static CenterRoiBinaryPreviewResult? BuildCenterRoiBinaryPreview(Mat source, int roiWidth, int roiHeight, int threshold, bool invert, int circleRadius = 30)
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

            var roiWithMarkers = roiImage.Clone();
            DrawCenterCrossAndCircle(roiWithMarkers, circleRadius);

            DrawCenterCrossAndCircle(binaryImage, circleRadius);

            return new CenterRoiBinaryPreviewResult(roiWithMarkers, binaryImage, roiRect, source.Width, source.Height);
        }

        private static void DrawCenterCrossAndCircle(Mat image, int circleRadius)
        {
            if (image == null || image.Empty()) return;

            int centerX = image.Width / 2;
            int centerY = image.Height / 2;

            // 灰度图用白色（128灰），彩色图用红色
            var color = image.Channels() == 1 ? new Scalar(128) : new Scalar(0, 0, 255);
            int thickness = 1;

            Cv2.Line(image, new OpenCvSharp.Point(centerX, 0), new OpenCvSharp.Point(centerX, image.Height), color, thickness);
            Cv2.Line(image, new OpenCvSharp.Point(0, centerY), new OpenCvSharp.Point(image.Width, centerY), color, thickness);

            int radius = Math.Max(1, Math.Min(circleRadius, Math.Min(image.Width, image.Height) / 2));
            Cv2.Circle(image, new OpenCvSharp.Point(centerX, centerY), radius, color, thickness);
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
                double crossStrokeThickness = Math.Clamp((uiRadius * 2.0) / 10.0, 1.0, 3.0);

                // 按检测结果绘制圆边缘轮廓，不再绘制外接矩形。
                var ellipse = new System.Windows.Shapes.Ellipse
                {
                    Stroke = Brushes.LimeGreen,
                    StrokeThickness = 1.5,
                    Width = uiRadius * 2,
                    Height = uiRadius * 2,
                    Fill = Brushes.Transparent
                };
                Canvas.SetLeft(ellipse, uiX - uiRadius);
                Canvas.SetTop(ellipse, uiY - uiRadius);
                overlayCanvas.Children.Add(ellipse);

                // 绘制圆心十字
                double crossSize = 5;
                var hLine = new System.Windows.Shapes.Line
                {
                    X1 = uiX - crossSize, Y1 = uiY,
                    X2 = uiX + crossSize, Y2 = uiY,
                    Stroke = Brushes.Red, StrokeThickness = crossStrokeThickness
                };
                var vLine = new System.Windows.Shapes.Line
                {
                    X1 = uiX, Y1 = uiY - crossSize,
                    X2 = uiX, Y2 = uiY + crossSize,
                    Stroke = Brushes.Red, StrokeThickness = crossStrokeThickness
                };
                overlayCanvas.Children.Add(hLine);
                overlayCanvas.Children.Add(vLine);
            }

            return result;
        }
    }
}