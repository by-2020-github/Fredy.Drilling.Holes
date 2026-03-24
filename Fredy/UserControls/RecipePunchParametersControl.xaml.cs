using Fredy.Drilling.Holes.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fredy.Drilling.Holes.UserControls
{
    public sealed class PunchPointMapVisual
    {
        public required double Left { get; init; }

        public required double Top { get; init; }

        public required double Size { get; init; }

        public required Brush Fill { get; init; }

        public required Brush Stroke { get; init; }

        public required double StrokeThickness { get; init; }

        public required string ToolTip { get; init; }

        public required PunchPointViewModel Point { get; init; }
    }

    public partial class RecipePunchParametersControl : UserControl
    {
        private const double MapSize = 640;
        private const double MapPadding = 40;
        private const double MinMapZoom = 0.3;
        private const double MaxMapZoom = 8.0;
        private const double NormalPointSize = 12;
        private const double SelectedPointSize = 18;
        private static readonly Brush NormalPointBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243));
        private static readonly Brush CompletedPointBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        private static readonly Brush SelectedPointBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));
        private static readonly Brush PointStrokeBrush = Brushes.White;
        private static readonly Brush SelectedPointStrokeBrush = new SolidColorBrush(Color.FromRgb(230, 81, 0));

        public static readonly DependencyProperty RecipeViewModelProperty = DependencyProperty.Register(
            nameof(RecipeViewModel),
            typeof(RecipeViewModel),
            typeof(RecipePunchParametersControl),
            new PropertyMetadata(null, OnRecipeViewModelChanged));

        public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
            nameof(IsEditing),
            typeof(bool),
            typeof(RecipePunchParametersControl),
            new PropertyMetadata(false));

        private readonly MatrixTransform _mapTransform = new();
        private bool _isMapPanning;
        private Point _lastMapPanPoint;

        public RecipePunchParametersControl()
        {
            InitializeComponent();
            if (MapContentElement is not null)
            {
                MapContentElement.RenderTransform = _mapTransform;
            }

            Loaded += RecipePunchParametersControl_Loaded;
        }

        public ObservableCollection<PunchPointMapVisual> DisplayPoints { get; } = new();

        public RecipeViewModel? RecipeViewModel
        {
            get => (RecipeViewModel?)GetValue(RecipeViewModelProperty);
            set => SetValue(RecipeViewModelProperty, value);
        }

        public bool IsEditing
        {
            get => (bool)GetValue(IsEditingProperty);
            set => SetValue(IsEditingProperty, value);
        }

        private FrameworkElement? MapViewportElement => FindName("PunchPointMapViewport") as FrameworkElement;

        private FrameworkElement? MapContentElement => FindName("PunchPointMapContent") as FrameworkElement;

        private DataGrid? PunchPointsGrid => FindName("PunchPointsDataGrid") as DataGrid;

        private static void OnRecipeViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (RecipePunchParametersControl)d;

            if (e.OldValue is RecipeViewModel oldViewModel)
            {
                oldViewModel.PropertyChanged -= control.RecipeViewModel_PropertyChanged;
            }

            if (e.NewValue is RecipeViewModel newViewModel)
            {
                newViewModel.PropertyChanged += control.RecipeViewModel_PropertyChanged;
            }

            control.RefreshMap();
            control.SyncGridSelection();
            control.ResetMapView();
        }

        private void RecipeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RefreshMap();

            if (e.PropertyName == nameof(RecipeViewModel.SelectedPunchPoint))
            {
                SyncGridSelection();
            }
        }

        private void RefreshMap()
        {
            DisplayPoints.Clear();

            if (RecipeViewModel?.PunchPoints is not { Count: > 0 } punchPoints)
            {
                return;
            }

            var scaleRadius = GetScaleRadius();
            var drawableRadius = (MapSize / 2) - MapPadding;

            foreach (var point in punchPoints)
            {
                var isSelected = ReferenceEquals(point, RecipeViewModel.SelectedPunchPoint);
                var pointSize = isSelected ? SelectedPointSize : NormalPointSize;
                var centerX = (MapSize / 2) + ((point.X / scaleRadius) * drawableRadius);
                var centerY = (MapSize / 2) - ((point.Y / scaleRadius) * drawableRadius);

                DisplayPoints.Add(new PunchPointMapVisual
                {
                    Left = centerX - (pointSize / 2),
                    Top = centerY - (pointSize / 2),
                    Size = pointSize,
                    Fill = isSelected ? SelectedPointBrush : point.Complete ? CompletedPointBrush : NormalPointBrush,
                    Stroke = isSelected ? SelectedPointStrokeBrush : PointStrokeBrush,
                    StrokeThickness = isSelected ? 2.2 : 1.2,
                    ToolTip = $"圈:{point.RingNumber} 序号:{point.SequenceIndex} 坐标:({point.X:N3}, {point.Y:N3}) 状态:{(point.Complete ? "已完成" : "未完成")}",
                    Point = point
                });
            }
        }

        private double GetScaleRadius()
        {
            var maxPointRadius = RecipeViewModel?.PunchPoints
                .Select(x => Math.Sqrt((x.X * x.X) + (x.Y * x.Y)))
                .DefaultIfEmpty(1)
                .Max() ?? 1;

            var recipeRadius = RecipeViewModel?.PunchParameters.Radius ?? 0;
            return Math.Max(1, Math.Max(recipeRadius, maxPointRadius));
        }

        private void SyncGridSelection()
        {
            if (PunchPointsGrid is null)
            {
                return;
            }

            PunchPointsGrid.SelectedItem = RecipeViewModel?.SelectedPunchPoint;

            if (RecipeViewModel?.SelectedPunchPoint is not null)
            {
                PunchPointsGrid.ScrollIntoView(RecipeViewModel.SelectedPunchPoint);
            }
        }

        private void MapPoint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not PunchPointMapVisual mapPoint)
            {
                return;
            }

            if (RecipeViewModel is not null)
            {
                RecipeViewModel.SelectedPunchPoint = mapPoint.Point;
            }

            e.Handled = true;
        }

        private void PunchPointsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RecipeViewModel is null)
            {
                return;
            }

            if (!ReferenceEquals(RecipeViewModel.SelectedPunchPoint, PunchPointsGrid?.SelectedItem))
            {
                RecipeViewModel.SelectedPunchPoint = PunchPointsGrid?.SelectedItem as PunchPointViewModel;
            }
        }

        private void PunchPointMapViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (RecipeViewModel?.PunchPoints.Count is not > 0)
            {
                return;
            }

            _isMapPanning = true;
            _lastMapPanPoint = e.GetPosition(MapViewportElement);
            MapViewportElement?.CaptureMouse();
            Mouse.OverrideCursor = Cursors.SizeAll;
        }

        private void PunchPointMapViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isMapPanning)
            {
                return;
            }

            var currentPoint = e.GetPosition(MapViewportElement);
            var delta = currentPoint - _lastMapPanPoint;
            var matrix = _mapTransform.Matrix;
            matrix.Translate(delta.X, delta.Y);
            _mapTransform.Matrix = matrix;
            _lastMapPanPoint = currentPoint;
        }

        private void PunchPointMapViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndMapPan();
        }

        private void PunchPointMapViewport_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndMapPan();
        }

        private void PunchPointMapViewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (RecipeViewModel?.PunchPoints.Count is not > 0 || MapContentElement is null)
            {
                return;
            }

            var zoomFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            var matrix = _mapTransform.Matrix;
            var currentZoom = matrix.M11;
            var targetZoom = currentZoom * zoomFactor;

            if (targetZoom < MinMapZoom)
            {
                zoomFactor = MinMapZoom / currentZoom;
            }
            else if (targetZoom > MaxMapZoom)
            {
                zoomFactor = MaxMapZoom / currentZoom;
            }

            var zoomCenter = e.GetPosition(MapContentElement);
            matrix.ScaleAt(zoomFactor, zoomFactor, zoomCenter.X, zoomCenter.Y);
            _mapTransform.Matrix = matrix;
            e.Handled = true;
        }

        private void PunchPointMapViewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isMapPanning)
            {
                ResetMapView(); // 隐患点
            }
        }

        private void ResetMapViewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ResetMapView();
        }

        private void FitAllPointsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            FitAllPoints();
        }

        private void EndMapPan()
        {
            if (!_isMapPanning)
            {
                return;
            }

            _isMapPanning = false;
            MapViewportElement?.ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
        }

        private void ResetMapView()
        {
            ApplyFitToBounds(new Rect(0, 0, MapSize, MapSize));
        }

        private void FitAllPoints()
        {
            if (DisplayPoints.Count == 0)
            {
                ResetMapView();
                return;
            }

            var center = MapSize / 2.0; // 物理圆心 (320)
            var maxDistance = 0.0;

            // 寻找距离物理圆心最远的边界距离
            foreach (var point in DisplayPoints)
            {
                var distLeft = Math.Abs(center - point.Left);
                var distRight = Math.Abs(point.Left + point.Size - center);
                var distTop = Math.Abs(center - point.Top);
                var distBottom = Math.Abs(point.Top + point.Size - center);

                var pointMax = Math.Max(Math.Max(distLeft, distRight), Math.Max(distTop, distBottom));
                if (pointMax > maxDistance)
                {
                    maxDistance = pointMax;
                }
            }

            // 加上 36 像素的安全边距
            maxDistance += 36;
            var size = maxDistance * 2;

            // 生成一个完美以 (320, 320) 为中心的对称正方形包围盒
            var symmetricBounds = new Rect(center - maxDistance, center - maxDistance, size, size);

            // 应用变换，无需 Rect.Intersect，矩阵会自动处理完美的等比缩放和居中
            ApplyFitToBounds(symmetricBounds);
        }
        private void ApplyFitToBounds(Rect contentBounds)
        {
            var viewport = MapViewportElement;
            if (viewport is null || viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0)
            {
                return;
            }

            var width = Math.Max(1, contentBounds.Width);
            var height = Math.Max(1, contentBounds.Height);
            var scale = Math.Min(viewport.ActualWidth / width, viewport.ActualHeight / height);
            scale = Math.Max(scale, 0.01);

            var offsetX = ((viewport.ActualWidth - (width * scale)) / 2) - (contentBounds.X * scale);
            var offsetY = ((viewport.ActualHeight - (height * scale)) / 2) - (contentBounds.Y * scale);

            _mapTransform.Matrix = new Matrix(scale, 0, 0, scale, offsetX, offsetY);
        }

        private void RecipePunchParametersControl_Loaded(object sender, RoutedEventArgs e)
        {
            ResetMapView();
        }
    }
}
