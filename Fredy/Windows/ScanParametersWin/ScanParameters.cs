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
        private int _detectMinArea = 3;
        private int _detectMaxArea = 600;
        private int _detectThreshold = 95;
        private double _detectCircularity = 0.5;
        private int _detectMorphologySize = 13;
        private double _deduplicateToleranceMm = 0.08;
        private bool _useBrightFieldDetector = true;

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

        public int DetectMinArea { get => _detectMinArea; set => SetProperty(ref _detectMinArea, value); }

        public int DetectMaxArea { get => _detectMaxArea; set => SetProperty(ref _detectMaxArea, value); }

        public int DetectThreshold { get => _detectThreshold; set => SetProperty(ref _detectThreshold, value); }

        public double DetectCircularity { get => _detectCircularity; set => SetProperty(ref _detectCircularity, value); }

        public int DetectMorphologySize { get => _detectMorphologySize; set => SetProperty(ref _detectMorphologySize, value); }

        public double DeduplicateToleranceMm { get => _deduplicateToleranceMm; set => SetProperty(ref _deduplicateToleranceMm, value); }

        public bool UseBrightFieldDetector { get => _useBrightFieldDetector; set => SetProperty(ref _useBrightFieldDetector, value); }
    }
}