using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Fredy.Drilling.Holes.UserControls
{
    public partial class NinePointCalibrationControl : UserControl
    {
        public static readonly DependencyProperty PixelSizeXProperty = DependencyProperty.Register(
            nameof(PixelSizeX), typeof(double), typeof(NinePointCalibrationControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPixelSizeChanged));

        public static readonly DependencyProperty PixelSizeYProperty = DependencyProperty.Register(
            nameof(PixelSizeY), typeof(double), typeof(NinePointCalibrationControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPixelSizeChanged));

        private readonly NinePointCalibrationViewModel _viewModel;

        public NinePointCalibrationControl()
        {
            InitializeComponent();
            _viewModel = new NinePointCalibrationViewModel();
            _viewModel.ApplyRequested += OnApplyRequested;
            DataContext = _viewModel;
            Loaded += (_, _) => SyncFromDependencyProperties();
        }

        public double PixelSizeX
        {
            get => (double)GetValue(PixelSizeXProperty);
            set => SetValue(PixelSizeXProperty, value);
        }

        public double PixelSizeY
        {
            get => (double)GetValue(PixelSizeYProperty);
            set => SetValue(PixelSizeYProperty, value);
        }

        private static void OnPixelSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((NinePointCalibrationControl)d).SyncFromDependencyProperties();
        }

        private void SyncFromDependencyProperties()
        {
            _viewModel.ManualPixelSizeX = PixelSizeX;
            _viewModel.ManualPixelSizeY = PixelSizeY;
        }

        private void OnApplyRequested(double pixelSizeX, double pixelSizeY)
        {
            PixelSizeX = pixelSizeX;
            PixelSizeY = pixelSizeY;
        }

        private sealed partial class NinePointCalibrationPoint : ObservableObject
        {
            [ObservableProperty] private int _index;
            [ObservableProperty] private double _pixelX;
            [ObservableProperty] private double _pixelY;
        }

        private sealed partial class NinePointCalibrationViewModel : ObservableObject
        {
            private readonly int[][] _gridRows =
            [
                [0,1,2],
                [3,4,5],
                [6,7,8]
            ];

            public event Action<double, double>? ApplyRequested;

            [ObservableProperty] private bool _isManualMode = true;
            [ObservableProperty] private double _manualPixelSizeX;
            [ObservableProperty] private double _manualPixelSizeY;
            [ObservableProperty] private double _stagePitchX = 1;
            [ObservableProperty] private double _stagePitchY = 1;
            [ObservableProperty] private string _calibrationResultText = "";

            public ObservableCollection<NinePointCalibrationPoint> Points { get; } = new();

            public Visibility ManualPanelVisibility => IsManualMode ? Visibility.Visible : Visibility.Collapsed;

            public Visibility FieldPanelVisibility => IsManualMode ? Visibility.Collapsed : Visibility.Visible;

            public bool IsFieldMode
            {
                get => !IsManualMode;
                set
                {
                    if (value)
                    {
                        IsManualMode = false;
                    }
                }
            }

            public NinePointCalibrationViewModel()
            {
                ResetPoints();
            }

            partial void OnIsManualModeChanged(bool value)
            {
                OnPropertyChanged(nameof(ManualPanelVisibility));
                OnPropertyChanged(nameof(FieldPanelVisibility));
                OnPropertyChanged(nameof(IsFieldMode));
            }

            [RelayCommand]
            private void ApplyManual()
            {
                if (ManualPixelSizeX <= 0 || ManualPixelSizeY <= 0)
                {
                    CalibrationResultText = "手动输入值必须大于 0";
                    return;
                }

                ApplyRequested?.Invoke(ManualPixelSizeX, ManualPixelSizeY);
                CalibrationResultText = $"已应用手动值 X={ManualPixelSizeX:F4}, Y={ManualPixelSizeY:F4} μm";
            }

            [RelayCommand]
            private void ResetPoints()
            {
                Points.Clear();
                for (int i = 1; i <= 9; i++)
                {
                    Points.Add(new NinePointCalibrationPoint { Index = i, PixelX = 0, PixelY = 0 });
                }

                CalibrationResultText = "请输入九点对应像素坐标。";
            }

            [RelayCommand]
            private void CalculateAndApply()
            {
                if (StagePitchX <= 0 || StagePitchY <= 0)
                {
                    CalibrationResultText = "物理间距必须大于 0";
                    return;
                }

                var xDiffs = _gridRows
                    .SelectMany(row => new[]
                    {
                        Math.Abs(Points[row[1]].PixelX - Points[row[0]].PixelX),
                        Math.Abs(Points[row[2]].PixelX - Points[row[1]].PixelX)
                    })
                    .Where(v => v > 0)
                    .ToList();

                var yDiffs = Enumerable.Range(0, 3)
                    .SelectMany(col => new[]
                    {
                        Math.Abs(Points[_gridRows[1][col]].PixelY - Points[_gridRows[0][col]].PixelY),
                        Math.Abs(Points[_gridRows[2][col]].PixelY - Points[_gridRows[1][col]].PixelY)
                    })
                    .Where(v => v > 0)
                    .ToList();

                if (xDiffs.Count == 0 || yDiffs.Count == 0)
                {
                    CalibrationResultText = "九点像素坐标无效，请检查输入。";
                    return;
                }

                var avgPixelStepX = xDiffs.Average();
                var avgPixelStepY = yDiffs.Average();

                var pixelSizeXUm = (StagePitchX / avgPixelStepX) * 1000.0;
                var pixelSizeYUm = (StagePitchY / avgPixelStepY) * 1000.0;

                ManualPixelSizeX = pixelSizeXUm;
                ManualPixelSizeY = pixelSizeYUm;
                ApplyRequested?.Invoke(pixelSizeXUm, pixelSizeYUm);
                CalibrationResultText = $"标定完成 X={pixelSizeXUm:F4}, Y={pixelSizeYUm:F4} μm";
            }
        }
    }
}
