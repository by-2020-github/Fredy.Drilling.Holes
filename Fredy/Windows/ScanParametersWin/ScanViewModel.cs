using BLL;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using HAL;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Common.Models;
using Common.Services;
using Common.Tools;
using Microsoft.Win32;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ScanViewModel : ObservableObject
    {
        private const double PreviewCanvasSizeValue = 620;
        private readonly ICamera? _camera;
        private readonly IMotionService? _motionService;
        private readonly ConfigService? _configService;
        private readonly ISecondPassAlignmentContext? _secondPassAlignmentContext;
        private readonly RecipeService? _recipeService;
        private readonly CircleDetector _circleDetector = new();
        private readonly List<ScanShotPoint> _scanPoints = new();
        private readonly Dictionary<int, Mat> _capturedTileMats = new();
        private readonly List<Point2d> _detectedHoleCandidates = new();
        private readonly List<Point2d> _detectedHoleCoordinates = new();
        private readonly string _debugImageDirectory = Path.Combine(AppContext.BaseDirectory, "images");
        private CancellationTokenSource? _scanCancellationTokenSource;
        private int _debugRenderIndex;
        private AffineTransform2D _pendingAlignmentTransform = AffineTransform2D.Identity;
        private bool _hasPendingAlignmentTransform;
        private int _lastMatchedPointCount;

        [ObservableProperty] private ScanParameters _params = new();
        [ObservableProperty] private OpenCvSharp.Mat? _stitchedPreviewMat;
        [ObservableProperty] private bool _showGridOverlay = true;

        public ScanViewModel()
            : this(null, null, null, null, null)
        {
        }

        public ScanViewModel(ConfigService? configService)
            : this(null, null, configService, null, null)
        {
        }

        public ScanViewModel(ICamera? camera, IMotionService? motionService, ConfigService? configService, ISecondPassAlignmentContext? secondPassAlignmentContext, RecipeService? recipeService)
         {
             _camera = camera;
             _motionService = motionService;
             _configService = configService;
             _secondPassAlignmentContext = secondPassAlignmentContext;
            _recipeService = recipeService;
             PreviewGridCells = new ObservableCollection<ScanGridCellVisual>();
             LoadDefaultsFromConfig(_configService?.CurrentConfig);
             Params.ScanStatus = "等待计算...";
         }

        public ObservableCollection<ScanGridCellVisual> PreviewGridCells { get; }

        public double PreviewCanvasSize => PreviewCanvasSizeValue;

        // 命令实现
        [RelayCommand]
        private void CalculateCoordinates()
        {
            ShowGridOverlay = true;
            NormalizeInputs();
            BuildScanPlan();
            RenderStitchedPreview();

            Params.PhotoIndex = 0;
            Params.ProgressValue = 0;
            Params.ScanStatus = $"坐标计算完成：{Params.RowCount}行 x {Params.ColumnCount}列，共{Params.TotalShots}点。";
        }

        [RelayCommand]
        private async Task StartScanAsync()
        {
            ShowGridOverlay = true;
            _hasPendingAlignmentTransform = false;
            _pendingAlignmentTransform = AffineTransform2D.Identity;
            _lastMatchedPointCount = 0;

             if (_scanPoints.Count == 0)
             {
                 CalculateCoordinates();
             }

            if (_scanPoints.Count == 0)
            {
                Params.ScanStatus = "无可扫描点位。";
                return;
            }

            ResetScanRunState();
            _secondPassAlignmentContext?.Clear();
            _detectedHoleCandidates.Clear();
            _detectedHoleCoordinates.Clear();

             _scanCancellationTokenSource?.Cancel();
             _scanCancellationTokenSource = new CancellationTokenSource();
             var token = _scanCancellationTokenSource.Token;

            if (_camera is null)
            {
                Params.ScanStatus = "未注入相机服务，无法执行拍照拼接。";
                return;
            }

            if (!_camera.IsConnected)
            {
                _camera.Open();
            }

            Params.ScanStatus = "正在执行Z字形软触发扫描...";

            try
            {
                for (int i = 0; i < _scanPoints.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var shot = _scanPoints[i];

                    await MoveToScanPointAsync(shot, token);
                    await Task.Delay(Math.Max(1, Params.SettleTime), token);

                    var captured = await CaptureFrameAsync(token);
                    if (captured is not null)
                    {
                        if (_capturedTileMats.TryGetValue(shot.ShotIndex, out var oldTile))
                        {
                            oldTile.Dispose();
                        }

                        _capturedTileMats[shot.ShotIndex] = captured;
                        var tileHoles = DetectTileHoleCoordinates(captured, shot);
                        if (tileHoles.Count > 0)
                        {
                            _detectedHoleCandidates.AddRange(tileHoles);
                        }
                     }

                    Params.PhotoIndex = i + 1;
                    Params.CurrentX = Math.Round(shot.X, 3);
                    Params.CurrentY = Math.Round(shot.Y, 3);
                    Params.ProgressValue = Math.Round((i + 1) * 100.0 / _scanPoints.Count, 2);

                    if (shot.ShotIndex >= 0 && shot.ShotIndex < PreviewGridCells.Count)
                    {
                        PreviewGridCells[shot.ShotIndex].IsScanned = true;
                    }

                    await RenderStitchedPreviewOnUiThreadAsync();
                    Params.ScanStatus = captured is null
                        ? $"扫描中 {i + 1}/{_scanPoints.Count}（当前帧为空）"
                        : $"扫描并拼接中 {i + 1}/{_scanPoints.Count}，已拼接 {_capturedTileMats.Count} 张，检测候选孔 {_detectedHoleCandidates.Count} 个";
                }

                Params.ScanStatus = "扫描完成，已完成实时拼接。";
                FinalizeSecondPassAlignmentPreparation();
            }
            catch (OperationCanceledException)
            {
                Params.ScanStatus = "扫描已停止。";
            }
            catch (Exception ex)
            {
                Params.ScanStatus = $"扫描失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private void StopScan()
        {
            _scanCancellationTokenSource?.Cancel();
            Params.ScanStatus = "正在停止扫描...";
        }

        [RelayCommand]
        private void Test()
        {
            CalculateCoordinates();
        }

        [RelayCommand]
        private void SaveDetectionParams()
        {
            if (_configService is null)
            {
                Params.ScanStatus = "配置服务未初始化，无法保存检测参数。";
                return;
            }

            try
            {
                var config = _configService.CurrentConfig;
                config.ScanUseBrightFieldDetector = Params.UseBrightFieldDetector;
                config.ScanDetectMinArea = Math.Max(1, Params.DetectMinArea);
                config.ScanDetectMaxArea = Math.Max(config.ScanDetectMinArea, Params.DetectMaxArea);
                config.ScanDetectThreshold = Math.Clamp(Params.DetectThreshold, 0, 255);
                config.ScanDetectCircularity = Math.Clamp(Params.DetectCircularity, 0.01, 1.0);

                var morphologySize = Math.Max(3, Params.DetectMorphologySize);
                if (morphologySize % 2 == 0)
                {
                    morphologySize++;
                }

                config.ScanDetectMorphologySize = morphologySize;
                config.ScanDeduplicateToleranceMm = Math.Max(0.001, Params.DeduplicateToleranceMm);

                _configService.SaveWithArchive(config);

                Params.DetectMinArea = config.ScanDetectMinArea;
                Params.DetectMaxArea = config.ScanDetectMaxArea;
                Params.DetectThreshold = config.ScanDetectThreshold;
                Params.DetectCircularity = config.ScanDetectCircularity;
                Params.DetectMorphologySize = config.ScanDetectMorphologySize;
                Params.DeduplicateToleranceMm = config.ScanDeduplicateToleranceMm;

                Params.ScanStatus = "检测参数已保存到配置。";
            }
            catch (Exception ex)
            {
                Params.ScanStatus = $"保存检测参数失败：{ex.Message}";
            }
        }
 
         [RelayCommand]
         private async Task TestDetectionFromFileAsync()
         {
             var dialog = new OpenFileDialog
             {
                 Title = "选择待测试图像",
                 Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff"
             };

             if (dialog.ShowDialog() != true)
             {
                 return;
             }

             using var image = Cv2.ImRead(dialog.FileName, ImreadModes.Color);
             if (image.Empty())
             {
                 Params.ScanStatus = "导入图片失败，无法执行检测。";
                 return;
             }

             RunSingleImageDetectionTest(image, "导入图片");
         }

        [RelayCommand]
        private async Task TestDetectionFromCameraAsync()
        {
            if (_camera is null)
            {
                Params.ScanStatus = "未注入相机服务，无法拍照测试。";
                return;
            }

            if (!_camera.IsConnected)
            {
                _camera.Open();
            }

            using var frame = await CaptureFrameAsync(CancellationToken.None);
            if (frame is null || frame.Empty())
            {
                Params.ScanStatus = "拍照测试失败，未获取到有效图像。";
                return;
            }

            RunSingleImageDetectionTest(frame, "拍照测试");
        }

        private void RunSingleImageDetectionTest(Mat image, string sourceName)
        {
            try
            {
                ShowGridOverlay = false;
                using var detection = DetectCircles(image);
                using var preview = detection.ResultImage.Clone();

                for (int i = 0; i < detection.Circles.Count; i++)
                {
                    var c = detection.Circles[i];
                    var center = new OpenCvSharp.Point((int)Math.Round(c.Center.X), (int)Math.Round(c.Center.Y));
                    var radius = Math.Max(2, (int)Math.Round(c.Radius));
                    Cv2.Circle(preview, center, radius, Scalar.Yellow, 2);
                    Cv2.Circle(preview, center, 2, Scalar.Red, -1);
                }

                var oldMat = StitchedPreviewMat;
                StitchedPreviewMat = preview.Clone();
                oldMat?.Dispose();
                Params.ScanStatus = $"{sourceName}检测完成：识别圆孔 {detection.Circles.Count} 个。";
            }
            catch (Exception ex)
            {
                Params.ScanStatus = $"{sourceName}检测失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private void ApplySecondPassAlignment()
        {
            if (_secondPassAlignmentContext is null)
            {
                Params.ScanStatus = "坐标校准上下文未初始化，无法应用二道矩阵。";
                return;
            }

            if (!_hasPendingAlignmentTransform)
            {
                Params.ScanStatus = "请先完成分区扫描并成功计算坐标变换矩阵，再执行二道校准应用。";
                return;
            }

            _secondPassAlignmentContext.SetTransform(_pendingAlignmentTransform);
            Params.ScanStatus = $"二道坐标校准成功，已写入坐标变换矩阵（匹配点 {_lastMatchedPointCount}），可返回主界面继续二道冲孔。";
        }

        private List<Point2d> DetectTileHoleCoordinates(Mat tile, ScanShotPoint shot)
        {
            var result = new List<Point2d>();
            if (tile.Empty())
            {
                return result;
            }

            double pixelSizeXUm = _configService?.CurrentConfig?.Camera.PixelSizeX ?? 3.45;
            double pixelSizeYUm = _configService?.CurrentConfig?.Camera.PixelSizeY ?? 3.45;
            double pixelSizeXmm = Math.Max(0.0001, pixelSizeXUm / 1000.0);
            double pixelSizeYmm = Math.Max(0.0001, pixelSizeYUm / 1000.0);

            using var detectorInput = tile.Clone();
            using var detection = DetectCircles(detectorInput);
            if (detection.Circles.Count == 0)
            {
                return result;
            }

            double centerX = tile.Width / 2.0;
            double centerY = tile.Height / 2.0;

            foreach (var circle in detection.Circles)
            {
                double dxPixel = circle.Center.X - centerX;
                double dyPixel = circle.Center.Y - centerY;

                // 图像Y向下为正，机台Y按数学坐标系向上为正
                double physicalX = shot.X + (dxPixel * pixelSizeXmm);
                double physicalY = shot.Y - (dyPixel * pixelSizeYmm);
                result.Add(new Point2d(physicalX, physicalY));
            }

            return result;
        }

        private DetectionResult DetectCircles(Mat input)
        {
            int minArea = Math.Max(1, Params.DetectMinArea);
            int maxArea = Math.Max(minArea, Params.DetectMaxArea);
            int threshold = Math.Clamp(Params.DetectThreshold, 0, 255);
            double circularity = Math.Clamp(Params.DetectCircularity, 0.01, 1.0);
            int morphologySize = Math.Max(3, Params.DetectMorphologySize);
            if (morphologySize % 2 == 0)
            {
                morphologySize++;
            }

            if (Params.UseBrightFieldDetector)
            {
                return _circleDetector.ProcessBrightField(input, minArea, maxArea, threshold, circularity, morphologySize);
            }

            return _circleDetector.Process(input, minArea, maxArea, threshold, circularity);
        }

        private void FinalizeSecondPassAlignmentPreparation()
        {
            _detectedHoleCoordinates.Clear();

            if (_detectedHoleCandidates.Count == 0)
            {
                Params.ScanStatus = "扫描完成，但未检测到孔位，请检查光照与检测参数。";
                return;
            }

            var dedupToleranceMm = Math.Max(0.001, Params.DeduplicateToleranceMm);
            var deduped = DeduplicatePoints(_detectedHoleCandidates, dedupToleranceMm);
            _detectedHoleCoordinates.AddRange(deduped);

            if (!TryGetRecipePoints(out var recipePoints, out var recipeName))
            {
                Params.ScanStatus = $"扫描完成，检测候选 {_detectedHoleCandidates.Count} 个、去重后 {_detectedHoleCoordinates.Count} 个，但未找到当前配方，无法计算坐标矩阵。";
                return;
            }

            if (!TryEstimateTransform(recipePoints, _detectedHoleCoordinates, out var transform, out var matchedCount))
            {
                Params.ScanStatus = $"扫描完成，检测候选 {_detectedHoleCandidates.Count} 个、去重后 {_detectedHoleCoordinates.Count} 个；与配方[{recipeName}]自动匹配失败，无法生成坐标矩阵。";
                return;
            }

            _pendingAlignmentTransform = transform;
            _hasPendingAlignmentTransform = true;
            _lastMatchedPointCount = matchedCount;
            Params.ScanStatus = $"扫描完成，候选 {_detectedHoleCandidates.Count} 个，去重后 {_detectedHoleCoordinates.Count} 个；与配方[{recipeName}]匹配 {matchedCount} 个，已计算坐标变换矩阵。";
        }

        private bool TryGetRecipePoints(out List<Point2d> recipePoints, out string recipeName)
        {
            recipePoints = new List<Point2d>();
            recipeName = string.Empty;

            if (_recipeService is null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(Params.WorkpieceType))
            {
                _recipeService.Load(Params.WorkpieceType);
            }

            var recipe = _recipeService.CurrentRecipe;
            if (recipe?.PunchParameters?.PunchPoints is not { Count: > 0 } points)
            {
                return false;
            }

            recipeName = recipe.RecipeName;
            foreach (var p in points)
            {
                recipePoints.Add(new Point2d(p.X, p.Y));
            }

            return recipePoints.Count > 0;
        }

        private static List<Point2d> DeduplicatePoints(IEnumerable<Point2d> points, double toleranceMm)
        {
            var clusters = new List<PointCluster>();

            foreach (var point in points)
            {
                int bestIndex = -1;
                double bestDistance = double.MaxValue;

                for (int i = 0; i < clusters.Count; i++)
                {
                    double dist = GetDistance(point, clusters[i].Center);
                    if (dist <= toleranceMm && dist < bestDistance)
                    {
                        bestDistance = dist;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                {
                    clusters.Add(new PointCluster(point));
                }
                else
                {
                    clusters[bestIndex].Add(point);
                }
            }

            return clusters.Select(c => c.Center).ToList();
        }

        private static bool TryEstimateTransform(
            IReadOnlyList<Point2d> recipePoints,
            IReadOnlyList<Point2d> detectedPoints,
            out AffineTransform2D transform,
            out int matchedCount)
        {
            transform = AffineTransform2D.Identity;
            matchedCount = 0;

            if (recipePoints.Count < 3 || detectedPoints.Count < 3)
            {
                return false;
            }

            var recipeCentroid = ComputeCentroid(recipePoints);
            var detectedCentroid = ComputeCentroid(detectedPoints);
            transform = new AffineTransform2D(1, 0, 0, 1, detectedCentroid.X - recipeCentroid.X, detectedCentroid.Y - recipeCentroid.Y);

            double maxMatchDistance = EstimateInitialMatchDistance(recipePoints);

            for (int iter = 0; iter < 6; iter++)
            {
                var pairs = BuildPairs(recipePoints, detectedPoints, transform, maxMatchDistance);
                if (pairs.Count < 3)
                {
                    break;
                }

                if (!TrySolveAffine(pairs, out var solved))
                {
                    break;
                }

                // 一次残差过滤，提升鲁棒性
                var filteredPairs = pairs
                    .Where(p => GetDistance(solved.Transform(p.Source.X, p.Source.Y), (p.Target.X, p.Target.Y)) <= maxMatchDistance)
                    .ToList();

                if (filteredPairs.Count >= 3 && TrySolveAffine(filteredPairs, out var refined))
                {
                    transform = refined;
                    matchedCount = filteredPairs.Count;
                }
                else
                {
                    transform = solved;
                    matchedCount = pairs.Count;
                }

                maxMatchDistance = Math.Max(0.06, maxMatchDistance * 0.85);
            }

            return matchedCount >= 3;
        }

        private static List<PointPair> BuildPairs(
            IReadOnlyList<Point2d> recipePoints,
            IReadOnlyList<Point2d> detectedPoints,
            AffineTransform2D transform,
            double maxDistance)
        {
            var pairs = new List<PointPair>();
            var used = new HashSet<int>();

            foreach (var source in recipePoints)
            {
                var mapped = transform.Transform(source.X, source.Y);
                int nearestIndex = -1;
                double nearestDistance = double.MaxValue;

                for (int i = 0; i < detectedPoints.Count; i++)
                {
                    if (used.Contains(i))
                    {
                        continue;
                    }

                    double dist = GetDistance(mapped, (detectedPoints[i].X, detectedPoints[i].Y));
                    if (dist < nearestDistance)
                    {
                        nearestDistance = dist;
                        nearestIndex = i;
                    }
                }

                if (nearestIndex >= 0 && nearestDistance <= maxDistance)
                {
                    used.Add(nearestIndex);
                    pairs.Add(new PointPair(source, detectedPoints[nearestIndex]));
                }
            }

            return pairs;
        }

        private static bool TrySolveAffine(IReadOnlyList<PointPair> pairs, out AffineTransform2D transform)
        {
            transform = AffineTransform2D.Identity;
            if (pairs.Count < 3)
            {
                return false;
            }

            var ata = new double[6, 6];
            var atb = new double[6];

            foreach (var pair in pairs)
            {
                var x = pair.Source.X;
                var y = pair.Source.Y;
                var u = pair.Target.X;
                var v = pair.Target.Y;

                var r1 = new[] { x, y, 0d, 0d, 1d, 0d };
                var r2 = new[] { 0d, 0d, x, y, 0d, 1d };

                AccumulateNormalEquation(ata, atb, r1, u);
                AccumulateNormalEquation(ata, atb, r2, v);
            }

            if (!TrySolveLinearSystem(ata, atb, out var p))
            {
                return false;
            }

            transform = new AffineTransform2D(p[0], p[1], p[2], p[3], p[4], p[5]);
            return true;
        }

        private static void AccumulateNormalEquation(double[,] ata, double[] atb, double[] row, double value)
        {
            for (int i = 0; i < 6; i++)
            {
                atb[i] += row[i] * value;
                for (int j = 0; j < 6; j++)
                {
                    ata[i, j] += row[i] * row[j];
                }
            }
        }

        private static bool TrySolveLinearSystem(double[,] a, double[] b, out double[] x)
        {
            x = new double[6];
            var aug = new double[6, 7];

            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    aug[i, j] = a[i, j];
                }

                aug[i, 6] = b[i];
            }

            for (int col = 0; col < 6; col++)
            {
                int pivot = col;
                double maxAbs = Math.Abs(aug[pivot, col]);
                for (int row = col + 1; row < 6; row++)
                {
                    double abs = Math.Abs(aug[row, col]);
                    if (abs > maxAbs)
                    {
                        maxAbs = abs;
                        pivot = row;
                    }
                }

                if (maxAbs < 1e-10)
                {
                    return false;
                }

                if (pivot != col)
                {
                    for (int k = col; k <= 6; k++)
                    {
                        (aug[col, k], aug[pivot, k]) = (aug[pivot, k], aug[col, k]);
                    }
                }

                double div = aug[col, col];
                for (int k = col; k <= 6; k++)
                {
                    aug[col, k] /= div;
                }

                for (int row = 0; row < 6; row++)
                {
                    if (row == col)
                    {
                        continue;
                    }

                    double factor = aug[row, col];
                    if (Math.Abs(factor) < 1e-12)
                    {
                        continue;
                    }

                    for (int k = col; k <= 6; k++)
                    {
                        aug[row, k] -= factor * aug[col, k];
                    }
                }
            }

            for (int i = 0; i < 6; i++)
            {
                x[i] = aug[i, 6];
            }

            return true;
        }

        private static Point2d ComputeCentroid(IReadOnlyList<Point2d> points)
        {
            if (points.Count == 0)
            {
                return new Point2d(0, 0);
            }

            double sumX = 0;
            double sumY = 0;
            for (int i = 0; i < points.Count; i++)
            {
                sumX += points[i].X;
                sumY += points[i].Y;
            }

            return new Point2d(sumX / points.Count, sumY / points.Count);
        }

        private static double EstimateInitialMatchDistance(IReadOnlyList<Point2d> recipePoints)
        {
            if (recipePoints.Count < 2)
            {
                return 1.0;
            }

            double totalNearest = 0;
            int count = 0;
            for (int i = 0; i < recipePoints.Count; i++)
            {
                double nearest = double.MaxValue;
                for (int j = 0; j < recipePoints.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    double dist = GetDistance((recipePoints[i].X, recipePoints[i].Y), (recipePoints[j].X, recipePoints[j].Y));
                    if (dist < nearest)
                    {
                        nearest = dist;
                    }
                }

                if (nearest < double.MaxValue)
                {
                    totalNearest += nearest;
                    count++;
                }
            }

            if (count == 0)
            {
                return 1.0;
            }

            var avgNearest = totalNearest / count;
            return Math.Clamp(avgNearest * 0.7, 0.2, 2.0);
        }

        private static double GetDistance(Point2d a, Point2d b)
        {
            return GetDistance((a.X, a.Y), (b.X, b.Y));
        }

        private static double GetDistance((double X, double Y) a, (double X, double Y) b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private sealed class PointCluster
        {
            private int _count;
            private double _sumX;
            private double _sumY;

            public PointCluster(Point2d first)
            {
                _count = 1;
                _sumX = first.X;
                _sumY = first.Y;
            }

            public Point2d Center => new(_sumX / _count, _sumY / _count);

            public void Add(Point2d point)
            {
                _count++;
                _sumX += point.X;
                _sumY += point.Y;
            }
        }

        private readonly record struct PointPair(Point2d Source, Point2d Target);
 
         private async Task MoveToScanPointAsync(ScanShotPoint shot, CancellationToken cancellationToken)
         {
             if (_motionService is null)
             {
                 return;
             }

             var xVelocity = _motionService.XAxis.Velocity > 0 ? _motionService.XAxis.Velocity : 100;
             var yVelocity = _motionService.YAxis.Velocity > 0 ? _motionService.YAxis.Velocity : 100;

             await _motionService.MoveXAsync(shot.X, xVelocity, true, cancellationToken).ConfigureAwait(false);
             await _motionService.MoveYAsync(shot.Y, yVelocity, true, cancellationToken).ConfigureAwait(false);
         }

         private async Task<Mat?> CaptureFrameAsync(CancellationToken cancellationToken)
         {
             if (_camera is null)
             {
                 return null;
             }

             cancellationToken.ThrowIfCancellationRequested();
             var args = await _camera.GrabAsync().ConfigureAwait(false);
             return ConvertToMat(args);
         }

         private static Mat? ConvertToMat(CameraArgs? args)
         {
             if (args?.Data is null || args.Width <= 0 || args.Height <= 0)
             {
                 return null;
             }

             return args.Format switch
             {
                 HAL.PixelFormat.Mono8 => CreateMatFromRaw(args, MatType.CV_8UC1, args.Stride > 0 ? args.Stride : args.Width),
                 HAL.PixelFormat.BGR8 => CreateMatFromRaw(args, MatType.CV_8UC3, args.Stride > 0 ? args.Stride : args.Width * 3),
                 HAL.PixelFormat.RGB8 => ConvertRgbToBgr(args),
                 HAL.PixelFormat.BGRA8 => ConvertBgraToBgr(args),
                 HAL.PixelFormat.RGBA8 => ConvertRgbaToBgr(args),
                 _ => CreateMatFromRaw(args, MatType.CV_8UC3, args.Stride > 0 ? args.Stride : args.Width * 3)
             };
         }

         private static Mat CreateMatFromRaw(CameraArgs args, MatType matType, int stride)
         {
             var handle = GCHandle.Alloc(args.Data!, GCHandleType.Pinned);
             try
             {
                 using var view = Mat.FromPixelData(args.Height, args.Width, matType, handle.AddrOfPinnedObject(), stride);
                 return view.Clone();
             }
             finally
             {
                 handle.Free();
             }
         }

         private static Mat ConvertRgbToBgr(CameraArgs args)
         {
             using var rgb = CreateMatFromRaw(args, MatType.CV_8UC3, args.Stride > 0 ? args.Stride : args.Width * 3);
             var bgr = new Mat();
             Cv2.CvtColor(rgb, bgr, ColorConversionCodes.RGB2BGR);
             return bgr;
         }

         private static Mat ConvertBgraToBgr(CameraArgs args)
         {
             using var bgra = CreateMatFromRaw(args, MatType.CV_8UC4, args.Stride > 0 ? args.Stride : args.Width * 4);
             var bgr = new Mat();
             Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
             return bgr;
         }

         private static Mat ConvertRgbaToBgr(CameraArgs args)
         {
             using var rgba = CreateMatFromRaw(args, MatType.CV_8UC4, args.Stride > 0 ? args.Stride : args.Width * 4);
             var bgr = new Mat();
             Cv2.CvtColor(rgba, bgr, ColorConversionCodes.RGBA2BGR);
             return bgr;
         }

         private void ResetScanRunState()
         {
             foreach (var cell in PreviewGridCells)
             {
                 cell.IsScanned = false;
             }

             foreach (var tile in _capturedTileMats.Values)
             {
                 tile.Dispose();
             }

             _capturedTileMats.Clear();
             _debugRenderIndex = 0;
             Params.PhotoIndex = 0;
             Params.ProgressValue = 0;
             RenderStitchedPreview();
         }

         private void LoadDefaultsFromConfig(AppConfig? config)
         {
             if (config is null)
             {
                 return;
             }

             var camera = config.Camera;
             var fovXmm = (camera.PixelSizeX * camera.FovWidth) / 1000.0;
             var fovYmm = (camera.PixelSizeY * camera.FovHeight) / 1000.0;
             var estimatedFov = Math.Max(0.1, Math.Min(fovXmm, fovYmm));

             Params.WorkpieceType = camera.CameraType;
             Params.FovSize = Math.Round(estimatedFov, 3);
             Params.ScanExpand = Math.Round(estimatedFov, 3);
             Params.WorkpieceDiameter = 42.7;
             Params.SettleTime = Math.Max(1, config.HomeSearchSpeed / 10);
             Params.UseBrightFieldDetector = config.ScanUseBrightFieldDetector;
             Params.DetectMinArea = config.ScanDetectMinArea;
             Params.DetectMaxArea = config.ScanDetectMaxArea;
             Params.DetectThreshold = config.ScanDetectThreshold;
             Params.DetectCircularity = config.ScanDetectCircularity;
             Params.DetectMorphologySize = config.ScanDetectMorphologySize;
             Params.DeduplicateToleranceMm = config.ScanDeduplicateToleranceMm;
         }
 
         private void NormalizeInputs()
         {
             Params.WorkpieceDiameter = Math.Max(1, Params.WorkpieceDiameter);
             Params.FovSize = Math.Max(0.1, Params.FovSize);

             var clampedOverlapX = Math.Clamp(Params.OverlapXPercent, 5, 20);
             var clampedOverlapY = Math.Clamp(Params.OverlapYPercent, 5, 20);
             Params.OverlapXPercent = clampedOverlapX;
             Params.OverlapYPercent = clampedOverlapY;

             var minExpand = Params.FovSize;
             var maxExpand = Params.FovSize * 2;
             Params.ScanExpand = Math.Clamp(Params.ScanExpand, minExpand, maxExpand);
             Params.SettleTime = Math.Max(1, Params.SettleTime);

            Params.DetectMinArea = Math.Max(1, Params.DetectMinArea);
            Params.DetectMaxArea = Math.Max(Params.DetectMinArea, Params.DetectMaxArea);
            Params.DetectThreshold = Math.Clamp(Params.DetectThreshold, 0, 255);
            Params.DetectCircularity = Math.Clamp(Params.DetectCircularity, 0.01, 1.0);
            Params.DetectMorphologySize = Math.Max(3, Params.DetectMorphologySize);
            Params.DeduplicateToleranceMm = Math.Max(0.001, Params.DeduplicateToleranceMm);
         }

         private void BuildScanPlan()
         {
             _scanPoints.Clear();
             PreviewGridCells.Clear();

             var scanRadius = (Params.WorkpieceDiameter / 2.0) + Params.ScanExpand;
             var rangeSize = scanRadius * 2;
             var fov = Params.FovSize;
             var stepX = fov * (1 - (Params.OverlapXPercent / 100.0));
             var stepY = fov * (1 - (Params.OverlapYPercent / 100.0));
             stepX = Math.Max(0.01, stepX);
             stepY = Math.Max(0.01, stepY);

             var xPositions = BuildSymmetricAxisPositions(scanRadius, stepX);
             var yPositions = BuildSymmetricAxisPositions(scanRadius, stepY)
                 .OrderByDescending(v => v)
                 .ToList();

             Params.ColumnCount = xPositions.Count;
             Params.RowCount = yPositions.Count;
             Params.ActualOverlapXPercent = Math.Round((1 - (stepX / fov)) * 100, 2);
             Params.ActualOverlapYPercent = Math.Round((1 - (stepY / fov)) * 100, 2);

             var scale = PreviewCanvasSizeValue / rangeSize;
             var rowCenters = yPositions
                 .Select((y, rowIndex) => new { rowIndex, y })
                 .ToList();

             int order = 0;
             foreach (var row in rowCenters)
             {
                 var colSequence = row.rowIndex % 2 == 0
                     ? Enumerable.Range(0, xPositions.Count)
                     : Enumerable.Range(0, xPositions.Count).Reverse();

                 foreach (var colIndex in colSequence)
                 {
                     var x = xPositions[colIndex];
                     var y = row.y;
                     var distance = Math.Sqrt((x * x) + (y * y));
                     if (distance > scanRadius)
                     {
                         continue;
                     }

                     var left = ((x - (fov / 2)) + (rangeSize / 2)) * scale;
                     var top = ((rangeSize / 2) - (y + (fov / 2))) * scale;
                     var size = fov * scale;

                     _scanPoints.Add(new ScanShotPoint(x, y, row.rowIndex, colIndex, order));
                     PreviewGridCells.Add(new ScanGridCellVisual
                     {
                         ShotIndex = order,
                         OrderIndex = order + 1,
                         Row = row.rowIndex + 1,
                         Column = colIndex + 1,
                         Left = left,
                         Top = top,
                         Width = size,
                         Height = size,
                         IsScanned = false
                     });

                     order++;
                 }
             }

             Params.TotalShots = order;
         }

         private static List<double> BuildSymmetricAxisPositions(double radius, double step)
         {
             var halfCount = Math.Max(0, (int)Math.Ceiling(radius / step));
             var values = new List<double>(halfCount * 2 + 1);

             for (int i = -halfCount; i <= halfCount; i++)
             {
                 values.Add(i * step);
             }

             return values;
         }

         private async Task RenderStitchedPreviewOnUiThreadAsync()
         {
             var dispatcher = Application.Current?.Dispatcher;
             if (dispatcher is null || dispatcher.CheckAccess())
             {
                 RenderStitchedPreview();
                 return;
             }

             await dispatcher.InvokeAsync(RenderStitchedPreview);
         }

         private void RenderStitchedPreview()
         {
             var size = (int)PreviewCanvasSizeValue;
             using var stitched = new Mat(size, size, MatType.CV_8UC3, new Scalar(36, 36, 36));

             foreach (var cell in PreviewGridCells)
             {
                 var x = Math.Max(0, (int)Math.Round(cell.Left));
                 var y = Math.Max(0, (int)Math.Round(cell.Top));
                 var w = Math.Min(size - x, Math.Max(1, (int)Math.Round(cell.Width)));
                 var h = Math.Min(size - y, Math.Max(1, (int)Math.Round(cell.Height)));
                 if (w <= 0 || h <= 0)
                 {
                     continue;
                 }

                 var roi = new OpenCvSharp.Rect(x, y, w, h);

                 if (_capturedTileMats.TryGetValue(cell.ShotIndex, out var tile))
                 {
                     using var resized = new Mat();
                     Cv2.Resize(tile, resized, new OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Area);
                     using var targetRoi = new Mat(stitched, roi);
                     resized.CopyTo(targetRoi);
                 }
                 else
                 {
                     Cv2.Rectangle(stitched, roi, new Scalar(95, 95, 95), -1);
                 }
             }

            SaveDebugStitchedMatIfNeeded(stitched);
            var oldMat = StitchedPreviewMat;
            StitchedPreviewMat = stitched.Clone();
            oldMat?.Dispose();
        }

         private void SaveDebugStitchedMatIfNeeded(Mat stitched)
         {
             if (_configService?.CurrentConfig.IsDebugMode != true)
             {
                 return;
             }

             Directory.CreateDirectory(_debugImageDirectory);
             var fileName = $"stitch_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{_debugRenderIndex++:D4}.png";
             var filePath = Path.Combine(_debugImageDirectory, fileName);
             Cv2.ImWrite(filePath, stitched);
         }

         private readonly record struct ScanShotPoint(double X, double Y, int Row, int Column, int ShotIndex);
    }
}


