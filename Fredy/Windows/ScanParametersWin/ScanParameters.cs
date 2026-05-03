using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace Fredy.Drilling.Holes.Models
{
    public class ScanGridCellVisual : ObservableObject
    {
        private int _shotIndex;
        private int _row;
        private int _column;
        private int _orderIndex;
        private double _left;
        private double _top;
        private double _width;
        private double _height;
        private bool _isScanned;

        public int ShotIndex
        {
            get => _shotIndex;
            set => SetProperty(ref _shotIndex, value);
        }

        public int Row
        {
            get => _row;
            set => SetProperty(ref _row, value);
        }

        public int Column
        {
            get => _column;
            set => SetProperty(ref _column, value);
        }

        public int OrderIndex
        {
            get => _orderIndex;
            set
            {
                if (SetProperty(ref _orderIndex, value))
                {
                    OnPropertyChanged(nameof(OrderLabel));
                }
            }
        }

        public string OrderLabel => OrderIndex <= 0 ? string.Empty : OrderIndex.ToString();

        public double Left
        {
            get => _left;
            set => SetProperty(ref _left, value);
        }

        public double Top
        {
            get => _top;
            set => SetProperty(ref _top, value);
        }

        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        public bool IsScanned
        {
            get => _isScanned;
            set
            {
                if (SetProperty(ref _isScanned, value))
                {
                    OnPropertyChanged(nameof(Stroke));
                    OnPropertyChanged(nameof(LabelBrush));
                }
            }
        }

        public Brush Stroke => IsScanned ? Brushes.LimeGreen : Brushes.Gold;

        public Brush LabelBrush => IsScanned ? Brushes.LimeGreen : Brushes.Gold;
    }

    public class DetectedHoleInfo : ObservableObject
    {
        public int ImageIndex { get; set; }
        public double PixelX { get; set; }
        public double PixelY { get; set; }
        public double PixelSize { get; set; } // 直径(像素)
        public double PhysicalX { get; set; }
        public double PhysicalY { get; set; }
        public double PhysicalSize { get; set; } // 直径(mm)
    }

    public class ScanParameters : ObservableObject
    {
        private string _workpieceType = "PS60-6X500...";
        private double _workpieceDiameter = 42.7;
        private double _fovSize = 5;
        private double _scanExpand = 5;
        private int _settleTime = 200;
        private double _overlapXPercent = 10;
        private double _overlapYPercent = 10;
        private int _rowCount;
        private int _columnCount;
        private int _totalShots;
        private double _actualOverlapXPercent;
        private double _actualOverlapYPercent;
        private string _scanStatus = "等待计算...";
        private int _photoIndex;
        private double _currentX;
        private double _currentY;
        private double _progressValue;
        private double _detectMinRadius = 15;
        private double _detectMaxRadius = 25;
        private double _detectParam1 = 50;
        private double _detectParam2 = 25;
        private bool _detectIsDarkHole = true;
        private double _deduplicateToleranceMm = 0.08;

        public string WorkpieceType { get => _workpieceType; set => SetProperty(ref _workpieceType, value); }

        public double WorkpieceDiameter { get => _workpieceDiameter; set => SetProperty(ref _workpieceDiameter, value); }

        public double FovSize { get => _fovSize; set => SetProperty(ref _fovSize, value); }

        public double ScanExpand { get => _scanExpand; set => SetProperty(ref _scanExpand, value); }

        public int SettleTime { get => _settleTime; set => SetProperty(ref _settleTime, value); }

        public double OverlapXPercent { get => _overlapXPercent; set => SetProperty(ref _overlapXPercent, value); }

        public double OverlapYPercent { get => _overlapYPercent; set => SetProperty(ref _overlapYPercent, value); }

        public int RowCount { get => _rowCount; set => SetProperty(ref _rowCount, value); }

        public int ColumnCount { get => _columnCount; set => SetProperty(ref _columnCount, value); }

        public int TotalShots { get => _totalShots; set => SetProperty(ref _totalShots, value); }

        public double ActualOverlapXPercent { get => _actualOverlapXPercent; set => SetProperty(ref _actualOverlapXPercent, value); }

        public double ActualOverlapYPercent { get => _actualOverlapYPercent; set => SetProperty(ref _actualOverlapYPercent, value); }

        public string ScanStatus { get => _scanStatus; set => SetProperty(ref _scanStatus, value); }

        public int PhotoIndex { get => _photoIndex; set => SetProperty(ref _photoIndex, value); }

        public double CurrentX { get => _currentX; set => SetProperty(ref _currentX, value); }

        public double CurrentY { get => _currentY; set => SetProperty(ref _currentY, value); }

        public double ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

        public double DetectMinRadius { get => _detectMinRadius; set => SetProperty(ref _detectMinRadius, value); }

        public double DetectMaxRadius { get => _detectMaxRadius; set => SetProperty(ref _detectMaxRadius, value); }

        public double DetectParam1 { get => _detectParam1; set => SetProperty(ref _detectParam1, value); }

        public double DetectParam2 { get => _detectParam2; set => SetProperty(ref _detectParam2, value); }

        public bool DetectIsDarkHole { get => _detectIsDarkHole; set => SetProperty(ref _detectIsDarkHole, value); }

        public double DeduplicateToleranceMm { get => _deduplicateToleranceMm; set => SetProperty(ref _deduplicateToleranceMm, value); }
    }
}