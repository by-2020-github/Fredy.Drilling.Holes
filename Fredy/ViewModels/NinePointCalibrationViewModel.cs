using BLL;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HAL;
using Microsoft.Win32;
using OpenCvSharp;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fredy.Drilling.Holes.ViewModels
{
    public sealed class NinePointCalibrationResult
    {
        public double PixelSizeXUm { get; init; }
        public double PixelSizeYUm { get; init; }
    }

    public partial class NinePointCalibrationPointViewModel : ObservableObject
    {
        [ObservableProperty] private int _index;
        [ObservableProperty] private double _stageX;
        [ObservableProperty] private double _stageY;
        [ObservableProperty] private double _pixelX;
        [ObservableProperty] private double _pixelY;
        [ObservableProperty] private double _score;
    }

    public partial class NinePointCalibrationViewModel : ObservableObject
    {
        private readonly IMotionService? _motionService;
        private readonly ICamera? _camera;
        private readonly Random _random = new();
        private CancellationTokenSource? _cts;

        [ObservableProperty] private double _manualPixelSizeX;
        [ObservableProperty] private double _manualPixelSizeY;
        [ObservableProperty] private double _xStep = 1;
        [ObservableProperty] private double _yStep = 1;
        [ObservableProperty] private string _templatePath = string.Empty;
        [ObservableProperty] private double _minScore = 0.6;
        [ObservableProperty] private ImageSource? _previewImage;
        [ObservableProperty] private string _statusMessage = "等待执行九点标定";
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private double _currentX;
        [ObservableProperty] private double _currentY;
        [ObservableProperty] private double _currentZ;
        [ObservableProperty] private double _relativeMoveStep = 1;
        [ObservableProperty] private double _absoluteTargetX;
        [ObservableProperty] private double _absoluteTargetY;
        [ObservableProperty] private double _absoluteTargetZ;

        public event Action<bool?>? RequestClose;

        public ObservableCollection<NinePointCalibrationPointViewModel> Points { get; } = new();

        public NinePointCalibrationResult? CalibrationResult { get; private set; }

        public NinePointCalibrationViewModel(IMotionService? motionService, ICamera? camera)
        {
            _motionService = motionService;
            _camera = camera;

            if (_motionService is not null)
            {
                RefreshAxisPositions();
            }
        }

        [RelayCommand]
        private void BrowseTemplate()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择模板图片",
                Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp"
            };

            if (dialog.ShowDialog() == true)
            {
                TemplatePath = dialog.FileName;
            }
        }

        [RelayCommand]
        private void ApplyManual()
        {
            if (ManualPixelSizeX <= 0 || ManualPixelSizeY <= 0)
            {
                StatusMessage = "手动输入必须大于0";
                return;
            }

            CalibrationResult = new NinePointCalibrationResult
            {
                PixelSizeXUm = ManualPixelSizeX,
                PixelSizeYUm = ManualPixelSizeY
            };

            RequestClose?.Invoke(true);
        }

        [RelayCommand]
        private void RefreshPosition()
        {
            RefreshAxisPositions();
        }

        [RelayCommand]
        private async Task MoveRelativeAsync(string? axis)
        {
            if (_motionService is null)
            {
                StatusMessage = "缺少运动控制服务";
                return;
            }

            if (RelativeMoveStep <= 0)
            {
                StatusMessage = "相对移动步长必须大于0";
                return;
            }

            if (string.IsNullOrWhiteSpace(axis))
            {
                StatusMessage = "未指定移动轴";
                return;
            }

            try
            {
                switch (axis.ToUpperInvariant())
                {
                    case "X+":
                        await _motionService.MoveXAsync(CurrentX + RelativeMoveStep, Math.Max(1, _motionService.XAxis.Velocity), true);
                        break;
                    case "X-":
                        await _motionService.MoveXAsync(CurrentX - RelativeMoveStep, Math.Max(1, _motionService.XAxis.Velocity), true);
                        break;
                    case "Y+":
                        await _motionService.MoveYAsync(CurrentY + RelativeMoveStep, Math.Max(1, _motionService.YAxis.Velocity), true);
                        break;
                    case "Y-":
                        await _motionService.MoveYAsync(CurrentY - RelativeMoveStep, Math.Max(1, _motionService.YAxis.Velocity), true);
                        break;
                    case "Z+":
                        await _motionService.MoveZAsync(CurrentZ + RelativeMoveStep, Math.Max(1, _motionService.ZAxis.Velocity), true);
                        break;
                    case "Z-":
                        await _motionService.MoveZAsync(CurrentZ - RelativeMoveStep, Math.Max(1, _motionService.ZAxis.Velocity), true);
                        break;
                    default:
                        StatusMessage = $"不支持的移动轴: {axis}";
                        return;
                }

                RefreshAxisPositions();
                StatusMessage = $"相对移动完成: {axis} {RelativeMoveStep:F3} mm";
            }
            catch (Exception ex)
            {
                StatusMessage = $"相对移动失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task MoveAbsoluteAsync(string? axis)
        {
            if (_motionService is null)
            {
                StatusMessage = "缺少运动控制服务";
                return;
            }

            if (string.IsNullOrWhiteSpace(axis))
            {
                StatusMessage = "未指定移动轴";
                return;
            }

            try
            {
                switch (axis.ToUpperInvariant())
                {
                    case "X":
                        await _motionService.MoveXAsync(AbsoluteTargetX, Math.Max(1, _motionService.XAxis.Velocity), true);
                        break;
                    case "Y":
                        await _motionService.MoveYAsync(AbsoluteTargetY, Math.Max(1, _motionService.YAxis.Velocity), true);
                        break;
                    case "Z":
                        await _motionService.MoveZAsync(AbsoluteTargetZ, Math.Max(1, _motionService.ZAxis.Velocity), true);
                        break;
                    default:
                        StatusMessage = $"不支持的移动轴: {axis}";
                        return;
                }

                RefreshAxisPositions();
                StatusMessage = $"绝对移动完成: {axis}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"绝对移动失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task StartVirtualDemoAsync()
        {
            if (IsRunning)
            {
                return;
            }

            if (XStep <= 0 || YStep <= 0)
            {
                StatusMessage = "xStep/yStep 必须大于0";
                return;
            }

            IsRunning = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                var token = _cts.Token;
                Points.Clear();

                using var template = CreateVirtualTriangleTemplate(30, 30);
                var originX = 0d;
                var originY = 0d;

                int index = 1;
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        token.ThrowIfCancellationRequested();

                        var offsetX = (col - 1) * XStep;
                        var offsetY = (1 - row) * YStep;
                        var targetX = originX + offsetX;
                        var targetY = originY + offsetY;

                        using var frameMat = CreateVirtualFrameWithTemplate(template, row, col);
                        using var gray = new Mat();
                        Cv2.CvtColor(frameMat, gray, ColorConversionCodes.BGR2GRAY);
                        using var result = new Mat();
                        Cv2.MatchTemplate(gray, template, result, TemplateMatchModes.CCoeffNormed);
                        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

                        if (maxVal < MinScore)
                        {
                            StatusMessage = $"虚拟第{index}点匹配失败, score={maxVal:F3}";
                            return;
                        }

                        var centerX = maxLoc.X + (template.Width / 2.0);
                        var centerY = maxLoc.Y + (template.Height / 2.0);
                        var rect = new OpenCvSharp.Rect(maxLoc.X, maxLoc.Y, template.Width, template.Height);
                        Cv2.Rectangle(frameMat, rect, Scalar.Lime, 2);
                        Cv2.Circle(frameMat, new OpenCvSharp.Point((int)centerX, (int)centerY), 4, Scalar.Red, -1);
                        Cv2.PutText(frameMat, $"Demo P{index} S={maxVal:F3}", new OpenCvSharp.Point(Math.Max(0, maxLoc.X - 4), Math.Max(16, maxLoc.Y - 6)), HersheyFonts.HersheySimplex, 0.55, Scalar.Yellow, 2);

                        PreviewImage = ConvertMatToBitmap(frameMat);

                        Points.Add(new NinePointCalibrationPointViewModel
                        {
                            Index = index,
                            StageX = targetX,
                            StageY = targetY,
                            PixelX = centerX,
                            PixelY = centerY,
                            Score = maxVal
                        });

                        StatusMessage = $"虚拟演示已完成 {index}/9";
                        index++;

                        await Task.Delay(120, token);
                    }
                }

                if (!TryCalculatePixelSize(out var pixelSizeX, out var pixelSizeY))
                {
                    StatusMessage = "虚拟演示计算失败，像素步长无效";
                    return;
                }

                ManualPixelSizeX = pixelSizeX;
                ManualPixelSizeY = pixelSizeY;
                CalibrationResult = new NinePointCalibrationResult { PixelSizeXUm = pixelSizeX, PixelSizeYUm = pixelSizeY };
                StatusMessage = $"虚拟演示完成 X={pixelSizeX:F4}μm, Y={pixelSizeY:F4}μm";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "虚拟演示已取消";
            }
            catch (Exception ex)
            {
                StatusMessage = $"虚拟演示异常: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        [RelayCommand]
        private async Task StartFieldCalibrationAsync()
        {
            if (IsRunning)
            {
                return;
            }

            if (_camera is null || _motionService is null)
            {
                StatusMessage = "缺少相机或运动控制服务";
                return;
            }

            if (string.IsNullOrWhiteSpace(TemplatePath) || !System.IO.File.Exists(TemplatePath))
            {
                StatusMessage = "请选择有效模板图片";
                return;
            }

            if (XStep <= 0 || YStep <= 0)
            {
                StatusMessage = "xStep/yStep 必须大于0";
                return;
            }

            IsRunning = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                if (!_camera.IsConnected)
                {
                    _camera.Open();
                }

                var token = _cts.Token;
                Points.Clear();

                using var template = Cv2.ImRead(TemplatePath, ImreadModes.Grayscale);
                if (template.Empty())
                {
                    StatusMessage = "模板图片加载失败";
                    return;
                }

                var originX = await _motionService.GetXPositionAsync(token);
                var originY = await _motionService.GetYPositionAsync(token);

                int index = 1;
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        token.ThrowIfCancellationRequested();

                        var offsetX = (col - 1) * XStep;
                        var offsetY = (1 - row) * YStep;
                        var targetX = originX + offsetX;
                        var targetY = originY + offsetY;

                        await _motionService.MoveXAsync(targetX, Math.Max(1, _motionService.XAxis.Velocity), true, token);
                        await _motionService.MoveYAsync(targetY, Math.Max(1, _motionService.YAxis.Velocity), true, token);

                        await Task.Delay(80, token);

                        var frameArgs = await _camera.GrabAsync();
                        using var frameMat = ConvertToBgrMat(frameArgs);
                        if (frameMat is null || frameMat.Empty())
                        {
                            StatusMessage = $"第{index}点拍照失败";
                            return;
                        }

                        using var gray = new Mat();
                        Cv2.CvtColor(frameMat, gray, ColorConversionCodes.BGR2GRAY);
                        using var result = new Mat();
                        Cv2.MatchTemplate(gray, template, result, TemplateMatchModes.CCoeffNormed);
                        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

                        if (maxVal < MinScore)
                        {
                            StatusMessage = $"第{index}点匹配失败, score={maxVal:F3}";
                            return;
                        }

                        var centerX = maxLoc.X + (template.Width / 2.0);
                        var centerY = maxLoc.Y + (template.Height / 2.0);
                        var rect = new OpenCvSharp.Rect(maxLoc.X, maxLoc.Y, template.Width, template.Height);
                        Cv2.Rectangle(frameMat, rect, Scalar.Lime, 2);
                        Cv2.Circle(frameMat, new OpenCvSharp.Point((int)centerX, (int)centerY), 4, Scalar.Red, -1);
                        Cv2.PutText(frameMat, $"P{index} S={maxVal:F3}", new OpenCvSharp.Point(Math.Max(0, maxLoc.X - 4), Math.Max(16, maxLoc.Y - 6)), HersheyFonts.HersheySimplex, 0.6, Scalar.Yellow, 2);

                        PreviewImage = ConvertMatToBitmap(frameMat);

                        Points.Add(new NinePointCalibrationPointViewModel
                        {
                            Index = index,
                            StageX = targetX,
                            StageY = targetY,
                            PixelX = centerX,
                            PixelY = centerY,
                            Score = maxVal
                        });

                        StatusMessage = $"已完成 {index}/9";
                        index++;
                    }
                }

                if (!TryCalculatePixelSize(out var pixelSizeX, out var pixelSizeY))
                {
                    StatusMessage = "标定计算失败，像素步长无效";
                    return;
                }

                ManualPixelSizeX = pixelSizeX;
                ManualPixelSizeY = pixelSizeY;
                CalibrationResult = new NinePointCalibrationResult { PixelSizeXUm = pixelSizeX, PixelSizeYUm = pixelSizeY };
                StatusMessage = $"标定完成 X={pixelSizeX:F4}μm, Y={pixelSizeY:F4}μm";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "标定已取消";
            }
            catch (Exception ex)
            {
                StatusMessage = $"标定异常: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        [RelayCommand]
        private void ConfirmApply()
        {
            if (CalibrationResult is null)
            {
                if (ManualPixelSizeX > 0 && ManualPixelSizeY > 0)
                {
                    CalibrationResult = new NinePointCalibrationResult
                    {
                        PixelSizeXUm = ManualPixelSizeX,
                        PixelSizeYUm = ManualPixelSizeY
                    };
                }
                else
                {
                    StatusMessage = "请先执行标定或输入有效手动值";
                    return;
                }
            }

            RequestClose?.Invoke(true);
        }

        [RelayCommand]
        private void Cancel()
        {
            _cts?.Cancel();
            RequestClose?.Invoke(false);
        }

        private bool TryCalculatePixelSize(out double pixelSizeX, out double pixelSizeY)
        {
            pixelSizeX = 0;
            pixelSizeY = 0;

            if (Points.Count < 9)
            {
                return false;
            }

            var xPixelSteps = new[]
            {
                Math.Abs(Points[1].PixelX - Points[0].PixelX), Math.Abs(Points[2].PixelX - Points[1].PixelX),
                Math.Abs(Points[4].PixelX - Points[3].PixelX), Math.Abs(Points[5].PixelX - Points[4].PixelX),
                Math.Abs(Points[7].PixelX - Points[6].PixelX), Math.Abs(Points[8].PixelX - Points[7].PixelX)
            }.Where(v => v > 0).ToArray();

            var yPixelSteps = new[]
            {
                Math.Abs(Points[3].PixelY - Points[0].PixelY), Math.Abs(Points[6].PixelY - Points[3].PixelY),
                Math.Abs(Points[4].PixelY - Points[1].PixelY), Math.Abs(Points[7].PixelY - Points[4].PixelY),
                Math.Abs(Points[5].PixelY - Points[2].PixelY), Math.Abs(Points[8].PixelY - Points[5].PixelY)
            }.Where(v => v > 0).ToArray();

            if (xPixelSteps.Length == 0 || yPixelSteps.Length == 0)
            {
                return false;
            }

            pixelSizeX = (XStep / xPixelSteps.Average()) * 1000.0;
            pixelSizeY = (YStep / yPixelSteps.Average()) * 1000.0;
            return true;
        }

        private void RefreshAxisPositions()
        {
            if (_motionService is null)
            {
                return;
            }

            try
            {
                CurrentX = _motionService.GetXPosition();
                CurrentY = _motionService.GetYPosition();
                CurrentZ = _motionService.GetZPosition();

                AbsoluteTargetX = CurrentX;
                AbsoluteTargetY = CurrentY;
                AbsoluteTargetZ = CurrentZ;
            }
            catch (Exception ex)
            {
                StatusMessage = $"读取当前位置失败: {ex.Message}";
            }
        }

        private static Mat CreateVirtualTriangleTemplate(int width, int height)
        {
            var template = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
            var pts = new[]
            {
                new OpenCvSharp.Point(width / 2, 2),
                new OpenCvSharp.Point(2, height - 3),
                new OpenCvSharp.Point(width - 3, height - 3)
            };

            Cv2.FillConvexPoly(template, pts, Scalar.White);
            return template;
        }

        private Mat CreateVirtualFrameWithTemplate(Mat template, int row, int col)
        {
            const int frameWidth = 960;
            const int frameHeight = 720;
            const int spacingX = 180;
            const int spacingY = 130;

            using var gray = new Mat(frameHeight, frameWidth, MatType.CV_8UC1, new Scalar(48));

            for (int i = 0; i < 200; i++)
            {
                var x = _random.Next(0, frameWidth);
                var y = _random.Next(0, frameHeight);
                Cv2.Circle(gray, new OpenCvSharp.Point(x, y), _random.Next(1, 3), new Scalar(_random.Next(30, 70)), -1);
            }

            var centerX = frameWidth / 2 + ((col - 1) * spacingX) + _random.Next(-6, 7);
            var centerY = frameHeight / 2 + ((row - 1) * spacingY) + _random.Next(-6, 7);
            var left = Math.Clamp(centerX - (template.Width / 2), 0, frameWidth - template.Width);
            var top = Math.Clamp(centerY - (template.Height / 2), 0, frameHeight - template.Height);

            var roiRect = new OpenCvSharp.Rect(left, top, template.Width, template.Height);
            using (var roi = new Mat(gray, roiRect))
            {
                template.CopyTo(roi, template);
            }

            var bgr = new Mat();
            Cv2.CvtColor(gray, bgr, ColorConversionCodes.GRAY2BGR);
            return bgr;
        }

        private static Mat? ConvertToBgrMat(CameraArgs? args)
        {
            if (args?.Data is null || args.Width <= 0 || args.Height <= 0)
            {
                return null;
            }

            return args.Format switch
            {
                HAL.PixelFormat.Mono8 => CreateMatFromRaw(args, MatType.CV_8UC1, args.Stride > 0 ? args.Stride : args.Width).CvtColor(ColorConversionCodes.GRAY2BGR),
                HAL.PixelFormat.BGR8 => CreateMatFromRaw(args, MatType.CV_8UC3, args.Stride > 0 ? args.Stride : args.Width * 3),
                HAL.PixelFormat.RGB8 => ConvertColor(CreateMatFromRaw(args, MatType.CV_8UC3, args.Stride > 0 ? args.Stride : args.Width * 3), ColorConversionCodes.RGB2BGR),
                HAL.PixelFormat.BGRA8 => ConvertColor(CreateMatFromRaw(args, MatType.CV_8UC4, args.Stride > 0 ? args.Stride : args.Width * 4), ColorConversionCodes.BGRA2BGR),
                HAL.PixelFormat.RGBA8 => ConvertColor(CreateMatFromRaw(args, MatType.CV_8UC4, args.Stride > 0 ? args.Stride : args.Width * 4), ColorConversionCodes.RGBA2BGR),
                _ => CreateMatFromRaw(args, MatType.CV_8UC3, args.Stride > 0 ? args.Stride : args.Width * 3)
            };
        }

        private static Mat CreateMatFromRaw(CameraArgs args, MatType type, int stride)
        {
            var handle = GCHandle.Alloc(args.Data!, GCHandleType.Pinned);
            try
            {
                using var view = Mat.FromPixelData(args.Height, args.Width, type, handle.AddrOfPinnedObject(), stride);
                return view.Clone();
            }
            finally
            {
                handle.Free();
            }
        }

        private static Mat ConvertColor(Mat src, ColorConversionCodes code)
        {
            using (src)
            {
                var dst = new Mat();
                Cv2.CvtColor(src, dst, code);
                return dst;
            }
        }

        private static BitmapSource ConvertMatToBitmap(Mat mat)
        {
            var stride = (int)mat.Step();
            var bufferSize = stride * mat.Rows;
            var source = BitmapSource.Create(mat.Width, mat.Height, 96, 96, PixelFormats.Bgr24, null, mat.Data, bufferSize, stride);
            source.Freeze();
            return source;
        }
    }
}
