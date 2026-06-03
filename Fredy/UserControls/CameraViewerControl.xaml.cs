using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HAL;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MvCamCtrl.NET;
using Mat = OpenCvSharp.Mat;
using CvPoint2d = OpenCvSharp.Point2d;
using CvRect = OpenCvSharp.Rect;

namespace Fredy.Drilling.Holes.UserControls
{
    public sealed class WorkpieceHoleCenterDetectedEventArgs : EventArgs
    {
        public WorkpieceHoleCenterDetectedEventArgs(double pixelX, double pixelY, double radiusPixels, int sourceWidth, int sourceHeight)
        {
            PixelX = pixelX;
            PixelY = pixelY;
            RadiusPixels = radiusPixels;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
        }

        public double PixelX { get; }

        public double PixelY { get; }

        public double RadiusPixels { get; }

        public int SourceWidth { get; }

        public int SourceHeight { get; }
    }

    public sealed class WorkpieceEdgePointDetectedEventArgs : EventArgs
    {
        public WorkpieceEdgePointDetectedEventArgs(double pixelX, double pixelY, int sourceWidth, int sourceHeight)
        {
            PixelX = pixelX;
            PixelY = pixelY;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
        }

        public double PixelX { get; }

        public double PixelY { get; }

        public int SourceWidth { get; }

        public int SourceHeight { get; }
    }

    public partial class CameraViewerControl : UserControl
    {
        public static readonly DependencyProperty ImageMatProperty = DependencyProperty.Register(
            nameof(ImageMat), typeof(OpenCvSharp.Mat), typeof(CameraViewerControl),
            new PropertyMetadata(null, OnImageMatChanged));

        public OpenCvSharp.Mat ImageMat
        {
            get => (OpenCvSharp.Mat)GetValue(ImageMatProperty);
            set => SetValue(ImageMatProperty, value);
        }

