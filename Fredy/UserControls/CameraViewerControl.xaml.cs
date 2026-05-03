using System;
using System.ComponentModel;
using System.Threading;
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

namespace Fredy.Drilling.Holes.UserControls
{
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

        private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CameraViewerControl control && control.MenuDetectCircles.IsChecked)
            {
                using var result = control.RunCircleDetection();
            }
        }

        private readonly ICamera? _camera;
        private CancellationTokenSource? _grabCts;
        private Task? _grabLoopTask;

        private Point _startDragPos;
        private bool _isDragging = false;
        private bool _isDrawingROI = false;
        private bool _isDraggingROI = false;

        public CameraViewerControl()
        {
            InitializeComponent();

            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                _camera = App.ServiceProvider.GetRequiredService<ICamera>();
            }

            Unloaded += CameraViewerControl_Unloaded;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDrawingROI)
            {
                var pos = e.GetPosition(ROICanvas);
                Canvas.SetLeft(ROIRect, pos.X);
                Canvas.SetTop(ROIRect, pos.Y);
                ROIRect.Width = 0;
                ROIRect.Height = 0;
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
            _isDragging = false;
            _isDrawingROI = false;
            Viewport.ReleaseMouseCapture();
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
                Point pos = e.GetPosition(this);
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

        private async void GrabSingle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureCameraOpenAsync();
                if (_camera is null)
                {
                    return;
                }

                var frame = await _camera.GrabAsync();
                UpdateImage(frame);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"单张拍照失败：{ex.Message}", "相机", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StartContinuousGrab_Click(object sender, RoutedEventArgs e)
        {
            if (_grabCts is not null)
            {
                return;
            }

            try
            {
                await EnsureCameraOpenAsync();

                _grabCts = new CancellationTokenSource();
                var token = _grabCts.Token;

                _grabLoopTask = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        CameraArgs frame;
                        try
                        {
                            frame = await _camera.GrabAsync();
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        await Dispatcher.InvokeAsync(() => UpdateImage(frame));

                        try
                        {
                            await Task.Delay(33, token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }, token);
            }
            catch (Exception ex)
            {
                await StopContinuousGrabAsync();
                MessageBox.Show($"启动连续拍照失败：{ex.Message}", "相机", MessageBoxButton.OK, MessageBoxImage.Error);
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
        }

        private async Task StopContinuousGrabAsync()
        {
            if (_grabCts is null)
            {
                return;
            }

            _grabCts.Cancel();

            if (_grabLoopTask is not null)
            {
                try
                {
                    await _grabLoopTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _grabLoopTask = null;
            _grabCts.Dispose();
            _grabCts = null;
        }

        private async void CameraViewerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            await StopContinuousGrabAsync();
        }

        private void StartDrawROI_Click(object sender, RoutedEventArgs e)
        {
            _isDrawingROI = true;
            ROICanvas.Visibility = Visibility.Visible;
            OverlayCanvas.Children.Clear();
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
            dialog.Owner = Window.GetWindow(this);
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
                    Rect roi = new Rect(left, top, ROIRect.Width, ROIRect.Height);
                    
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
            ROICanvas.Width = e.NewSize.Width;
            ROICanvas.Height = e.NewSize.Height;
        }
    }
}