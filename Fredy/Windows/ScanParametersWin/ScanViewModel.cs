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
using Serilog;

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
        private readonly ILogger _logger;
        private readonly CircleDetector _circleDetector = new();
        private readonly List<ScanShotPoint> _scanPoints = new();
        private readonly Dictionary<int, Mat> _capturedTileMats = new();
        private readonly List<Point2d> _detectedHoleCandidates = new();
        private readonly List<Point2d> _detectedHoleCoordinates = new();
        private readonly string _debugImageDirectory = Path.Combine(AppContext.BaseDirectory, "images");
        private CancellationTokenSource? _scanCancellationTokenSource;
        private int _debugRenderIndex;
        private bool _hasPendingAlignmentTransform;
        private int _lastMatchedPointCount;

        private IReadOnlyDictionary<int, (double X, double Y)> _pendingMatchedPoints = new Dictionary<int, (double X, double Y)>();

        private Mat? _incrementalStitchedMat;
        private int _currentStitchSize;
        private double _currentStitchScaleRatio;

        [ObservableProperty] private ScanParameters _params = new();
        [ObservableProperty] private OpenCvSharp.Mat? _stitchedPreviewMat;
        [ObservableProperty] private bool _showGridOverlay = true;

        public ScanViewModel()
            : this(null, null, null, null, null, null)
        {
        }

        public ScanViewModel(ConfigService? configService)
            : this(null, null, configService, null, null, null)
        {
        }

        public ScanViewModel(ICamera? camera, IMotionService? motionService, ConfigService? configService, ISecondPassAlignmentContext? secondPassAlignmentContext, RecipeService? recipeService, ILogger? logger)
         {
             _camera = camera;
             _motionService = motionService;
             _configService = configService;
             _secondPassAlignmentContext = secondPassAlignmentContext;
              _recipeService = recipeService;
              _logger = (logger ?? Log.Logger).ForContext<ScanViewModel>();
             PreviewGridCells = new ObservableCollection<ScanGridCellVisual>();
             LoadDefaultsFromConfig(_configService?.CurrentConfig);
             Params.ScanStatus = "等待计算...";
         }

        public ObservableCollection<ScanGridCellVisual> PreviewGridCells { get; }

        public ObservableCollection<DetectedHoleInfo> DetectionResults { get; } = new();

        [ObservableProperty] private bool _drawDetectedCircles = true;

        [ObservableProperty] private OpenCvSharp.Mat? _latestDetectionMat;

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
            _lastMatchedPointCount = 0;

            _logger.Information("开始扫描流程，当前扫描点数：{ScanPointCount}", _scanPoints.Count);

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
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                DetectionResults.Clear();
                LatestDetectionMat?.Dispose();
                LatestDetectionMat = null;
            });

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
                        var tileHoles = DetectTileHoleCoordinates(captured, shot, i + 1);
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

                    if (captured is not null)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => UpdateIncrementalStitchedCanvas(shot.ShotIndex, captured));
                    }

                    Params.ScanStatus = captured is null
                        ? $"扫描中 {i + 1}/{_scanPoints.Count}（当前帧为空）"
                        : $"扫描并拼接中 {i + 1}/{_scanPoints.Count}，已拼接 {_capturedTileMats.Count} 张，检测候选孔 {_detectedHoleCandidates.Count} 个";
                }

                Params.ScanStatus = "扫描完成，已完成实时拼接。";
                _logger.Information("扫描完成，已拍摄 {CapturedTileCount} 张，检测候选孔 {HoleCount} 个", _capturedTileMats.Count, _detectedHoleCandidates.Count);
                FinalizeSecondPassAlignmentPreparation();
            }
            catch (OperationCanceledException)
            {
                Params.ScanStatus = "扫描已停止。";
                _logger.Information("扫描被取消");
            }
            catch (Exception ex)
            {
                Params.ScanStatus = $"扫描失败：{ex.Message}";
                _logger.Error(ex, "扫描流程失败");
            }
        }

        [RelayCommand]
        private void StopScan()
        {
            _scanCancellationTokenSource?.Cancel();
            Params.ScanStatus = "正在停止扫描...";
            _logger.Information("请求停止扫描");
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
                config.CircleIsDarkTarget = Params.DetectIsDarkHole;
                config.CircleMinRadius = Math.Max(1, Params.DetectMinRadius);
                config.CircleMaxRadius = Math.Max(config.CircleMinRadius, Params.DetectMaxRadius);
                config.CircleParam1 = Params.DetectParam1;
                config.CircleParam2 = Params.DetectParam2;

                config.ScanDeduplicateToleranceMm = Math.Max(0.001, Params.DeduplicateToleranceMm);

                _configService.SaveWithArchive(config);

                Params.DetectIsDarkHole = config.CircleIsDarkTarget;
                Params.DetectMinRadius = config.CircleMinRadius;
                Params.DetectMaxRadius = config.CircleMaxRadius;
                Params.DetectParam1 = config.CircleParam1;
                Params.DetectParam2 = config.CircleParam2;
                Params.DeduplicateToleranceMm = config.ScanDeduplicateToleranceMm;

                Params.ScanStatus = "检测参数已保存到配置。";
                _logger.Information("扫描检测参数已保存");
            }
            catch (Exception ex)
            {
                Params.ScanStatus = $"保存检测参数失败：{ex.Message}";
                _logger.Error(ex, "保存扫描检测参数失败");
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
                _logger.Information("{SourceName}检测完成，识别圆孔 {CircleCount} 个", sourceName, detection.Circles.Count);
            }
            catch (Exception ex)
            {
                Params.ScanStatus = $"{sourceName}检测失败：{ex.Message}";
                _logger.Error(ex, "{SourceName}检测失败", sourceName);
            }
        }

        [RelayCommand]
        private async Task ApplySecondPassAlignmentAsync()
        {
            if (_secondPassAlignmentContext is null)
            {
                Params.ScanStatus = "坐标校准上下文未初始化，无法应用二道矩阵。";
                return;
            }

            if (_capturedTileMats.Count == 0)
            {
                Params.ScanStatus = "暂无已拍摄的扫描图像，请先执行分区扫描。";
                return;
            }

            Params.ScanStatus = "正在重新识别拼接图像并进行二道坐标校准...";

            try
            {
                _hasPendingAlignmentTransform = false;
                _lastMatchedPointCount = 0;

                _logger.Information("开始应用二道坐标校准，已拍摄图像数：{CapturedTileCount}", _capturedTileMats.Count);

                _detectedHoleCandidates.Clear();
                _detectedHoleCoordinates.Clear();

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DetectionResults.Clear();
                    LatestDetectionMat?.Dispose();
                    LatestDetectionMat = null;
                });

                var sortedTiles = _capturedTileMats.OrderBy(kvp => kvp.Key).ToList();
                int processedCount = 0;

                foreach (var kvp in sortedTiles)
                {
                    int shotIndex = kvp.Key;
                    var tile = kvp.Value;

                    var shot = _scanPoints.FirstOrDefault(s => s.ShotIndex == shotIndex);
                    if (shot != null)
                    {
                        var tileHoles = await Task.Run(() => DetectTileHoleCoordinates(tile, shot, processedCount + 1)).ConfigureAwait(true);
                        if (tileHoles.Count > 0)
                        {
                            _detectedHoleCandidates.AddRange(tileHoles);
                        }
                    }
                    processedCount++;
                    Params.ScanStatus = $"重新识别中 {processedCount}/{sortedTiles.Count}，检测候选孔 {_detectedHoleCandidates.Count} 个";
                }

                FinalizeSecondPassAlignmentPreparation();

                if (_hasPendingAlignmentTransform)
                {
                    _secondPassAlignmentContext.SetMatchedPoints(_pendingMatchedPoints);
                    Params.ScanStatus = $"二道坐标校准成功，已写入匹配结果（匹配点 {_lastMatchedPointCount}），可返回主界面继续二道冲孔。";
                    _logger.Information("二道坐标校准成功，匹配点数：{MatchedPointCount}", _lastMatchedPointCount);
                }
            }
            catch (Exception ex)
            {
                Params.ScanStatus = $"应用二道校准失败：{ex.Message}";
                _logger.Error(ex, "应用二道坐标校准失败");
            }
        }

        private List<Point2d> DetectTileHoleCoordinates(Mat tile, ScanShotPoint shot, int photoIndex)
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
            _logger.Information("扫描图块检测开始，序号 {PhotoIndex}，图像尺寸 {Width}x{Height}，通道数 {Channels}，像素格式 {Type}",
                photoIndex,
                detectorInput.Width,
                detectorInput.Height,
                detectorInput.Channels(),
                detectorInput.Type());
            using var detection = DetectCircles(detectorInput);
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var oldMat = LatestDetectionMat;
                if (DrawDetectedCircles)
                {
                    var overlay = tile.Clone();
                    foreach (var circle in detection.Circles)
                    {
                        var cx = (int)circle.Center.X;
                        var cy = (int)circle.Center.Y;
                        var r = (int)circle.Radius;
                        
                        // 绿色圆形轮廓
                        OpenCvSharp.Cv2.Circle(overlay, new OpenCvSharp.Point(cx, cy), r, OpenCvSharp.Scalar.LimeGreen, 2);
                        // 圆心红色十字
                        int crossLen = 6;
                        OpenCvSharp.Cv2.Line(overlay, new OpenCvSharp.Point(cx - crossLen, cy), new OpenCvSharp.Point(cx + crossLen, cy), OpenCvSharp.Scalar.Red, 2);
                        OpenCvSharp.Cv2.Line(overlay, new OpenCvSharp.Point(cx, cy - crossLen), new OpenCvSharp.Point(cx, cy + crossLen), OpenCvSharp.Scalar.Red, 2);
                    }
                    LatestDetectionMat = overlay;
                }
                else
                {
                    LatestDetectionMat = tile.Clone();
                }
                oldMat?.Dispose();
            });

            if (detection.Circles.Count == 0)
            {
                return result;
            }

            double centerX = tile.Width / 2.0;
            double centerY = tile.Height / 2.0;

            var newHoles = new List<DetectedHoleInfo>();

            foreach (var circle in detection.Circles)
            {
                double dxPixel = circle.Center.X - centerX;
                double dyPixel = circle.Center.Y - centerY;

                // 图像Y向下为正，机台Y按数学坐标系向上为正
                double physicalX = shot.X + (dxPixel * pixelSizeXmm);
                double physicalY = shot.Y - (dyPixel * pixelSizeYmm);
                result.Add(new Point2d(physicalX, physicalY));
                
                double pixelDiam = circle.Radius * 2;
                double physDiam = pixelDiam * ((pixelSizeXmm + pixelSizeYmm) / 2.0);

                newHoles.Add(new DetectedHoleInfo 
                {
                    ImageIndex = photoIndex,
                    PixelX = Math.Round(circle.Center.X, 2),
                    PixelY = Math.Round(circle.Center.Y, 2),
                    PixelSize = Math.Round(pixelDiam, 2),
                    PhysicalX = Math.Round(physicalX, 3),
                    PhysicalY = Math.Round(physicalY, 3),
                    PhysicalSize = Math.Round(physDiam, 3)
                });
            }

            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
            {
                foreach (var h in newHoles)
                {
                    DetectionResults.Add(h);
                }
            });

            return result;
        }

        private DetectionResult DetectCircles(Mat input)
        {
            var isDarkHole = Params.DetectIsDarkHole;
            int minRadius = Math.Max(1, (int)Params.DetectMinRadius);
            int maxRadius = Math.Max(minRadius, (int)Params.DetectMaxRadius);
            int param1 = Math.Max(1, (int)Params.DetectParam1);
            int param2 = Math.Max(1, (int)Params.DetectParam2);

            return _circleDetector.ProcessMetalHoles(input, isDarkHole, minRadius, maxRadius, param1, param2);
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
                Params.ScanStatus = $"扫描完成，检测候选 {_detectedHoleCandidates.Count} 个、去重后 {_detectedHoleCoordinates.Count} 个，但未找到当前配方，无法计算坐标匹配。";
                return;
            }

            if (!TryMatchDetectedHolesToRecipe(recipePoints, _detectedHoleCoordinates, out var matchedIndexMap))
            {
                Params.ScanStatus = $"扫描完成，检测候选 {_detectedHoleCandidates.Count} 个、去重后 {_detectedHoleCoordinates.Count} 个；与配方[{recipeName}]自动匹配失败。";
                return;
            }

            _pendingMatchedPoints = matchedIndexMap;
            _hasPendingAlignmentTransform = true;
            _lastMatchedPointCount = matchedIndexMap.Count;
            Params.ScanStatus = $"扫描完成，候选 {_detectedHoleCandidates.Count} 个，去重后 {_detectedHoleCoordinates.Count} 个；与配方[{recipeName}]匹配 {matchedIndexMap.Count} 个。";
        }

        private static bool TryMatchDetectedHolesToRecipe(
            IReadOnlyList<Point2d> recipePoints,
            IReadOnlyList<Point2d> detectedPoints,
            out IReadOnlyDictionary<int, (double X, double Y)> matchedIndexMap)
        {
            var map = new Dictionary<int, (double X, double Y)>();
            matchedIndexMap = map;

            if (recipePoints.Count == 0 || detectedPoints.Count == 0)
            {
                return false;
            }

            // 搜索半径，如果孔位相较于理论设计偏离太多则认为不是这个孔
            double maxMatchDistanceMm = 10.0;
            var usedDetectedIndices = new HashSet<int>();

            for (int rIndex = 0; rIndex < recipePoints.Count; rIndex++)
            {
                var recipePoint = recipePoints[rIndex];
                int bestMatchIndex = -1;
                double bestDistance = double.MaxValue;

                for (int dIndex = 0; dIndex < detectedPoints.Count; dIndex++)
                {
                    if (usedDetectedIndices.Contains(dIndex))
                    {
                        continue;
                    }

                    var detectedPoint = detectedPoints[dIndex];
                    double dist = GetDistance(recipePoint, detectedPoint);
                    if (dist < bestDistance)
                    {
                        bestDistance = dist;
                        bestMatchIndex = dIndex;
                    }
                }

                if (bestMatchIndex >= 0 && bestDistance <= maxMatchDistanceMm)
                {
                    usedDetectedIndices.Add(bestMatchIndex);
                    map[rIndex] = (detectedPoints[bestMatchIndex].X, detectedPoints[bestMatchIndex].Y);
                }
            }

            return map.Count > 0;
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

        private readonly record struct PointPair(Point2d Source, Point2d Target, int SourceIndex = -1);
 
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

             Params.WorkpieceType = _recipeService?.CurrentRecipe?.RecipeName ?? camera.CameraType;
             Params.FovSize = Math.Round(estimatedFov, 3);
             Params.ScanExpand = Math.Round(estimatedFov, 3);
             
             // 尝试从当前配方中提取实际工件直径（如果配方有设置的话，默认取配方外圈半径 * 2），否则采用默认的42.7
             double diameter = 42.7;
             if (_recipeService?.CurrentRecipe?.PunchParameters is { Radius: > 0 } punchParams)
             {
                 diameter = punchParams.Radius * 2.0;
             }
             Params.WorkpieceDiameter = diameter;

             Params.SettleTime = Math.Max(1, config.HomeSearchSpeed / 10);
            Params.DetectMinRadius = config.CircleMinRadius;
            Params.DetectMaxRadius = config.CircleMaxRadius;
            Params.DetectParam1 = config.CircleParam1;
            Params.DetectParam2 = config.CircleParam2;
            Params.DetectIsDarkHole = config.CircleIsDarkTarget;
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

            Params.DetectMinRadius = Math.Max(1, Params.DetectMinRadius);
            Params.DetectMaxRadius = Math.Max(Params.DetectMinRadius, Params.DetectMaxRadius);
            Params.DetectParam1 = Math.Max(1, Params.DetectParam1);
            Params.DetectParam2 = Math.Max(1, Params.DetectParam2);
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

             FilterScanPlanByRecipePoints();
             Params.TotalShots = _scanPoints.Count;
         }

         private void FilterScanPlanByRecipePoints()
         {
             try
             {
                 if (!TryGetRecipePoints(out var recipePoints, out _))
                 {
                     return;
                 }

                 var fov = Params.FovSize;
                 var halfFov = fov / 2.0;
                 var margin = Math.Max(2.0, Params.ScanExpand); // 根据扩展范围给定一个足够安全的容差

                 var filteredPoints = new List<ScanShotPoint>();
                 var filteredCells = new List<ScanGridCellVisual>();

                 int newOrder = 0;
                 for (int i = 0; i < _scanPoints.Count; i++)
                 {
                     var pt = _scanPoints[i];
                     var cell = PreviewGridCells[i];

                     bool hasHole = false;
                     foreach (var rp in recipePoints)
                     {
                         if (Math.Abs(rp.X - pt.X) <= halfFov + margin &&
                             Math.Abs(rp.Y - pt.Y) <= halfFov + margin)
                         {
                             hasHole = true;
                             break;
                         }
                     }

                     if (hasHole)
                     {
                         filteredPoints.Add(pt with { ShotIndex = newOrder });
                         
                         cell.ShotIndex = newOrder;
                         cell.OrderIndex = newOrder + 1;
                         filteredCells.Add(cell);

                         newOrder++;
                     }
                 }

                 // 如果过滤结果为0，说明当前扫描区设置得太小(如默认直径42.7)未覆盖到实际的图纸孔位(如 Virtual_Recipe_01 半径达62)
                 // 为了避免界面网格全部消失引发"异常"观感，保留原全部网格。
                 if (filteredPoints.Count > 0 && filteredPoints.Count < _scanPoints.Count)
                 {
                     _scanPoints.Clear();
                     _scanPoints.AddRange(filteredPoints);

                     PreviewGridCells.Clear();
                     foreach (var cell in filteredCells)
                     {
                         PreviewGridCells.Add(cell);
                     }
                 }
             }
             catch
             {
                 // 忽略过滤产生的异常，退回至扫描全部计算出网格
             }
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
             var scanRadius = (Params.WorkpieceDiameter / 2.0) + Params.ScanExpand;
             var rangeSize = scanRadius * 2;
             
             double pixelSizeXmm = (_configService?.CurrentConfig?.Camera.PixelSizeX ?? 3.45) / 1000.0;
             double requiredPixels = rangeSize / pixelSizeXmm;
             _currentStitchSize = (int)Math.Min(3000, Math.Max(PreviewCanvasSizeValue, requiredPixels));
             _currentStitchScaleRatio = _currentStitchSize / PreviewCanvasSizeValue;

             _incrementalStitchedMat?.Dispose();
             _incrementalStitchedMat = new Mat(_currentStitchSize, _currentStitchSize, MatType.CV_8UC3, new Scalar(36, 36, 36));

             foreach (var cell in PreviewGridCells)
             {
                 var x = Math.Max(0, (int)Math.Round(cell.Left * _currentStitchScaleRatio));
                 var y = Math.Max(0, (int)Math.Round(cell.Top * _currentStitchScaleRatio));
                 var w = Math.Min(_currentStitchSize - x, Math.Max(1, (int)Math.Round(cell.Width * _currentStitchScaleRatio)));
                 var h = Math.Min(_currentStitchSize - y, Math.Max(1, (int)Math.Round(cell.Height * _currentStitchScaleRatio)));
                 if (w <= 0 || h <= 0)
                 {
                     continue;
                 }

                 var roi = new OpenCvSharp.Rect(x, y, w, h);

                 if (_capturedTileMats.TryGetValue(cell.ShotIndex, out var tile))
                 {
                     using var resized = new Mat();
                     Cv2.Resize(tile, resized, new OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Area);
                     using var targetRoi = new Mat(_incrementalStitchedMat, roi);
                     resized.CopyTo(targetRoi);
                 }
                 else
                 {
                     Cv2.Rectangle(_incrementalStitchedMat, roi, new Scalar(95, 95, 95), -1);
                 }
             }

             SaveDebugStitchedMatIfNeeded(_incrementalStitchedMat);
             
             var oldMat = StitchedPreviewMat;
             StitchedPreviewMat = _incrementalStitchedMat.Clone();
             oldMat?.Dispose();
         }

         private void UpdateIncrementalStitchedCanvas(int shotIndex, Mat tile)
         {
             if (_incrementalStitchedMat == null || _incrementalStitchedMat.IsDisposed) return;
             if (tile == null || tile.Empty()) return;

             var cell = PreviewGridCells.FirstOrDefault(c => c.ShotIndex == shotIndex);
             if (cell == null) return;

             var x = Math.Max(0, (int)Math.Round(cell.Left * _currentStitchScaleRatio));
             var y = Math.Max(0, (int)Math.Round(cell.Top * _currentStitchScaleRatio));
             var w = Math.Min(_currentStitchSize - x, Math.Max(1, (int)Math.Round(cell.Width * _currentStitchScaleRatio)));
             var h = Math.Min(_currentStitchSize - y, Math.Max(1, (int)Math.Round(cell.Height * _currentStitchScaleRatio)));
             
             if (w > 0 && h > 0)
             {
                 var roi = new OpenCvSharp.Rect(x, y, w, h);
                 using var resized = new Mat();
                 Cv2.Resize(tile, resized, new OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Area);
                 using var targetRoi = new Mat(_incrementalStitchedMat, roi);
                 resized.CopyTo(targetRoi);

                 var oldMat = StitchedPreviewMat;
                 StitchedPreviewMat = _incrementalStitchedMat.Clone();
                 oldMat?.Dispose();
             }
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