        private static void OnImageMatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CameraViewerControl control && e.NewValue is OpenCvSharp.Mat mat && !mat.Empty())
            {
                // 只有在显示时转成 Bitmap
                control.ImageSource = Tools.VisionUIHelper.MatToBitmapSource(mat);

                if (control.MenuDetectCircles.IsChecked)
                {
                    using var result = control.RunCircleDetection();
                }

                control.RefreshCenterRoiBinaryPreview();
            }
            else if (d is CameraViewerControl imageMatClearedControl)
            {
                imageMatClearedControl.RefreshCenterRoiBinaryPreview();
            }
        }

        public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
            nameof(ImageSource), typeof(ImageSource), typeof(CameraViewerControl),
            new PropertyMetadata(null, OnImageSourceChanged));

        public ImageSource ImageSource
        {
            get => (ImageSource)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }

        public static readonly DependencyProperty ShowCaptureButtonsProperty = DependencyProperty.Register(
            nameof(ShowCaptureButtons), typeof(bool), typeof(CameraViewerControl), new PropertyMetadata(true));

        public bool ShowCaptureButtons
        {
            get => (bool)GetValue(ShowCaptureButtonsProperty);
            set => SetValue(ShowCaptureButtonsProperty, value);
        }

        public static readonly DependencyProperty CameraConnectionStatusTextProperty = DependencyProperty.Register(
            nameof(CameraConnectionStatusText), typeof(string), typeof(CameraViewerControl), new PropertyMetadata("相机状态：未连接"));

        public string CameraConnectionStatusText
        {
            get => (string)GetValue(CameraConnectionStatusTextProperty);
            set => SetValue(CameraConnectionStatusTextProperty, value);
        }

        public static readonly DependencyProperty CameraGrabModeStatusTextProperty = DependencyProperty.Register(
            nameof(CameraGrabModeStatusText), typeof(string), typeof(CameraViewerControl), new PropertyMetadata("采集模式：单张/空闲"));

        public string CameraGrabModeStatusText
        {
            get => (string)GetValue(CameraGrabModeStatusTextProperty);
            set => SetValue(CameraGrabModeStatusTextProperty, value);
        }

        public static readonly DependencyProperty CameraLatestFrameStatusTextProperty = DependencyProperty.Register(
            nameof(CameraLatestFrameStatusText), typeof(string), typeof(CameraViewerControl), new PropertyMetadata("最新帧：-"));

        public string CameraLatestFrameStatusText
        {
            get => (string)GetValue(CameraLatestFrameStatusTextProperty);
            set => SetValue(CameraLatestFrameStatusTextProperty, value);
        }

        public static readonly DependencyProperty CameraOperationLogTextProperty = DependencyProperty.Register(
            nameof(CameraOperationLogText), typeof(string), typeof(CameraViewerControl), new PropertyMetadata("操作日志：就绪"));

        public string CameraOperationLogText
        {
            get => (string)GetValue(CameraOperationLogTextProperty);
            set => SetValue(CameraOperationLogTextProperty, value);
        }

        public static readonly DependencyProperty CanGrabSingleProperty = DependencyProperty.Register(
            nameof(CanGrabSingle), typeof(bool), typeof(CameraViewerControl), new PropertyMetadata(true));

        public bool CanGrabSingle
        {
            get => (bool)GetValue(CanGrabSingleProperty);
            set => SetValue(CanGrabSingleProperty, value);
        }

        public static readonly DependencyProperty CanStartContinuousGrabProperty = DependencyProperty.Register(
            nameof(CanStartContinuousGrab), typeof(bool), typeof(CameraViewerControl), new PropertyMetadata(true));

        public bool CanStartContinuousGrab
        {
            get => (bool)GetValue(CanStartContinuousGrabProperty);
            set => SetValue(CanStartContinuousGrabProperty, value);
        }

        public static readonly DependencyProperty CanStopContinuousGrabProperty = DependencyProperty.Register(
            nameof(CanStopContinuousGrab), typeof(bool), typeof(CameraViewerControl), new PropertyMetadata(false));

        public bool CanStopContinuousGrab
        {
            get => (bool)GetValue(CanStopContinuousGrabProperty);
            set => SetValue(CanStopContinuousGrabProperty, value);
        }

        public static readonly DependencyProperty ShowWorkpieceCalibrationToolsProperty = DependencyProperty.Register(
            nameof(ShowWorkpieceCalibrationTools), typeof(bool), typeof(CameraViewerControl), new PropertyMetadata(false));

        public bool ShowWorkpieceCalibrationTools
        {
            get => (bool)GetValue(ShowWorkpieceCalibrationToolsProperty);
            set => SetValue(ShowWorkpieceCalibrationToolsProperty, value);
        }

        public bool IsContinuousGrabActive => _isContinuousGrabbing || (_camera?.IsContinuousGrabbing ?? false);

        /// <summary>
        /// 设为 true 时，Unloaded 不会自动停止相机连续采集。
        /// 用于主界面在弹出子窗口（内含 CameraViewerControl）期间保护自身的连续采集状态。
        /// </summary>
        public bool SuppressAutoStopOnUnload { get; set; }

        private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CameraViewerControl control && control.MenuDetectCircles.IsChecked)
            {
                using var result = control.RunCircleDetection();
            }

            if (d is CameraViewerControl sourceControl)
            {
                sourceControl.RefreshCenterRoiBinaryPreview();
            }
        }

        private readonly ICamera? _camera;
        private readonly Services.ConfigService? _configService;
        private bool _isContinuousGrabbing;
        private bool _isSingleGrabInProgress;
        private bool _isSubscribedToCameraEvents;
        private System.Windows.Window? _ownerWindow;
        private long _latestFrameId;
        private DateTime? _latestFrameTimestamp;
        private int _continuousGrabFrameRate = 20;
        private RoiInteractionMode _roiInteractionMode;
        private bool _isWorkpieceEdgePickMode;
        private readonly List<WorkpieceEdgeOverlayState> _workpieceEdgeOverlayPoints = new();
        private WorkpieceHoleOverlayState? _workpieceHoleOverlay;

        public event EventHandler<WorkpieceHoleCenterDetectedEventArgs>? WorkpieceHoleCenterDetected;
        public event EventHandler<WorkpieceEdgePointDetectedEventArgs>? WorkpieceEdgePointDetected;

        private System.Windows.Point _startDragPos;
        private bool _isDragging = false;
        private bool _isDrawingROI = false;
        private bool _isDraggingROI = false;

        private enum RoiInteractionMode
        {
            None,
            TemplateExport,
            WorkpieceHoleCalibration,
        }

        private sealed class WorkpieceEdgeOverlayState
        {
            public WorkpieceEdgeOverlayState(CvPoint2d pixelPoint, int sourceWidth, int sourceHeight)
            {
                PixelPoint = pixelPoint;
                SourceWidth = sourceWidth;
                SourceHeight = sourceHeight;
            }

            public CvPoint2d PixelPoint { get; }

            public int SourceWidth { get; }

            public int SourceHeight { get; }
        }

        private sealed class WorkpieceHoleOverlayState
        {
            public WorkpieceHoleOverlayState(CvPoint2d pixelCenter, double pixelRadius, CvRect roiRect, int sourceWidth, int sourceHeight)
            {
                PixelCenter = pixelCenter;
                PixelRadius = pixelRadius;
                RoiRect = roiRect;
                SourceWidth = sourceWidth;
                SourceHeight = sourceHeight;
            }

            public CvPoint2d PixelCenter { get; }

            public double PixelRadius { get; }

            public CvRect RoiRect { get; }

            public int SourceWidth { get; }

            public int SourceHeight { get; }
        }

        public CameraViewerControl()
        {
            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                _camera = App.ServiceProvider.GetRequiredService<ICamera>();
                _configService = App.ServiceProvider.GetRequiredService<Services.ConfigService>();
            }
            else
            {
                RefreshCameraStatus();
            }

            Loaded += CameraViewerControl_Loaded;
            Unloaded += CameraViewerControl_Unloaded;
            IsVisibleChanged += CameraViewerControl_IsVisibleChanged;
            UpdateContinuousGrabFrameRateMenuChecks();
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_roiInteractionMode != RoiInteractionMode.None)
            {
                _isDrawingROI = true;
                var pos = e.GetPosition(ROICanvas);
                Canvas.SetLeft(ROIRect, pos.X);
                Canvas.SetTop(ROIRect, pos.Y);
                ROIRect.Width = 0;
                ROIRect.Height = 0;
                ROICanvas.Visibility = Visibility.Visible;
                Viewport.CaptureMouse();
                e.Handled = true;
            }
            else if (_isWorkpieceEdgePickMode)
            {
                HandleWorkpieceEdgePick(e.GetPosition(DisplayImage));
                e.Handled = true;
            }
            else
            {
                _isDragging = true;
                _startDragPos = e.GetPosition(this);
                Viewport.CaptureMouse();
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var completedRoiMode = _roiInteractionMode;
            bool shouldProcessRoi = _isDrawingROI;

            _isDragging = false;
            _isDrawingROI = false;
            Viewport.ReleaseMouseCapture();

            if (!shouldProcessRoi)
            {
                return;
            }

            if (completedRoiMode == RoiInteractionMode.WorkpieceHoleCalibration)
            {
                TryDetectWorkpieceHoleCenterFromCurrentRoi();
                _roiInteractionMode = RoiInteractionMode.None;
                if (MenuWorkpieceHoleRoiMode != null)
                {
                    MenuWorkpieceHoleRoiMode.IsChecked = false;
                }
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDrawingROI && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(ROICanvas);
                double left = Canvas.GetLeft(ROIRect);
                double top = Canvas.GetTop(ROIRect);
                ROIRect.Width = Math.Max(0, pos.X - left);
                ROIRect.Height = Math.Max(0, pos.Y - top);
            }
            else if (_isDragging)
            {
                System.Windows.Point pos = e.GetPosition(this);
                Translation.X += (pos.X - _startDragPos.X);
                Translation.Y += (pos.Y - _startDragPos.Y);
                _startDragPos = pos;
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double zoom = e.Delta > 0 ? 1.1 : 0.9;
            Scaling.ScaleX *= zoom;
            Scaling.ScaleY *= zoom;
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            Scaling.ScaleX = 1;
            Scaling.ScaleY = 1;
            Translation.X = 0;
            Translation.Y = 0;
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (ImageSource is BitmapSource bitmap)
            {
                var dlg = new SaveFileDialog { Filter = "PNG Image|*.png" };
                if (dlg.ShowDialog() == true)
                {
                    using var stream = new System.IO.FileStream(dlg.FileName, System.IO.FileMode.Create);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                }
            }
        }

        private void ToggleCross_Click(object sender, RoutedEventArgs e)
        {
            CenterCross.Visibility = MenuShowCross.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetContinuousGrabFrameRate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string valueText || !int.TryParse(valueText, out var frameRate))
            {
                SetOperationLog("连续模式帧率设置失败：无效的帧率参数。");
                UpdateContinuousGrabFrameRateMenuChecks();
                return;
            }

            _continuousGrabFrameRate = frameRate;
            UpdateContinuousGrabFrameRateMenuChecks();

            if (_camera is HkCamera hkCamera && hkCamera.IsConnected && hkCamera.IsContinuousGrabbing)
            {
                var ret = hkCamera.TrySetFrameRate(frameRate);
                if (ret == MyCamera.MV_OK)
                {
                    SetOperationLog($"连续模式帧率已更新为 {frameRate} FPS。", true);
                }
                else
                {
                    SetOperationLog($"连续模式帧率设置失败，错误码: 0x{ret:X8}");
                }

                return;
            }

            SetOperationLog($"连续模式帧率目标值已设置为 {frameRate} FPS，启动连续采集时生效。", true);
        }

        private async void GrabSingle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isSingleGrabInProgress = true;
                RefreshCaptureActionStates();
                await EnsureCameraOpenAsync();
                if (_camera is null)
                {
                    return;
                }

                var frame = await _camera.GrabAsync();
                UpdateImage(frame);
                RefreshCameraStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"单张拍照失败：{ex.Message}", "相机", MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshCameraStatus();
            }
            finally
            {
                _isSingleGrabInProgress = false;
                RefreshCaptureActionStates();
            }
        }

        private async void StartContinuousGrab_Click(object sender, RoutedEventArgs e)
        {
            await StartContinuousGrabAsync();
        }

        private async Task StartContinuousGrabAsync()
        {
            if (_isContinuousGrabbing)
            {
                return;
            }

            try
            {
                await EnsureCameraOpenAsync();

                if (_camera is null)
                {
                    return;
                }

                if (_camera is HkCamera hkCamera)
                {
                    var frameRateRet = hkCamera.TrySetFrameRate(_continuousGrabFrameRate);
                    if (frameRateRet != MyCamera.MV_OK)
                    {
                        throw new InvalidOperationException($"设置连续采集帧率失败，错误码: 0x{frameRateRet:X8}");
                    }

                    SetOperationLog($"连续模式帧率已设置为 {_continuousGrabFrameRate} FPS。");
                }
                else
                {
                    SetOperationLog($"当前相机不支持设置连续模式帧率，保留目标值 {_continuousGrabFrameRate} FPS。");
                }

                await Task.Run(() => _camera.StartContinuousGrab());
                _isContinuousGrabbing = true;
                UpdateCameraSubscription();
                RefreshCameraStatus();
                RefreshCaptureActionStates();
            }
            catch (Exception ex)
            {
                await StopContinuousGrabAsync();
                MessageBox.Show($"启动连续拍照失败：{ex.Message}", "相机", MessageBoxButton.OK, MessageBoxImage.Error);
                RefreshCameraStatus();
                RefreshCaptureActionStates();
            }
        }

        private async void StopContinuousGrab_Click(object sender, RoutedEventArgs e)
        {
            await StopContinuousGrabAsync();
        }

        private async Task EnsureCameraOpenAsync()
        {
            if (_camera is null)
            {
                throw new InvalidOperationException("设计模式下不加载相机服务。");
            }

            if (_camera.IsConnected)
            {
                return;
            }

            var opened = await Task.Run(() => _camera.Open());
            if (!opened)
            {
                throw new InvalidOperationException("相机打开失败。");
            }

            RefreshCameraStatus();
        }

        private void UpdateImage(CameraArgs frame)
        {
            using var mat = Tools.VisionUIHelper.CameraArgsToMat(frame);
            if (mat.Empty())
            {
                return;
            }

            var oldMat = ImageMat;
            ImageMat = mat.Clone();
            oldMat?.Dispose();

            _latestFrameId = frame.FrameId;
            _latestFrameTimestamp = frame.Timestamp;
        }

        private async Task StopContinuousGrabAsync()
        {
            if (_camera is null)
            {
                return;
            }

            if (!_isContinuousGrabbing && !_camera.IsContinuousGrabbing)
            {
                UpdateCameraSubscription();
                return;
            }

            try
            {
                if (_camera.IsContinuousGrabbing)
                {
                    await Task.Run(() => _camera.StopContinuousGrab());
                }
            }
            finally
            {
                _isContinuousGrabbing = false;
                UpdateCameraSubscription();
                RefreshCameraStatus();
                RefreshCaptureActionStates();
            }

            await Task.CompletedTask;
        }

        public Task StopContinuousGrabForNavigationAsync()
        {
            return StopContinuousGrabAsync();
        }

        public Task StartContinuousGrabForNavigationAsync()
        {
            return StartContinuousGrabAsync();
        }

        private void CameraViewerControl_Loaded(object sender, RoutedEventArgs e)
        {
            HookOwnerWindow();
            UpdateCameraSubscription();
            RefreshCameraStatus();
            RefreshCaptureActionStates();
            RefreshCenterRoiBinaryPreview();
        }

        private async void CameraViewerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            UnhookOwnerWindow();
            SetCameraSubscription(false);
            if (!SuppressAutoStopOnUnload)
            {
                await StopContinuousGrabAsync();
            }
            RefreshCameraStatus();
            RefreshCaptureActionStates();
        }

        private void CameraViewerControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UpdateCameraSubscription();
        }

        private void Camera_ImageGrabbed(object? sender, CameraArgs e)
        {
            if (Dispatcher.CheckAccess())
            {
                UpdateImage(e);
                RefreshCameraStatus();
                return;
            }

            _ = Dispatcher.InvokeAsync(() =>
            {
                UpdateImage(e);
                RefreshCameraStatus();
            });
        }

        private void HookOwnerWindow()
        {
            var window = System.Windows.Window.GetWindow(this);
            if (ReferenceEquals(_ownerWindow, window))
            {
                return;
            }

            UnhookOwnerWindow();
            _ownerWindow = window;
            if (_ownerWindow is not null)
            {
                _ownerWindow.Activated += OwnerWindow_Activated;
                _ownerWindow.Deactivated += OwnerWindow_Deactivated;
            }
        }

        private void UnhookOwnerWindow()
        {
            if (_ownerWindow is null)
            {
                return;
            }

            _ownerWindow.Activated -= OwnerWindow_Activated;
            _ownerWindow.Deactivated -= OwnerWindow_Deactivated;
            _ownerWindow = null;
        }

        private void OwnerWindow_Activated(object? sender, EventArgs e)
        {
            UpdateCameraSubscription();
        }

        private void OwnerWindow_Deactivated(object? sender, EventArgs e)
        {
            UpdateCameraSubscription();
        }

        private void UpdateCameraSubscription()
        {
            var shouldSubscribe = _camera is not null
                && IsLoaded
                && IsVisible
                && (_ownerWindow?.IsActive ?? true);

            SetCameraSubscription(shouldSubscribe);
        }

        private void SetCameraSubscription(bool subscribe)
        {
            if (_camera is null || _isSubscribedToCameraEvents == subscribe)
            {
                return;
            }

            if (subscribe)
            {
                _camera.ImageGrabbed += Camera_ImageGrabbed;
            }
            else
            {
                _camera.ImageGrabbed -= Camera_ImageGrabbed;
            }

            _isSubscribedToCameraEvents = subscribe;
        }

        private void RefreshCameraStatus()
        {
            if (_camera is null)
            {
                CameraConnectionStatusText = "相机状态：未连接";
                CameraGrabModeStatusText = "采集模式：单张/空闲";
                CameraLatestFrameStatusText = "最新帧：-";
                RefreshCaptureActionStates();
                return;
            }

            CameraConnectionStatusText = _camera.IsConnected ? "相机状态：已连接" : "相机状态：未连接";
            CameraGrabModeStatusText = _camera.IsContinuousGrabbing ? "采集模式：连续采集" : "采集模式：单张/空闲";
            CameraLatestFrameStatusText = _latestFrameTimestamp.HasValue
                ? $"最新帧：{_latestFrameId} / {_latestFrameTimestamp:HH:mm:ss.fff}"
                : "最新帧：-";
            RefreshCaptureActionStates();
        }

        private void RefreshCaptureActionStates()
        {
            var isContinuousMode = _isContinuousGrabbing || (_camera?.IsContinuousGrabbing ?? false);
            var canOperate = !_isSingleGrabInProgress;

            CanGrabSingle = canOperate && !isContinuousMode;
            CanStartContinuousGrab = canOperate && !isContinuousMode;
            CanStopContinuousGrab = canOperate && isContinuousMode;
        }

        private void UpdateContinuousGrabFrameRateMenuChecks()
        {
            if (ContinuousGrabFrameRate5MenuItem is null)
            {
                return;
            }

            ContinuousGrabFrameRate5MenuItem.IsChecked = _continuousGrabFrameRate == 5;
            ContinuousGrabFrameRate10MenuItem.IsChecked = _continuousGrabFrameRate == 10;
            ContinuousGrabFrameRate20MenuItem.IsChecked = _continuousGrabFrameRate == 20;
            ContinuousGrabFrameRate30MenuItem.IsChecked = _continuousGrabFrameRate == 30;
        }

        private void SetOperationLog(string message, bool includeTimestamp = false)
        {
            CameraOperationLogText = includeTimestamp
                ? $"操作日志：[{DateTime.Now:HH:mm:ss}] {message}"
                : $"操作日志：{message}";
        }

        private void StartDrawROI_Click(object sender, RoutedEventArgs e)
        {
            _roiInteractionMode = RoiInteractionMode.TemplateExport;
            ROICanvas.Visibility = Visibility.Visible;
            OverlayCanvas.Children.Clear();
            SetOperationLog("请拖拽绘制模板 ROI。", true);
        }

        private void ROIRect_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingROI = true;
            _startDragPos = e.GetPosition(ROICanvas);
            ROIRect.CaptureMouse();
            e.Handled = true;
        }

        private void ROIRect_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingROI = false;
            ROIRect.ReleaseMouseCapture();
        }

        private void ROIRect_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingROI)
            {
                var pos = e.GetPosition(ROICanvas);
                Canvas.SetLeft(ROIRect, Canvas.GetLeft(ROIRect) + (pos.X - _startDragPos.X));
                Canvas.SetTop(ROIRect, Canvas.GetTop(ROIRect) + (pos.Y - _startDragPos.Y));
                _startDragPos = pos;
            }
        }

        // Demo behavior for circle detection inside UserControl
        private void DetectCircles_Click(object sender, RoutedEventArgs e)
        {
            if (MenuDetectCircles.IsChecked)
            {
                using var result = RunCircleDetection();
            }
            else
            {
                OverlayCanvas.Children.Clear();
            }
        }

        private void ToggleCenterRoiBinary_Click(object sender, RoutedEventArgs e)
        {
            RefreshCenterRoiBinaryPreview();
        }

        private void EditCenterRoiBinaryParams_Click(object sender, RoutedEventArgs e)
        {
            Mat? snapshotMat = null;
            ImageSource? snapshotSource = null;

            if (ImageMat != null && !ImageMat.Empty())
            {
                snapshotMat = ImageMat.Clone();
            }
            else if (ImageSource != null)
            {
                snapshotSource = ImageSource.CloneCurrentValue();
            }

            try
            {
                var dialog = new Views.CenterRoiBinarySettingsWindow(snapshotMat, snapshotSource);
                dialog.Owner = System.Windows.Window.GetWindow(this);
                if (dialog.ShowDialog() == true && MenuCenterRoiBinaryPreview.IsChecked)
                {
                    RefreshCenterRoiBinaryPreview();
                }
            }
            finally
            {
                snapshotMat?.Dispose();
            }
        }

        public void DetectCirclesSingle_Click(object sender, RoutedEventArgs e)
        {
            using var result = RunCircleDetection();
        }

        private void EditDetectParams_Click(object sender, RoutedEventArgs e)
        {
            if (ImageSource == null || DisplayImage.ActualWidth <= 0)
            {
                MessageBox.Show("当前没有相机图像或图像尺寸无效，无法编辑参数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new Views.CircleDetectionSettingsWindow(ImageSource.Clone());
            dialog.Owner = System.Windows.Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                // User pressed Apply. Rerender with the updated globals if monitoring is active
                if (MenuDetectCircles.IsChecked)
                {
                    using var result = RunCircleDetection();
                }
            }
        }

        public Common.Services.DetectionResult RunCircleDetection(
            bool? isDarkHole = null, 
            double? minRadius = null, 
            double? maxRadius = null, 
            double? param1 = null, 
            double? param2 = null)
        {
            if (DisplayImage.ActualWidth <= 0 || DisplayImage.ActualHeight <= 0) return null;

            var configService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Services.ConfigService>(App.ServiceProvider);
            var config = configService.CurrentConfig;

            bool actualIsDarkHole = isDarkHole ?? config.CircleIsDarkTarget;
            double actualMinRadius = minRadius ?? config.CircleMinRadius;
            double actualMaxRadius = maxRadius ?? config.CircleMaxRadius;
            double actualParam1 = param1 ?? config.CircleParam1;
            double actualParam2 = param2 ?? config.CircleParam2;

            if (ImageMat != null && !ImageMat.Empty())
            {
                return Tools.VisionUIHelper.DetectAndDrawCircles(ImageMat, DisplayImage.ActualWidth, DisplayImage.ActualHeight, OverlayCanvas, actualIsDarkHole, actualMinRadius, actualMaxRadius, actualParam1, actualParam2);
            }
            else if (ImageSource is BitmapSource bitmap)
            {
                return Tools.VisionUIHelper.DetectAndDrawCircles(bitmap, DisplayImage.ActualWidth, DisplayImage.ActualHeight, OverlayCanvas, actualIsDarkHole, actualMinRadius, actualMaxRadius, actualParam1, actualParam2);
            }
            else if (!MenuDetectCircles.IsChecked) // Only warn if it's a single click via other ways, avoid spam checking
            {
                MessageBox.Show("当前没有相机图像或图像尺寸无效。");
            }
            return null;
        }

        private void ExportROI_Click(object sender, RoutedEventArgs e)
        {
            if (ImageSource is BitmapSource bitmap && ROICanvas.Visibility == Visibility.Visible && DisplayImage.ActualWidth > 0)
            {
                var dlg = new SaveFileDialog { Filter = "PNG Template|*.png", Title = "导出ROI为模板" };
                if (dlg.ShowDialog() == true)
                {
                    double left = double.IsNaN(Canvas.GetLeft(ROIRect)) ? 0 : Canvas.GetLeft(ROIRect);
                    double top = double.IsNaN(Canvas.GetTop(ROIRect)) ? 0 : Canvas.GetTop(ROIRect);
                    System.Windows.Rect roi = new System.Windows.Rect(left, top, ROIRect.Width, ROIRect.Height);
                    
                    Fredy.Drilling.Holes.Tools.VisionUIHelper.ExportROI(bitmap, DisplayImage.ActualWidth, DisplayImage.ActualHeight, roi, dlg.FileName);
                    MessageBox.Show("ROI模板已成功导出！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("请先确认相机已开启并成功绘制了 ROI 区域。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DisplayImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            OverlayCanvas.Width = e.NewSize.Width;
            OverlayCanvas.Height = e.NewSize.Height;
            CalibrationOverlayCanvas.Width = e.NewSize.Width;
            CalibrationOverlayCanvas.Height = e.NewSize.Height;
            CenterRoiOverlayCanvas.Width = e.NewSize.Width;
            CenterRoiOverlayCanvas.Height = e.NewSize.Height;
            ROICanvas.Width = e.NewSize.Width;
            ROICanvas.Height = e.NewSize.Height;
            RedrawWorkpieceCalibrationOverlay();
            RefreshCenterRoiBinaryPreview();
        }

        private void ToggleWorkpieceHoleRoiMode_Click(object sender, RoutedEventArgs e)
        {
            if (!ShowWorkpieceCalibrationTools)
            {
                return;
            }

            if (MenuWorkpieceHoleRoiMode.IsChecked)
            {
                MenuWorkpieceEdgePickMode.IsChecked = false;
                _isWorkpieceEdgePickMode = false;
                _roiInteractionMode = RoiInteractionMode.WorkpieceHoleCalibration;
                ROICanvas.Visibility = Visibility.Visible;
                SetOperationLog("圆孔 ROI 识别已启用，请在图像上拖拽框选圆孔区域。", true);
                return;
            }

            if (_roiInteractionMode == RoiInteractionMode.WorkpieceHoleCalibration)
            {
                _roiInteractionMode = RoiInteractionMode.None;
                _isDrawingROI = false;
                SetOperationLog("已退出圆孔 ROI 识别模式。", true);
            }
        }

        private void ToggleWorkpieceEdgePickMode_Click(object sender, RoutedEventArgs e)
        {
            if (!ShowWorkpieceCalibrationTools)
            {
                return;
            }

            _isWorkpieceEdgePickMode = MenuWorkpieceEdgePickMode.IsChecked;
            if (_isWorkpieceEdgePickMode)
            {
                MenuWorkpieceHoleRoiMode.IsChecked = false;
                if (_roiInteractionMode == RoiInteractionMode.WorkpieceHoleCalibration)
                {
                    _roiInteractionMode = RoiInteractionMode.None;
                    _isDrawingROI = false;
                }

                SetOperationLog("边缘点自动拾取已启用，请左键点击圆边附近位置。", true);
                return;
            }

            SetOperationLog("已退出边缘点自动拾取模式。", true);
        }

        private void ClearWorkpieceCalibrationMarkers_Click(object sender, RoutedEventArgs e)
        {
            _workpieceEdgeOverlayPoints.Clear();
            _workpieceHoleOverlay = null;
            CalibrationOverlayCanvas.Children.Clear();
            SetOperationLog("已清除工件圆心校准图像标记。", true);
        }

        private void HandleWorkpieceEdgePick(System.Windows.Point uiPoint)
        {
            using var sourceMat = CreateWorkingMat();
            if (sourceMat == null || sourceMat.Empty())
            {
                MessageBox.Show("当前没有可用于自动寻边的图像。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Fredy.Drilling.Holes.Tools.VisionUIHelper.TryConvertUiPointToImagePixel(sourceMat.Width, sourceMat.Height, DisplayImage.ActualWidth, DisplayImage.ActualHeight, uiPoint, out var imagePoint))
            {
                MessageBox.Show("点击位置不在有效图像区域内。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Fredy.Drilling.Holes.Tools.VisionUIHelper.TryFindWhiteObjectEdgePoint(sourceMat, imagePoint, out var edgePoint))
            {
                MessageBox.Show("未找到有效边缘，请更靠近工件圆边重新点击。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _workpieceEdgeOverlayPoints.Add(new WorkpieceEdgeOverlayState(edgePoint, sourceMat.Width, sourceMat.Height));
            RedrawWorkpieceCalibrationOverlay();
            WorkpieceEdgePointDetected?.Invoke(this, new WorkpieceEdgePointDetectedEventArgs(edgePoint.X, edgePoint.Y, sourceMat.Width, sourceMat.Height));
            SetOperationLog($"已自动拾取边缘点：像素({edgePoint.X:F1}, {edgePoint.Y:F1})。", true);
        }

        private void TryDetectWorkpieceHoleCenterFromCurrentRoi()
        {
            using var sourceMat = CreateWorkingMat();
            if (sourceMat == null || sourceMat.Empty())
            {
                MessageBox.Show("当前没有可用于圆孔识别的图像。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double left = double.IsNaN(Canvas.GetLeft(ROIRect)) ? 0 : Canvas.GetLeft(ROIRect);
            double top = double.IsNaN(Canvas.GetTop(ROIRect)) ? 0 : Canvas.GetTop(ROIRect);
            var uiRect = new System.Windows.Rect(left, top, ROIRect.Width, ROIRect.Height);
            if (!Fredy.Drilling.Holes.Tools.VisionUIHelper.TryConvertUiRectToImageRect(sourceMat.Width, sourceMat.Height, DisplayImage.ActualWidth, DisplayImage.ActualHeight, uiRect, out var imageRect))
            {
                MessageBox.Show("ROI 无效，请重新框选圆孔区域。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Fredy.Drilling.Holes.Tools.VisionUIHelper.TryDetectDarkCircleInRoi(sourceMat, imageRect, out var detectedCircle) || detectedCircle == null)
            {
                MessageBox.Show("在 ROI 内未识别到稳定圆孔，请调整框选范围后重试。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _workpieceHoleOverlay = new WorkpieceHoleOverlayState(detectedCircle.Center, detectedCircle.Radius, imageRect, sourceMat.Width, sourceMat.Height);
            RedrawWorkpieceCalibrationOverlay();
            WorkpieceHoleCenterDetected?.Invoke(this, new WorkpieceHoleCenterDetectedEventArgs(detectedCircle.Center.X, detectedCircle.Center.Y, detectedCircle.Radius, sourceMat.Width, sourceMat.Height));
            SetOperationLog($"圆孔识别成功：像素圆心({detectedCircle.Center.X:F1}, {detectedCircle.Center.Y:F1})。", true);
        }

        private Mat? CreateWorkingMat()
        {
            if (ImageMat != null && !ImageMat.Empty())
            {
                return ImageMat.Clone();
            }

            if (ImageSource is BitmapSource bitmap)
            {
                return Tools.VisionUIHelper.BitmapSourceToMat(bitmap);
            }

            return null;
        }

        private void RedrawWorkpieceCalibrationOverlay()
        {
            CalibrationOverlayCanvas.Children.Clear();
            if (DisplayImage.ActualWidth <= 0 || DisplayImage.ActualHeight <= 0)
            {
                return;
            }

            if (_workpieceHoleOverlay != null)
            {
                var uiRect = Tools.VisionUIHelper.ConvertImageRectToUiRect(
                    _workpieceHoleOverlay.SourceWidth,
                    _workpieceHoleOverlay.SourceHeight,
                    DisplayImage.ActualWidth,
                    DisplayImage.ActualHeight,
                    _workpieceHoleOverlay.RoiRect);
                var uiCenter = Tools.VisionUIHelper.ConvertImagePointToUiPoint(
                    _workpieceHoleOverlay.SourceWidth,
                    _workpieceHoleOverlay.SourceHeight,
                    DisplayImage.ActualWidth,
                    DisplayImage.ActualHeight,
                    _workpieceHoleOverlay.PixelCenter);
                double uniformScale = Math.Min(DisplayImage.ActualWidth / _workpieceHoleOverlay.SourceWidth, DisplayImage.ActualHeight / _workpieceHoleOverlay.SourceHeight);
                double uiRadius = _workpieceHoleOverlay.PixelRadius * uniformScale;

                var roiRectangle = new Rectangle
                {
                    Width = uiRect.Width,
                    Height = uiRect.Height,
                    Stroke = Brushes.Gold,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 4 },
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(roiRectangle, uiRect.X);
                Canvas.SetTop(roiRectangle, uiRect.Y);
                CalibrationOverlayCanvas.Children.Add(roiRectangle);

                var ellipse = new Ellipse
                {
                    Width = uiRadius * 2,
                    Height = uiRadius * 2,
                    Stroke = Brushes.OrangeRed,
                    StrokeThickness = 2,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ellipse, uiCenter.X - uiRadius);
                Canvas.SetTop(ellipse, uiCenter.Y - uiRadius);
                CalibrationOverlayCanvas.Children.Add(ellipse);

                AddCrossMarker(uiCenter, Brushes.OrangeRed, 8, 2);
            }

            foreach (var overlayPoint in _workpieceEdgeOverlayPoints)
            {
                var uiPoint = Tools.VisionUIHelper.ConvertImagePointToUiPoint(
                    overlayPoint.SourceWidth,
                    overlayPoint.SourceHeight,
                    DisplayImage.ActualWidth,
                    DisplayImage.ActualHeight,
                    overlayPoint.PixelPoint);
                var marker = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = Brushes.DeepSkyBlue,
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(marker, uiPoint.X - 4);
                Canvas.SetTop(marker, uiPoint.Y - 4);
                CalibrationOverlayCanvas.Children.Add(marker);
                AddCrossMarker(uiPoint, Brushes.DeepSkyBlue, 6, 1.5);
            }
        }

        private void AddCrossMarker(System.Windows.Point center, Brush stroke, double size, double thickness)
        {
            var horizontal = new Line
            {
                X1 = center.X - size,
                Y1 = center.Y,
                X2 = center.X + size,
                Y2 = center.Y,
                Stroke = stroke,
                StrokeThickness = thickness,
                IsHitTestVisible = false
            };
            var vertical = new Line
            {
                X1 = center.X,
                Y1 = center.Y - size,
                X2 = center.X,
                Y2 = center.Y + size,
                Stroke = stroke,
                StrokeThickness = thickness,
                IsHitTestVisible = false
            };

            CalibrationOverlayCanvas.Children.Add(horizontal);
            CalibrationOverlayCanvas.Children.Add(vertical);
        }

        private void RefreshCenterRoiBinaryPreview()
        {
            if (!IsLoaded || !MenuCenterRoiBinaryPreview.IsChecked)
            {
                ClearCenterRoiBinaryPreview();
                return;
            }

            var config = _configService?.CurrentConfig;
            if (config == null || DisplayImage.ActualWidth <= 0 || DisplayImage.ActualHeight <= 0)
            {
                ClearCenterRoiBinaryPreview();
                return;
            }

            using var preview = BuildCenterRoiBinaryPreview(config);
            if (preview == null)
            {
                ClearCenterRoiBinaryPreview();
                return;
            }

            CenterRoiPreviewTitle.Text = $"中心ROI二值化 ({preview.RoiRect.Width}×{preview.RoiRect.Height})";
            CenterRoiPreviewImage.Source = Tools.VisionUIHelper.MatToBitmapSource(preview.BinaryImage);
            CenterRoiPreviewPanel.Visibility = Visibility.Visible;
            DrawCenterRoiOverlay(preview.RoiRect, preview.SourceWidth, preview.SourceHeight, CenterRoiPreviewImage.Source);
        }

        private Tools.CenterRoiBinaryPreviewResult? BuildCenterRoiBinaryPreview(Models.AppConfig config)
        {
            if (ImageMat != null && !ImageMat.Empty())
            {
                return Tools.VisionUIHelper.BuildCenterRoiBinaryPreview(
                    ImageMat,
                    config.CenterRoiWidth,
                    config.CenterRoiHeight,
                    config.CenterRoiThreshold,
                    config.CenterRoiBinaryInvert,
                    config.CenterRoiCircleRadius);
            }

            if (ImageSource is BitmapSource bitmap)
            {
                return Tools.VisionUIHelper.BuildCenterRoiBinaryPreview(
                    bitmap,
                    config.CenterRoiWidth,
                    config.CenterRoiHeight,
                    config.CenterRoiThreshold,
                    config.CenterRoiBinaryInvert,
                    config.CenterRoiCircleRadius);
            }

            return null;
        }

        private void DrawCenterRoiOverlay(OpenCvSharp.Rect roiRect, int sourceWidth, int sourceHeight, ImageSource? binarySource)
        {
            CenterRoiOverlayCanvas.Children.Clear();

            if (sourceWidth <= 0 || sourceHeight <= 0 || DisplayImage.ActualWidth <= 0 || DisplayImage.ActualHeight <= 0)
            {
                return;
            }

            double uniformScale = Math.Min(DisplayImage.ActualWidth / sourceWidth, DisplayImage.ActualHeight / sourceHeight);
            double renderedWidth = uniformScale * sourceWidth;
            double renderedHeight = uniformScale * sourceHeight;
            double offsetX = (DisplayImage.ActualWidth - renderedWidth) / 2.0;
            double offsetY = (DisplayImage.ActualHeight - renderedHeight) / 2.0;

            var uiRect = new System.Windows.Rect(
                offsetX + roiRect.X * uniformScale,
                offsetY + roiRect.Y * uniformScale,
                roiRect.Width * uniformScale,
                roiRect.Height * uniformScale);

            if (binarySource != null)
            {
                var overlayImage = new Image
                {
                    Width = uiRect.Width,
                    Height = uiRect.Height,
                    Source = binarySource,
                    Stretch = Stretch.Fill,
                    Opacity = 0.92,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(overlayImage, uiRect.X);
                Canvas.SetTop(overlayImage, uiRect.Y);
                CenterRoiOverlayCanvas.Children.Add(overlayImage);
            }

            var rectangle = new Rectangle
            {
                Width = uiRect.Width,
                Height = uiRect.Height,
                Stroke = Brushes.Gold,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(rectangle, uiRect.X);
            Canvas.SetTop(rectangle, uiRect.Y);
            CenterRoiOverlayCanvas.Children.Add(rectangle);
        }

        private void ClearCenterRoiBinaryPreview()
        {
            CenterRoiOverlayCanvas.Children.Clear();
            CenterRoiPreviewImage.Source = null;
            CenterRoiPreviewPanel.Visibility = Visibility.Collapsed;
        }
    }
}