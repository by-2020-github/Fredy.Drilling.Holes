using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Services;

public class DetectionResult : IDisposable
{
    public int Count => Circles.Count;
    public List<CircleSegment> Circles { get; set; } = new List<CircleSegment>();
    public Mat ResultImage { get; set; }
    public Mat BinaryImage { get; set; } // 新增：保存中间二值化结果
    public string Config { get; set; }

    public void Dispose()
    {
        ResultImage?.Dispose();
        BinaryImage?.Dispose();
    }
}

public class CircleDetector
{
  
    /// <summary>
    /// 处理通用暗场图像检测圆孔 (如第一张图)
    /// 原理：简单阈值使小孔变白，背景变黑。
    /// </summary>
    /// <param name="path">图片路径</param>
    /// <param name="minArea">最小像素面积</param>
    /// <param name="maxArea">最大像素面积</param>
    /// <param name="threshold">二值化阈值 (0-255)</param>
    /// <param name="circularity">圆度阈值 (0-1)，越高越严，建议 0.5-0.7</param>
    public DetectionResult Process(string path, int minArea = 2, int maxArea = 500, int threshold = 120, double circularity = 0.5)
    {
        using var src = new Mat(path, ImreadModes.Color);
        var result = new DetectionResult();
        result.ResultImage = src.Clone();
        result.Config = $"DarkField - Area:[{minArea}-{maxArea}], Threshold:{threshold}, Circularity:{circularity}";

        using var gray = new Mat();
        using var binary = new Mat();

        // 1. 预处理
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Threshold(gray, binary, threshold, 255, ThresholdTypes.Binary);

        // 2. 轮廓提取与筛选
        result.Circles = ProcessContours(binary, result.ResultImage, minArea, maxArea, circularity, Scalar.Lime); // 使用绿色

        return result;
    }

    /// <summary>
    /// 专门处理明场图像检测圆孔 (如第二张图)
    /// 原理：利用黑帽变换消除纹理和反光，使目标小孔变白，背景变黑。
    /// </summary>
    /// <param name="path">图片路径</param>
    /// <param name="minArea">最小像素面积</param>
    /// <param name="maxArea">最大像素面积</param>
    /// <param name="threshold">针对黑帽处理后小孔的阈值，建议 80-150</param>
    /// <param name="circularity">圆度阈值 (0-1)，越高越严，建议 0.6-0.8</param>
    /// <param name="morphologySize">黑帽变换的结构元素大小，应大于圆孔直径</param>
    public DetectionResult ProcessBrightField(string path, int minArea = 2, int maxArea = 500, int threshold = 100, double circularity = 0.6, int morphologySize = 15)
    {
        using var src = new Mat(path, ImreadModes.Color);
        var result = new DetectionResult();
        result.ResultImage = src.Clone();
        result.Config = $"BrightField - Area:[{minArea}-{maxArea}], Threshold:{threshold}, Circularity:{circularity}, MorphSize:{morphologySize}";

        using var gray = new Mat();
        using var preprocessed = new Mat();
        using var binary = new Mat();

        // 1. 灰度化
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        // 2. 黑帽变换 (突显暗斑，消除纹理)
        using var element = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(morphologySize, morphologySize));
        Cv2.MorphologyEx(gray, preprocessed, MorphTypes.BlackHat, element);

        // 3. 简单阈值提取小孔
        Cv2.Threshold(preprocessed, binary, threshold, 255, ThresholdTypes.Binary);

        // 4. 轮廓提取与筛选
        result.Circles = ProcessContours(binary, result.ResultImage, minArea, maxArea, circularity, Scalar.Red); // 使用蓝色

        return result;
    }
    public DetectionResult ProcessBrightFieldDebug(string path, int minArea = 1, int maxArea = 100, int threshold = 30, double circularity = 0.2)
    {
        using var src = new Mat(path, ImreadModes.Color);
        var result = new DetectionResult();
        result.ResultImage = src.Clone();
        result.Config = $"Debug-Bright: Area:[{minArea}-{maxArea}], Threshold:{threshold}, Circularity:{circularity}";

        using var gray = new Mat();
        using var enhanced = new Mat();
        using var blackHat = new Mat();

        // 1. 预处理
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        // 局部对比度增强
        using var clahe = Cv2.CreateCLAHE(3.0, new Size(8, 8));
        clahe.Apply(gray, enhanced);

        // 2. 黑帽变换：提取比背景暗的小点
        using var element = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(10, 10));
        Cv2.MorphologyEx(enhanced, blackHat, MorphTypes.BlackHat, element);

        // 3. 二值化
        result.BinaryImage = new Mat();
        Cv2.Threshold(blackHat, result.BinaryImage, threshold, 255, ThresholdTypes.Binary);

        // 4. 提取轮廓并标记
        Cv2.FindContours(result.BinaryImage, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        foreach (var contour in contours)
        {
            double area = Cv2.ContourArea(contour);
            if (area >= minArea && area <= maxArea)
            {
                double perimeter = Cv2.ArcLength(contour, true);
                double c = (perimeter > 0) ? (4 * Math.PI * area) / (perimeter * perimeter) : 0;

                if (c >= circularity)
                {
                    var rect = Cv2.BoundingRect(contour);
                    Point center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                    int radius = Math.Max(rect.Width, rect.Height) / 2;

                    result.Circles.Add(new CircleSegment(center, radius));

                    // 用红色粗线标记，方便在 LINQPad 缩略图中查看
                    Cv2.Circle(result.ResultImage, center, radius > 0 ? radius : 1, Scalar.Red, 1);
                }
            }
        }
        return result;
    }
    /// <summary>
    /// 通用的轮廓提取和筛选逻辑
    /// </summary>
    private List<CircleSegment> ProcessContours(Mat binary, Mat resultImage, int minArea, int maxArea, double circularity, Scalar drawColor)
    {
        var detectedCircles = new List<CircleSegment>();
        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        foreach (var contour in contours)
        {
            double area = Cv2.ContourArea(contour);

            // 面积筛选
            if (area >= minArea && area <= maxArea)
            {
                double perimeter = Cv2.ArcLength(contour, true);
                if (perimeter <= 0) continue;

                // 圆度筛选
                double c = (4 * Math.PI * area) / (perimeter * perimeter);
                if (c >= circularity)
                {
                    // 获取外接圆
                    Cv2.MinEnclosingCircle(contour, out var center, out float radius);

                    detectedCircles.Add(new CircleSegment(center, radius));

                    // 绘图
                    Cv2.Circle(resultImage, (Point)center, (int)radius, drawColor, 1);
                }
            }
        }
        return detectedCircles;
    }
}

