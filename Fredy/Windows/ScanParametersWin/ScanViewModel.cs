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

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class ScanViewModel : ObservableObject
    {
        private const double PreviewCanvasSizeValue = 620;
        private readonly ICamera? _camera;
        private readonly IMotionService? _motionService;
        private readonly ConfigService? _configService;
        private readonly List<ScanShotPoint> _scanPoints = new();
        private readonly Dictionary<int, Mat> _capturedTileMats = new();
        private readonly string _debugImageDirectory = Path.Combine(AppContext.BaseDirectory, "images");
        private CancellationTokenSource? _scanCancellationTokenSource;
        private int _debugRenderIndex;

        [ObservableProperty] private ScanParameters _params = new();
        [ObservableProperty] private ImageSource? _stitchedPreviewImage;

        public ScanViewModel()
            : this(null, null, null)
        {
        }

        public ScanViewModel(ConfigService? configService)
            : this(null, null, configService)
        {
        }

        public ScanViewModel(ICamera? camera, IMotionService? motionService, ConfigService? configService)
        {
            _camera = camera;
            _motionService = motionService;
            _configService = configService;
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
                        : $"扫描并拼接中 {i + 1}/{_scanPoints.Count}，已拼接 {_capturedTileMats.Count} 张";
                }

                Params.ScanStatus = "扫描完成，已完成实时拼接。";
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

        private static BitmapSource ConvertMatToBitmapSource(Mat mat)
        {
            Mat bgrMat = mat;
            if (mat.Type() == MatType.CV_8UC1)
            {
                bgrMat = new Mat();
                Cv2.CvtColor(mat, bgrMat, ColorConversionCodes.GRAY2BGR);
            }

            try
            {
                var stride = (int)bgrMat.Step();
                var bufferSize = stride * bgrMat.Rows;
                var bitmap = BitmapSource.Create(
                    bgrMat.Width,
                    bgrMat.Height,
                    96,
                    96,
                    PixelFormats.Bgr24,
                    null,
                    bgrMat.Data,
                    bufferSize,
                    stride);

                bitmap.Freeze();
                return bitmap;
            }
            finally
            {
                if (!ReferenceEquals(bgrMat, mat))
                {
                    bgrMat.Dispose();
                }
            }
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
            StitchedPreviewImage = ConvertMatToBitmapSource(stitched);
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