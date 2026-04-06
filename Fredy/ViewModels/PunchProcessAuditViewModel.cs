using BLL;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Fredy.Drilling.Holes.ViewModels
{
    public class PunchHoleAuditItemViewModel : ObservableObject
    {
        private int _holeIndex;
        private int _ringNumber;
        private int _sequenceIndex;
        private double _targetX;
        private double _targetY;
        private string _state = "待加工";
        private string _compensation = "-";
        private string _nearestSample = "-";
        private bool _isCompleted;

        public int HoleIndex { get => _holeIndex; set => SetProperty(ref _holeIndex, value); }
        public int RingNumber { get => _ringNumber; set => SetProperty(ref _ringNumber, value); }
        public int SequenceIndex { get => _sequenceIndex; set => SetProperty(ref _sequenceIndex, value); }
        public double TargetX { get => _targetX; set => SetProperty(ref _targetX, value); }
        public double TargetY { get => _targetY; set => SetProperty(ref _targetY, value); }
        public string State { get => _state; set => SetProperty(ref _state, value); }
        public string Compensation { get => _compensation; set => SetProperty(ref _compensation, value); }
        public string NearestSample { get => _nearestSample; set => SetProperty(ref _nearestSample, value); }
        public bool IsCompleted { get => _isCompleted; set => SetProperty(ref _isCompleted, value); }
    }

    public class PunchHeatmapPointViewModel : ObservableObject
    {
        private double _left;
        private double _top;
        private double _size;
        private Brush _fill = Brushes.Gray;
        private Brush _stroke = Brushes.Transparent;
        private double _strokeThickness;
        private string _toolTip = string.Empty;
        private double _canvasX;
        private double _canvasY;

        public int HoleIndex { get; init; }
        public int RingNumber { get; init; }
        public int SequenceIndex { get; init; }
        public double X { get; init; }
        public double Y { get; init; }

        public double Left { get => _left; set => SetProperty(ref _left, value); }
        public double Top { get => _top; set => SetProperty(ref _top, value); }
        public double Size { get => _size; set => SetProperty(ref _size, value); }
        public Brush Fill { get => _fill; set => SetProperty(ref _fill, value); }
        public Brush Stroke { get => _stroke; set => SetProperty(ref _stroke, value); }
        public double StrokeThickness { get => _strokeThickness; set => SetProperty(ref _strokeThickness, value); }
        public string ToolTip { get => _toolTip; set => SetProperty(ref _toolTip, value); }
        public double CanvasX { get => _canvasX; set => SetProperty(ref _canvasX, value); }
        public double CanvasY { get => _canvasY; set => SetProperty(ref _canvasY, value); }
    }

    public class PunchProcessAuditViewModel : ObservableObject
    {
        private const int MaxLogs = 200;
        private const double HeatmapWidth = 940;
        private const double HeatmapHeight = 230;
        private const double HeatmapPadding = 16;

        private string _title = "冲孔流程审计";
        private string _processMode = "-";
        private string _currentState = "-";
        private string _completionStatus = "进行中";
        private int _totalHoles;
        private int _completedHoles;
        private double _progressPercent;
        private int _currentHoleIndex = -1;
        private int _nearestHoleIndex = -1;
        private double _linkX1;
        private double _linkY1;
        private double _linkX2;
        private double _linkY2;
        private Visibility _linkVisibility = Visibility.Collapsed;

        private readonly Dictionary<int, PunchHeatmapPointViewModel> _heatmapPointsByHoleIndex = new();

        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public string ProcessMode { get => _processMode; set => SetProperty(ref _processMode, value); }
        public string CurrentState { get => _currentState; set => SetProperty(ref _currentState, value); }
        public string CompletionStatus { get => _completionStatus; set => SetProperty(ref _completionStatus, value); }
        public int TotalHoles { get => _totalHoles; set => SetProperty(ref _totalHoles, value); }
        public int CompletedHoles { get => _completedHoles; set => SetProperty(ref _completedHoles, value); }
        public double ProgressPercent { get => _progressPercent; set => SetProperty(ref _progressPercent, value); }
        public double LinkX1 { get => _linkX1; set => SetProperty(ref _linkX1, value); }
        public double LinkY1 { get => _linkY1; set => SetProperty(ref _linkY1, value); }
        public double LinkX2 { get => _linkX2; set => SetProperty(ref _linkX2, value); }
        public double LinkY2 { get => _linkY2; set => SetProperty(ref _linkY2, value); }
        public Visibility LinkVisibility { get => _linkVisibility; set => SetProperty(ref _linkVisibility, value); }

        public ObservableCollection<PunchHoleAuditItemViewModel> HoleItems { get; } = new();

        public ObservableCollection<PunchHeatmapPointViewModel> HeatmapPoints { get; } = new();

        public ObservableCollection<string> Timeline { get; } = new();

        public void Initialize(RecipeViewModel recipeViewModel, bool isSimulation, bool isFirstPass)
        {
            HoleItems.Clear();
            HeatmapPoints.Clear();
            _heatmapPointsByHoleIndex.Clear();
            Timeline.Clear();

            ProcessMode = $"{(isSimulation ? "Debug模拟" : "实际")}-{(isFirstPass ? "头道" : "二道")}";
            CurrentState = PunchState.ReadCoordinate.ToString();
            CompletionStatus = "进行中";
            CompletedHoles = 0;
            _currentHoleIndex = 1;
            _nearestHoleIndex = -1;

            for (int i = 0; i < recipeViewModel.PunchPoints.Count; i++)
            {
                var point = recipeViewModel.PunchPoints[i];
                HoleItems.Add(new PunchHoleAuditItemViewModel
                {
                    HoleIndex = i + 1,
                    RingNumber = point.RingNumber,
                    SequenceIndex = point.SequenceIndex,
                    TargetX = point.X,
                    TargetY = point.Y,
                    State = "待加工",
                    Compensation = "-",
                    NearestSample = "-",
                    IsCompleted = false
                });
            }

            TotalHoles = HoleItems.Count;
            BuildHeatmapPoints();
            RefreshHeatmapStyles();
            UpdateProgress();
            AddTimeline($"流程初始化，孔位总数: {TotalHoles}");
        }

        public void OnStateChanged(StateChangedEventArgs args)
        {
            CurrentState = args.NewState.ToString();

            int previousHoleIndex = _currentHoleIndex;
            _currentHoleIndex = args.CurrentHoleIndex;

            // 切换到新孔位时，清空上一孔位留下的最近邻高亮，
            // 避免出现“当前孔 n 仍连接到 n-2，随后才跳到 n-1”的视觉误导。
            if (previousHoleIndex != _currentHoleIndex)
            {
                _nearestHoleIndex = -1;
            }

            var hole = HoleItems.FirstOrDefault(x => x.HoleIndex == args.CurrentHoleIndex);
            if (hole is not null)
            {
                hole.State = args.NewState.ToString();
            }

            RefreshHeatmapStyles();
            AddTimeline($"状态切换: 孔#{args.CurrentHoleIndex} {args.OldState} -> {args.NewState}");
        }

        public void OnMessage(MessageEventArgs args)
        {
            var level = args.IsAlarm ? "ALARM" : "INFO";
            AddTimeline($"{level}: {args.Message}");
        }

        public void OnCompensationSelected(CompensationSelectedEventArgs args)
        {
            var hole = HoleItems.FirstOrDefault(x => x.HoleIndex == args.HoleIndex);
            if (hole is null)
            {
                return;
            }

            hole.Compensation = args.Compensation.ToString("F4");
            hole.NearestSample = args.HasNearestSample
                ? $"({args.NearestSampleX:F3}, {args.NearestSampleY:F3}), Z={args.NearestSampleSurfaceZ:F4}, D={args.NearestDistance:F3}, N={args.SampleCount}"
                : $"无采样点, N={args.SampleCount}";

            _nearestHoleIndex = args.HasNearestSample
                ? ResolveNearestHoleIndex(args.NearestSampleX, args.NearestSampleY)
                : -1;

            RefreshHeatmapStyles();
            AddTimeline($"孔#{args.HoleIndex} 补偿={args.Compensation:F4}，最近邻={hole.NearestSample}");
        }

        public void MarkHoleCompleted(int holeIndex)
        {
            var hole = HoleItems.FirstOrDefault(x => x.HoleIndex == holeIndex);
            if (hole is null)
            {
                return;
            }

            hole.IsCompleted = true;
            hole.State = "已完成";
            CompletedHoles = HoleItems.Count(x => x.IsCompleted);
            RefreshHeatmapStyles();
            UpdateProgress();
            AddTimeline($"孔#{holeIndex} 完成，整体进度 {CompletedHoles}/{TotalHoles}");
        }

        public void SetCompletionStatus(PunchCompletionStatus status)
        {
            CompletionStatus = status switch
            {
                PunchCompletionStatus.NormalFinished => "正常结束",
                PunchCompletionStatus.AbnormalFinished => "异常结束",
                PunchCompletionStatus.Cancelled => "已取消",
                _ => "结束"
            };

            AddTimeline($"流程结束: {CompletionStatus}");
        }

        private void UpdateProgress()
        {
            ProgressPercent = TotalHoles <= 0 ? 0 : Math.Round((double)CompletedHoles / TotalHoles * 100, 2);
        }

        private void AddTimeline(string text)
        {
            Timeline.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {text}");
            while (Timeline.Count > MaxLogs)
            {
                Timeline.RemoveAt(Timeline.Count - 1);
            }
        }

        private void BuildHeatmapPoints()
        {
            if (HoleItems.Count == 0)
            {
                return;
            }

            double maxAbsX = Math.Max(0.0001, HoleItems.Max(x => Math.Abs(x.TargetX)));
            double maxAbsY = Math.Max(0.0001, HoleItems.Max(x => Math.Abs(x.TargetY)));

            double centerX = HeatmapWidth / 2.0;
            double centerY = HeatmapHeight / 2.0;
            double usableHalfWidth = centerX - HeatmapPadding;
            double usableHalfHeight = centerY - HeatmapPadding;

            // 保持X/Y统一缩放比例，尽量贴近真实物理比例
            double scale = Math.Min(usableHalfWidth / maxAbsX, usableHalfHeight / maxAbsY);
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                scale = 1.0;
            }

            foreach (var hole in HoleItems)
            {
                double canvasX = centerX + hole.TargetX * scale;
                double canvasY = centerY - hole.TargetY * scale;

                var mapPoint = new PunchHeatmapPointViewModel
                {
                    HoleIndex = hole.HoleIndex,
                    RingNumber = hole.RingNumber,
                    SequenceIndex = hole.SequenceIndex,
                    X = hole.TargetX,
                    Y = hole.TargetY,
                    CanvasX = canvasX,
                    CanvasY = canvasY,
                    Size = 8,
                    Left = canvasX - 4,
                    Top = canvasY - 4,
                    Fill = Brushes.Gray,
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 0,
                    ToolTip = BuildHeatmapToolTip(hole.HoleIndex)
                };

                HeatmapPoints.Add(mapPoint);
                _heatmapPointsByHoleIndex[hole.HoleIndex] = mapPoint;
            }
        }

        private void RefreshHeatmapStyles()
        {
            foreach (var hole in HoleItems)
            {
                if (!_heatmapPointsByHoleIndex.TryGetValue(hole.HoleIndex, out var point))
                {
                    continue;
                }

                bool isCurrent = hole.HoleIndex == _currentHoleIndex;
                bool isNearest = hole.HoleIndex == _nearestHoleIndex;

                point.Fill = hole.IsCompleted
                    ? Brushes.ForestGreen
                    : Brushes.DimGray;

                point.Size = 8;
                point.Stroke = Brushes.Transparent;
                point.StrokeThickness = 0;

                if (isNearest)
                {
                    point.Fill = Brushes.DeepSkyBlue;
                    point.Size = 11;
                    point.Stroke = Brushes.Black;
                    point.StrokeThickness = 1;
                }

                if (isCurrent)
                {
                    point.Fill = Brushes.OrangeRed;
                    point.Size = 13;
                    point.Stroke = Brushes.Yellow;
                    point.StrokeThickness = 1.5;
                }

                // 当前孔与最近邻重合时，保留蓝色填充并用橙色描边强调“当前孔”
                if (isCurrent && isNearest)
                {
                    point.Fill = Brushes.DeepSkyBlue;
                    point.Size = 13;
                    point.Stroke = Brushes.OrangeRed;
                    point.StrokeThickness = 2;
                }

                point.Left = point.CanvasX - point.Size / 2.0;
                point.Top = point.CanvasY - point.Size / 2.0;
                point.ToolTip = BuildHeatmapToolTip(hole.HoleIndex);
            }

            UpdateDynamicLink();
        }

        private void UpdateDynamicLink()
        {
            if (_currentHoleIndex <= 0 || _nearestHoleIndex <= 0)
            {
                LinkVisibility = Visibility.Collapsed;
                return;
            }

            if (!_heatmapPointsByHoleIndex.TryGetValue(_currentHoleIndex, out var current)
                || !_heatmapPointsByHoleIndex.TryGetValue(_nearestHoleIndex, out var nearest))
            {
                LinkVisibility = Visibility.Collapsed;
                return;
            }

            LinkX1 = current.CanvasX;
            LinkY1 = current.CanvasY;
            LinkX2 = nearest.CanvasX;
            LinkY2 = nearest.CanvasY;
            LinkVisibility = Visibility.Visible;
        }

        private int ResolveNearestHoleIndex(double x, double y)
        {
            if (HoleItems.Count == 0)
            {
                return -1;
            }

            PunchHoleAuditItemViewModel nearest = HoleItems[0];
            double minDistSq = GetDistanceSquare(x, y, nearest.TargetX, nearest.TargetY);

            for (int i = 1; i < HoleItems.Count; i++)
            {
                var hole = HoleItems[i];
                double distSq = GetDistanceSquare(x, y, hole.TargetX, hole.TargetY);
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    nearest = hole;
                }
            }

            return nearest.HoleIndex;
        }

        private string BuildHeatmapToolTip(int holeIndex)
        {
            var hole = HoleItems.FirstOrDefault(x => x.HoleIndex == holeIndex);
            if (hole is null)
            {
                return string.Empty;
            }

            return $"孔#{hole.HoleIndex} 圈{hole.RingNumber}-序{hole.SequenceIndex}\nX={hole.TargetX:F3}, Y={hole.TargetY:F3}\n状态={hole.State}\n补偿={hole.Compensation}";
        }

        private static double GetDistanceSquare(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return dx * dx + dy * dy;
        }
    }
}
