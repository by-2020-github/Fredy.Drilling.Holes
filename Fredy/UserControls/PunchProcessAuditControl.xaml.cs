using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fredy.Drilling.Holes.UserControls
{
    /// <summary>
    /// PunchProcessAuditControl.xaml 的交互逻辑
    /// </summary>
    public partial class PunchProcessAuditControl : UserControl
    {
        private readonly ScaleTransform _scaleTransform = new(1.0, 1.0);
        private readonly TranslateTransform _translateTransform = new(0.0, 0.0);
        private Point _dragStartPoint;
        private Point _dragStartOffset;
        private bool _isDragging;

        public PunchProcessAuditControl()
        {
            InitializeComponent();

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(_scaleTransform);
            transformGroup.Children.Add(_translateTransform);
            HeatmapContentLayer.RenderTransform = transformGroup;
        }

        private void HeatmapViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            const double minScale = 0.2;
            const double maxScale = 10.0;
            const double zoomFactor = 1.15;

            double factor = e.Delta > 0 ? zoomFactor : 1 / zoomFactor;
            double targetScale = Math.Clamp(_scaleTransform.ScaleX * factor, minScale, maxScale);
            if (Math.Abs(targetScale - _scaleTransform.ScaleX) < 0.0001)
            {
                return;
            }

            Point cursor = e.GetPosition(HeatmapViewport);
            double contentX = (cursor.X - _translateTransform.X) / _scaleTransform.ScaleX;
            double contentY = (cursor.Y - _translateTransform.Y) / _scaleTransform.ScaleY;

            _scaleTransform.ScaleX = targetScale;
            _scaleTransform.ScaleY = targetScale;
            _translateTransform.X = cursor.X - contentX * targetScale;
            _translateTransform.Y = cursor.Y - contentY * targetScale;

            e.Handled = true;
        }

        private void HeatmapViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStartPoint = e.GetPosition(HeatmapViewport);
            _dragStartOffset = new Point(_translateTransform.X, _translateTransform.Y);
            HeatmapViewport.CaptureMouse();
            Cursor = Cursors.Hand;
            e.Handled = true;
        }

        private void HeatmapViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            Point current = e.GetPosition(HeatmapViewport);
            Vector delta = current - _dragStartPoint;
            _translateTransform.X = _dragStartOffset.X + delta.X;
            _translateTransform.Y = _dragStartOffset.Y + delta.Y;
        }

        private void HeatmapViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndDrag();
            e.Handled = true;
        }

        private void HeatmapViewport_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndDrag();
        }

        private void EndDrag()
        {
            _isDragging = false;
            if (HeatmapViewport.IsMouseCaptured)
            {
                HeatmapViewport.ReleaseMouseCapture();
            }

            Cursor = Cursors.Arrow;
        }
    }
}
