using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Fredy.Drilling.Holes.Views
{
    /// <summary>
    /// MainView.xaml 的交互逻辑
    /// </summary>
    public partial class MainView : UserControl
    {
        private const double MinCameraZoom = 0.2;
        private const double MaxCameraZoom = 8.0;
        private readonly MatrixTransform _cameraImageTransform = new();
        private bool _isCameraPanning;
        private Point _lastCameraPanPoint;

        public MainView()
        {
            InitializeComponent();
            CameraContentLayer.RenderTransform = _cameraImageTransform;
            SetCenterCrossVisibility(true);
        }

        private void CameraViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CameraImage.Source is null)
            {
                return;
            }

            _isCameraPanning = true;
            _lastCameraPanPoint = e.GetPosition(CameraViewport);
            CameraViewport.CaptureMouse();
            Mouse.OverrideCursor = Cursors.SizeAll;
        }

        private void CameraViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndCameraPan();
        }

        private void CameraViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isCameraPanning)
            {
                return;
            }

            var currentPoint = e.GetPosition(CameraViewport);
            var delta = currentPoint - _lastCameraPanPoint;
            var matrix = _cameraImageTransform.Matrix;
            matrix.Translate(delta.X, delta.Y);
            _cameraImageTransform.Matrix = matrix;
            _lastCameraPanPoint = currentPoint;
        }

        private void CameraViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (CameraImage.Source is null)
            {
                return;
            }

            var zoomFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            var matrix = _cameraImageTransform.Matrix;
            var currentZoom = matrix.M11;
            var targetZoom = currentZoom * zoomFactor;

            if (targetZoom < MinCameraZoom)
            {
                zoomFactor = MinCameraZoom / currentZoom;
            }
            else if (targetZoom > MaxCameraZoom)
            {
                zoomFactor = MaxCameraZoom / currentZoom;
            }

            var zoomCenter = e.GetPosition(CameraImage);
            matrix.ScaleAt(zoomFactor, zoomFactor, zoomCenter.X, zoomCenter.Y);
            _cameraImageTransform.Matrix = matrix;
            e.Handled = true;
        }

        private void CameraViewport_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndCameraPan();
        }

        private void EndCameraPan()
        {
            if (!_isCameraPanning)
            {
                return;
            }

            _isCameraPanning = false;
            CameraViewport.ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
        }

        private void ResetCameraViewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _cameraImageTransform.Matrix = Matrix.Identity;
        }

        private void SaveCameraImageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (CameraImage.Source is null)
            {
                MessageBox.Show("当前没有可保存的图像。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "保存图像",
                Filter = "PNG 图像|*.png|JPEG 图像|*.jpg|BMP 图像|*.bmp",
                DefaultExt = ".png",
                FileName = $"Camera_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var bitmapSource = GetBitmapSource(CameraImage.Source);
                if (bitmapSource is null)
                {
                    MessageBox.Show("当前图像格式暂不支持保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                BitmapEncoder encoder = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
                {
                    ".jpg" => new JpegBitmapEncoder { QualityLevel = 95 },
                    ".bmp" => new BmpBitmapEncoder(),
                    _ => new PngBitmapEncoder()
                };

                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

                using var stream = System.IO.File.Create(dialog.FileName);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存图像失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleCenterCrossMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SetCenterCrossVisibility(ToggleCenterCrossMenuItem.IsChecked);
        }

        private void SetCenterCrossVisibility(bool isVisible)
        {
            CameraCenterCross.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private static BitmapSource? GetBitmapSource(ImageSource imageSource)
        {
            return imageSource switch
            {
                BitmapSource bitmapSource => bitmapSource,
                _ => null
            };
        }

        private void LogListBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (LogListBox.Items is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged -= LogItems_CollectionChanged;
                collection.CollectionChanged += LogItems_CollectionChanged;
            }

            ScrollLogsToEnd();
        }

        private void LogListBox_Unloaded(object sender, RoutedEventArgs e)
        {
            if (LogListBox.Items is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged -= LogItems_CollectionChanged;
            }
        }

        private void LogItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
            {
                ScrollLogsToEnd();
            }
        }

        private void ScrollLogsToEnd()
        {
            if (LogListBox.Items.Count == 0)
            {
                return;
            }

            var lastItem = LogListBox.Items[^1];
            Dispatcher.BeginInvoke(() => LogListBox.ScrollIntoView(lastItem), DispatcherPriority.Background);
        }
    }
}
