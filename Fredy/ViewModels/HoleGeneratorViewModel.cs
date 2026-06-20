using Fredy.Drilling.Holes.UserControls.HoleGeneration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fredy.Drilling.Holes.ViewModels
{
    public class HoleGeneratorViewModel : ObservableObject
    {
        /// <summary>
        /// 自动扫描程序集中所有标注了 [GenerationTab] 特性的 ViewModel，
        /// 实例化并构建 GenerationTabs 集合。新增点位模式只需标注特性即可。
        /// </summary>
        public HoleGeneratorViewModel(Action<IReadOnlyList<HoleCoordinate>>? applyGeneratedPoints = null)
        {
            var discoveredTabs = DiscoverGenerationTabs(applyGeneratedPoints);
            GenerationTabs = new ObservableCollection<GenerationTabDescriptor>(discoveredTabs);

            Sector = GenerationTabs.FirstOrDefault(t => t.ViewModel is SectorGeneratorViewModel)?.ViewModel as SectorGeneratorViewModel
                ?? new SectorGeneratorViewModel(applyGeneratedPoints);
            Starry = GenerationTabs.FirstOrDefault(t => t.ViewModel is StarryGeneratorViewModel)?.ViewModel as StarryGeneratorViewModel
                ?? new StarryGeneratorViewModel(applyGeneratedPoints);
            RingSpinneret = GenerationTabs.FirstOrDefault(t => t.ViewModel is RingSpinneretGeneratorViewModel)?.ViewModel as RingSpinneretGeneratorViewModel
                ?? new RingSpinneretGeneratorViewModel(applyGeneratedPoints);
        }

        /// <summary>
        /// 通过反射扫描当前程序集中所有标注 [GenerationTab] 的 GeneratorTabViewModelBase 子类，
        /// 按 Order 升序排列后实例化并构建 GenerationTabDescriptor 列表。
        /// </summary>
        private static IEnumerable<GenerationTabDescriptor> DiscoverGenerationTabs(
            Action<IReadOnlyList<HoleCoordinate>>? applyGeneratedPoints)
        {
            var viewModelTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsClass
                            && !t.IsAbstract
                            && t.IsSubclassOf(typeof(GeneratorTabViewModelBase))
                            && t.GetCustomAttribute<GenerationTabAttribute>() is not null)
                .OrderBy(t => t.GetCustomAttribute<GenerationTabAttribute>()!.Order)
                .ToList();

            foreach (var type in viewModelTypes)
            {
                var attr = type.GetCustomAttribute<GenerationTabAttribute>()!;
                var viewModel = (GeneratorTabViewModelBase?)Activator.CreateInstance(type, applyGeneratedPoints);
                yield return new GenerationTabDescriptor { Header = attr.Header, ViewModel = viewModel };
            }
        }

        public SectorGeneratorViewModel Sector { get; }

        public StarryGeneratorViewModel Starry { get; }

        public RingSpinneretGeneratorViewModel RingSpinneret { get; }

        /// <summary>
        /// 动态 Tab 集合，新增点位生成模式只需在此集合添加即可。
        /// </summary>
        public ObservableCollection<GenerationTabDescriptor> GenerationTabs { get; }
    }

    /// <summary>
    /// 描述一个点位生成 Tab 的头部与对应的子 ViewModel。
    /// 水磨针等尚未实现的模式 ViewModel 可为 null。
    /// </summary>
    public sealed class GenerationTabDescriptor
    {
        public required string Header { get; init; }

        /// <summary>
        /// 对应 Tab 的子 ViewModel；若为 null 表示尚未实现。
        /// </summary>
        public GeneratorTabViewModelBase? ViewModel { get; init; }
    }

    public abstract class GeneratorTabViewModelBase : ObservableObject
    {
        private const double CanvasSize = 420;
        private const double PointSize = 6;
        private const double Padding = 24;
        private readonly Action<IReadOnlyList<HoleCoordinate>>? applyGeneratedPoints;
        private int totalHoles;
        private string statusMessage = "等待操作。";

        protected GeneratorTabViewModelBase(Action<IReadOnlyList<HoleCoordinate>>? applyGeneratedPoints = null)
        {
            this.applyGeneratedPoints = applyGeneratedPoints;
            PreviewPoints = new ObservableCollection<PreviewPointItem>();
        }

        public ObservableCollection<PreviewPointItem> PreviewPoints { get; }

        public double PreviewCanvasSize => CanvasSize;

        public int TotalHoles
        {
            get => totalHoles;
            protected set => SetProperty(ref totalHoles, value);
        }

        public string StatusMessage
        {
            get => statusMessage;
            protected set => SetProperty(ref statusMessage, value);
        }

        protected void ApplyGeneratedPointsToRecipe(IReadOnlyList<HoleCoordinate> coordinates)
        {
            applyGeneratedPoints?.Invoke(coordinates);
        }

        protected void UpdatePreview(IEnumerable<HoleCoordinate> coordinates)
        {
            var pointList = coordinates.ToList();
            PreviewPoints.Clear();

            if (pointList.Count == 0)
            {
                return;
            }

            double minX = pointList.Min(p => p.X);
            double maxX = pointList.Max(p => p.X);
            double minY = pointList.Min(p => p.Y);
            double maxY = pointList.Max(p => p.Y);
            double width = Math.Max(1.0, maxX - minX);
            double height = Math.Max(1.0, maxY - minY);
            double scaleX = (CanvasSize - Padding * 2) / width;
            double scaleY = (CanvasSize - Padding * 2) / height;
            double scale = Math.Min(scaleX, scaleY);
            double offsetX = (CanvasSize - width * scale) / 2;
            double offsetY = (CanvasSize - height * scale) / 2;

            foreach (var coordinate in pointList)
            {
                double x = offsetX + (coordinate.X - minX) * scale - PointSize / 2;
                double y = CanvasSize - offsetY - (coordinate.Y - minY) * scale - PointSize / 2 - height * scale;
                PreviewPoints.Add(new PreviewPointItem(x, y, coordinate.ToString()));
            }
        }

        protected void ClearPreview()
        {
            PreviewPoints.Clear();
        }

        protected bool ValidateResult(HoleGenerationResult result)
        {
            if (!result.Success)
            {
                ClearPreview();
                TotalHoles = 0;
                StatusMessage = $"生成失败：{result.ErrorMessage}";
                return false;
            }

            if (result.Coordinates.Count == 0)
            {
                ClearPreview();
                TotalHoles = 0;
                StatusMessage = "没有生成任何坐标，请检查参数。";
                return false;
            }

            return true;
        }
    }

    [GenerationTab("扇区盘", Order = 1)]
    public sealed class SectorGeneratorViewModel : GeneratorTabViewModelBase
    {
        private readonly SectorHoleGenerator generator = new();
        private readonly HoleDataExporter exporter = new();
        private int circleCount = 3;
        private int sectorCount = 8;
        private double ribWidth = 5;
        private double startRadius = 50;
        private double circleSpacing = 20;

        public SectorGeneratorViewModel(Action<IReadOnlyList<HoleCoordinate>>? applyGeneratedPoints = null)
            : base(applyGeneratedPoints)
        {
            Circles = new ObservableCollection<SectorCircleItemViewModel>();
            Circles.CollectionChanged += Circles_CollectionChanged;

            CalculateTotalCommand = new RelayCommand(CalculateTotalHoles);
            GeneratePreviewCommand = new RelayCommand(GeneratePreview);
            GenerateAndApplyCommand = new RelayCommand(GenerateAndApply);
            ExportCoordinatesCommand = new RelayCommand(ExportCoordinates);

            SyncCircles();
            CalculateTotalHoles();
        }

        public ObservableCollection<SectorCircleItemViewModel> Circles { get; }

        public IRelayCommand CalculateTotalCommand { get; }

        public IRelayCommand GeneratePreviewCommand { get; }

        public IRelayCommand GenerateAndApplyCommand { get; }

        public IRelayCommand ExportCoordinatesCommand { get; }

        public int CircleCount
        {
            get => circleCount;
            set
            {
                if (SetProperty(ref circleCount, Math.Max(1, value)))
                {
                    SyncCircles();
                    CalculateTotalHoles();
                }
            }
        }

        public int SectorCount
        {
            get => sectorCount;
            set
            {
                if (SetProperty(ref sectorCount, Math.Max(1, value)))
                {
                    CalculateTotalHoles();
                }
            }
        }

        public double RibWidth
        {
            get => ribWidth;
            set => SetProperty(ref ribWidth, Math.Max(0, value));
        }

        public double StartRadius
        {
            get => startRadius;
            set => SetProperty(ref startRadius, Math.Max(0, value));
        }

        public double CircleSpacing
        {
            get => circleSpacing;
            set => SetProperty(ref circleSpacing, Math.Max(0, value));
        }

        private void Circles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (SectorCircleItemViewModel item in e.NewItems)
                {
                    item.PropertyChanged += CircleItem_PropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (SectorCircleItemViewModel item in e.OldItems)
                {
                    item.PropertyChanged -= CircleItem_PropertyChanged;
                }
            }
        }

        private void CircleItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SectorCircleItemViewModel.HoleCount))
            {
                CalculateTotalHoles();
            }
        }

        private void SyncCircles()
        {
            while (Circles.Count < CircleCount)
            {
                int index = Circles.Count;
                Circles.Add(new SectorCircleItemViewModel
                {
                    Number = index + 1,
                    HoleCount = 4 + index * 2
                });
            }

            while (Circles.Count > CircleCount)
            {
                Circles.RemoveAt(Circles.Count - 1);
            }

            for (int i = 0; i < Circles.Count; i++)
            {
                Circles[i].Number = i + 1;
            }
        }

        private SectorHoleParameters BuildParameters()
        {
            return new SectorHoleParameters
            {
                CenterX = 0,
                CenterY = 0,
                CircleCount = CircleCount,
                SectorCount = SectorCount,
                RibWidth = RibWidth,
                StartRadius = StartRadius,
                CircleSpacing = CircleSpacing,
                HolesPerCircle = Circles.Select(c => Math.Max(0, c.HoleCount)).ToList()
            };
        }

        private void CalculateTotalHoles()
        {
            TotalHoles = Circles.Sum(c => Math.Max(0, c.HoleCount)) * Math.Max(1, SectorCount);
            StatusMessage = "已更新总孔数。";
        }

        private void GeneratePreview()
        {
            var result = generator.GenerateCoordinates(BuildParameters());
            if (!ValidateResult(result))
            {
                return;
            }

            TotalHoles = result.TotalHoles;
            UpdatePreview(result.Coordinates);
            ApplyGeneratedPointsToRecipe(result.Coordinates);
            StatusMessage = "已生成橘子瓣模式预览。";
        }

        private void GenerateAndApply()
        {
            var result = generator.GenerateCoordinates(BuildParameters());
            if (!ValidateResult(result))
            {
                return;
            }

            TotalHoles = result.TotalHoles;
            UpdatePreview(result.Coordinates);
            ApplyGeneratedPointsToRecipe(result.Coordinates);
            StatusMessage = "已生成橘子瓣点集并应用到配方点位。";
        }

        private void ExportCoordinates()
        {
            var result = generator.GenerateCoordinates(BuildParameters());
            if (!ValidateResult(result))
            {
                return;
            }

            exporter.ExportToDefaultFile(result.Coordinates);
            TotalHoles = result.TotalHoles;
            StatusMessage = "已执行橘子瓣坐标导出。";
        }
    }

    [GenerationTab("满天星", Order = 2)]
    public sealed class StarryGeneratorViewModel : GeneratorTabViewModelBase
    {
        private readonly HoleDataExporter exporter = new();
        private readonly StarryHoleGenerator generator = new();
        private double offsetAngle;
        private double startRadius = 50;
        private int circleCount = 5;
        private double circleSpacing = 20;
        private int holeIncrement = 4;
        private int firstCircleHoles = 6;

        public StarryGeneratorViewModel(Action<IReadOnlyList<HoleCoordinate>>? applyGeneratedPoints = null)
            : base(applyGeneratedPoints)
        {
            CircleInfos = new ObservableCollection<StarryCircleInfoItemViewModel>();

            GenerateCircleInfoCommand = new RelayCommand(GenerateCircleInfo);
            RefreshCommand = new RelayCommand(RefreshCircleInfo);
            GeneratePreviewCommand = new RelayCommand(GeneratePreview);
            GenerateAndApplyCommand = new RelayCommand(GenerateAndApply);
            ExportCoordinatesCommand = new RelayCommand(ExportCoordinates);

            GenerateCircleInfo();
        }

        public ObservableCollection<StarryCircleInfoItemViewModel> CircleInfos { get; }

        public IRelayCommand GenerateCircleInfoCommand { get; }

        public IRelayCommand RefreshCommand { get; }

        public IRelayCommand GeneratePreviewCommand { get; }

        public IRelayCommand GenerateAndApplyCommand { get; }

        public IRelayCommand ExportCoordinatesCommand { get; }

        public double OffsetAngle
        {
            get => offsetAngle;
            set => SetProperty(ref offsetAngle, value);
        }

        public double StartRadius
        {
            get => startRadius;
            set => SetProperty(ref startRadius, Math.Max(0, value));
        }

        public int CircleCount
        {
            get => circleCount;
            set => SetProperty(ref circleCount, Math.Max(1, value));
        }

        public double CircleSpacing
        {
            get => circleSpacing;
            set => SetProperty(ref circleSpacing, Math.Max(0, value));
        }

        public int HoleIncrement
        {
            get => holeIncrement;
            set => SetProperty(ref holeIncrement, value);
        }

        public int FirstCircleHoles
        {
            get => firstCircleHoles;
            set => SetProperty(ref firstCircleHoles, Math.Max(1, value));
        }

        private StarryHoleParameters BuildParameters()
        {
            return new StarryHoleParameters
            {
                CenterX = 0,
                CenterY = 0,
                OffsetAngle = OffsetAngle,
                StartRadius = StartRadius,
                CircleCount = CircleCount,
                CircleSpacing = CircleSpacing,
                HoleIncrement = HoleIncrement,
                FirstCircleHoles = FirstCircleHoles
            };
        }

        private void GenerateCircleInfo()
        {
            var rows = generator.GenerateCircleInfo(BuildParameters());
            CircleInfos.Clear();

            foreach (var row in rows)
            {
                CircleInfos.Add(new StarryCircleInfoItemViewModel
                {
                    Number = row.Number,
                    HoleIncrement = row.HoleIncrement,
                    Radius = row.Radius,
                    StartAngle = row.StartAngle,
                    HoleCount = row.HoleCount
                });
            }

            UpdateStarrySummary();
            StatusMessage = "已生成满天星圈信息。";
        }

        private void RefreshCircleInfo()
        {
            if (CircleInfos.Count == 0)
            {
                GenerateCircleInfo();
                return;
            }

            while (CircleInfos.Count < CircleCount)
            {
                CircleInfos.Add(new StarryCircleInfoItemViewModel
                {
                    Number = CircleInfos.Count + 1,
                    HoleIncrement = HoleIncrement
                });
            }

            while (CircleInfos.Count > CircleCount)
            {
                CircleInfos.RemoveAt(CircleInfos.Count - 1);
            }

            int previousHoles = 0;
            for (int i = 0; i < CircleInfos.Count; i++)
            {
                var row = CircleInfos[i];
                row.Number = i + 1;
                row.Radius = StartRadius + i * CircleSpacing;
                row.StartAngle = i == 0 ? 0 : OffsetAngle * i;

                if (i == 0)
                {
                    row.HoleIncrement = 0;
                    row.HoleCount = FirstCircleHoles;
                }
                else
                {
                    row.HoleCount = previousHoles + row.HoleIncrement;
                }

                previousHoles = row.HoleCount;
            }

            UpdateStarrySummary();
            StatusMessage = "已刷新满天星圈信息。";
        }

        private void GeneratePreview()
        {
            RefreshCircleInfo();
            var coordinates = BuildStarryCoordinates().ToList();
            if (coordinates.Count == 0)
            {
                ClearPreview();
                TotalHoles = 0;
                StatusMessage = "没有生成任何坐标，请检查参数。";
                return;
            }

            TotalHoles = coordinates.Count;
            UpdatePreview(coordinates);
            ApplyGeneratedPointsToRecipe(coordinates);
            StatusMessage = "已生成满天星模式预览。";
        }

        private void GenerateAndApply()
        {
            RefreshCircleInfo();
            var coordinates = BuildStarryCoordinates().ToList();
            if (coordinates.Count == 0)
            {
                ClearPreview();
                TotalHoles = 0;
                StatusMessage = "没有生成任何坐标，请检查参数。";
                return;
            }

            TotalHoles = coordinates.Count;
            UpdatePreview(coordinates);
            ApplyGeneratedPointsToRecipe(coordinates);
            StatusMessage = "已生成满天星点集并应用到配方点位。";
        }

        private void ExportCoordinates()
        {
            RefreshCircleInfo();
            var coordinates = BuildStarryCoordinates().ToList();
            if (coordinates.Count == 0)
            {
                ClearPreview();
                TotalHoles = 0;
                StatusMessage = "没有可导出的满天星坐标。";
                return;
            }

            var circleInfos = CircleInfos.Select(row => new CircleInfo
            {
                Number = row.Number,
                HoleIncrement = row.HoleIncrement,
                Radius = row.Radius,
                StartAngle = row.StartAngle,
                HoleCount = row.HoleCount
            }).ToList();

            exporter.ExportToTwoFiles(coordinates, circleInfos, StartRadius, CircleSpacing, CircleCount);
            TotalHoles = coordinates.Count;
            StatusMessage = "已执行满天星坐标导出。";
        }

        private IEnumerable<HoleCoordinate> BuildStarryCoordinates()
        {
            foreach (var row in CircleInfos.OrderBy(r => r.Number))
            {
                if (row.HoleCount <= 0)
                {
                    continue;
                }

                double startAngleRad = row.StartAngle * Math.PI / 180.0;
                double angleIncrementRad = 2 * Math.PI / row.HoleCount;

                for (int hole = 0; hole < row.HoleCount; hole++)
                {
                    double angle = startAngleRad + hole * angleIncrementRad;
                    double x = row.Radius * Math.Cos(angle);
                    double y = row.Radius * Math.Sin(angle);
                    yield return new HoleCoordinate(Math.Round(x, 6), Math.Round(y, 6), row.Number, 0, hole + 1);
                }
            }
        }

        private void UpdateStarrySummary()
        {
            TotalHoles = CircleInfos.Sum(c => Math.Max(0, c.HoleCount));
        }
    }

    [GenerationTab("环形喷丝板", Order = 3)]
    public sealed class RingSpinneretGeneratorViewModel : GeneratorTabViewModelBase
    {
        private readonly HoleDataExporter exporter = new();
        private readonly RingSpinneretGenerator generator = new();
        private int circleCount = 5;
        private int firstCircleHoles = 6;
        private double firstCircleDiameter = 100;
        private double circleSpacing = 20;
        private double offsetAngle;

        public RingSpinneretGeneratorViewModel(Action<IReadOnlyList<HoleCoordinate>>? applyGeneratedPoints = null)
            : base(applyGeneratedPoints)
        {
            CircleInfos = new ObservableCollection<RingSpinneretCircleInfoItemViewModel>();

            GenerateCircleInfoCommand = new RelayCommand(GenerateCircleInfo);
            RefreshCommand = new RelayCommand(GenerateCircleInfo);
            GeneratePreviewCommand = new RelayCommand(GeneratePreview);
            GenerateAndApplyCommand = new RelayCommand(GenerateAndApply);
            ExportCoordinatesCommand = new RelayCommand(ExportCoordinates);

            GenerateCircleInfo();
        }

        public ObservableCollection<RingSpinneretCircleInfoItemViewModel> CircleInfos { get; }

        public IRelayCommand GenerateCircleInfoCommand { get; }

        public IRelayCommand RefreshCommand { get; }

        public IRelayCommand GeneratePreviewCommand { get; }

        public IRelayCommand GenerateAndApplyCommand { get; }

        public IRelayCommand ExportCoordinatesCommand { get; }

        public int CircleCount
        {
            get => circleCount;
            set => SetProperty(ref circleCount, Math.Max(1, value));
        }

        public int FirstCircleHoles
        {
            get => firstCircleHoles;
            set => SetProperty(ref firstCircleHoles, Math.Max(1, value));
        }

        public double FirstCircleDiameter
        {
            get => firstCircleDiameter;
            set => SetProperty(ref firstCircleDiameter, Math.Max(0, value));
        }

        public double CircleSpacing
        {
            get => circleSpacing;
            set => SetProperty(ref circleSpacing, Math.Max(0, value));
        }

        public double OffsetAngle
        {
            get => offsetAngle;
            set => SetProperty(ref offsetAngle, value);
        }

        private RingSpinneretParameters BuildParameters()
        {
            return new RingSpinneretParameters
            {
                CenterX = 0,
                CenterY = 0,
                CircleCount = CircleCount,
                FirstCircleHoles = FirstCircleHoles,
                FirstCircleDiameter = FirstCircleDiameter,
                StartRadius = FirstCircleDiameter / 2,
                CircleSpacing = CircleSpacing,
                OffsetAngle = OffsetAngle,
                HoleIncrement = 2
            };
        }

        private void GenerateCircleInfo()
        {
            var rows = generator.GenerateCircleInfo(BuildParameters());
            CircleInfos.Clear();

            foreach (var row in rows)
            {
                CircleInfos.Add(new RingSpinneretCircleInfoItemViewModel
                {
                    Number = row.Number,
                    Diameter = row.Diameter,
                    HoleCount = row.HoleCount,
                    StartAngle = row.StartAngle
                });
            }

            TotalHoles = CircleInfos.Sum(c => c.HoleCount);
            StatusMessage = "已生成环形喷丝板圈信息。";
        }

        private void GeneratePreview()
        {
            var result = generator.GenerateCoordinates(BuildParameters());
            if (!ValidateResult(result))
            {
                return;
            }

            TotalHoles = result.TotalHoles;
            UpdatePreview(result.Coordinates);
            ApplyGeneratedPointsToRecipe(result.Coordinates);
            StatusMessage = "已生成环形喷丝板模式预览。";
        }

        private void GenerateAndApply()
        {
            var result = generator.GenerateCoordinates(BuildParameters());
            if (!ValidateResult(result))
            {
                return;
            }

            TotalHoles = result.TotalHoles;
            UpdatePreview(result.Coordinates);
            ApplyGeneratedPointsToRecipe(result.Coordinates);
            StatusMessage = "已生成环形喷丝板点集并应用到配方点位。";
        }

        private void ExportCoordinates()
        {
            var result = generator.GenerateCoordinates(BuildParameters());
            if (!ValidateResult(result))
            {
                return;
            }

            var circleInfos = generator.GenerateCircleInfo(BuildParameters());
            exporter.ExportRingSpinneretData(result.Coordinates, circleInfos);
            TotalHoles = result.TotalHoles;
            StatusMessage = "已执行环形喷丝板坐标导出。";
        }
    }

    /// <summary>
    /// 水磨针点位生成 ViewModel（预留扩展）。
    /// 标注 [GenerationTab] 即可自动加入生成 Tab，无需手动修改 HoleGeneratorViewModel。
    /// </summary>
    [GenerationTab("水磨针", Order = 4)]
    public sealed class WaterGrindingNeedleViewModel : GeneratorTabViewModelBase
    {
        public WaterGrindingNeedleViewModel(Action<IReadOnlyList<HoleCoordinate>>? applyGeneratedPoints = null)
            : base(applyGeneratedPoints)
        {
            StatusMessage = "水磨针功能开发中。";
        }
    }

    public sealed class SectorCircleItemViewModel : ObservableObject
    {
        private int number;
        private int holeCount;

        public int Number
        {
            get => number;
            set => SetProperty(ref number, value);
        }

        public int HoleCount
        {
            get => holeCount;
            set => SetProperty(ref holeCount, Math.Max(0, value));
        }
    }

    public sealed class StarryCircleInfoItemViewModel : ObservableObject
    {
        private int number;
        private int holeIncrement;
        private double radius;
        private double startAngle;
        private int holeCount;

        public int Number
        {
            get => number;
            set => SetProperty(ref number, value);
        }

        public int HoleIncrement
        {
            get => holeIncrement;
            set => SetProperty(ref holeIncrement, value);
        }

        public double Radius
        {
            get => radius;
            set => SetProperty(ref radius, value);
        }

        public double StartAngle
        {
            get => startAngle;
            set => SetProperty(ref startAngle, value);
        }

        public int HoleCount
        {
            get => holeCount;
            set => SetProperty(ref holeCount, Math.Max(0, value));
        }
    }

    public sealed class RingSpinneretCircleInfoItemViewModel : ObservableObject
    {
        private int number;
        private double diameter;
        private int holeCount;
        private double startAngle;

        public int Number
        {
            get => number;
            set => SetProperty(ref number, value);
        }

        public double Diameter
        {
            get => diameter;
            set => SetProperty(ref diameter, value);
        }

        public int HoleCount
        {
            get => holeCount;
            set => SetProperty(ref holeCount, value);
        }

        public double StartAngle
        {
            get => startAngle;
            set => SetProperty(ref startAngle, value);
        }
    }

    public sealed class PreviewPointItem
    {
        public PreviewPointItem(double x, double y, string toolTip)
        {
            X = x;
            Y = y;
            ToolTip = toolTip;
        }

        public double X { get; }

        public double Y { get; }

        public string ToolTip { get; }
    }

    public sealed class HoleCoordinate
    {
        public HoleCoordinate(double x, double y, int circleIndex, int sectorIndex, int pointIndex)
        {
            X = x;
            Y = y;
            CircleIndex = circleIndex;
            SectorIndex = sectorIndex;
            PointIndex = pointIndex;
        }

        public double X { get; set; }

        public double Y { get; set; }

        public int CircleIndex { get; set; }

        public int SectorIndex { get; set; }

        public int PointIndex { get; set; }

        public override string ToString()
        {
            return $"X:{X:F3}, Y:{Y:F3}, 圈:{CircleIndex}, 扇区:{SectorIndex}, 孔:{PointIndex}";
        }
    }

    public abstract class HoleParameters
    {
        public double CenterX { get; set; }

        public double CenterY { get; set; }
    }

    public sealed class SectorHoleParameters : HoleParameters
    {
        public int SectorCount { get; set; }

        public double RibWidth { get; set; }

        public double StartRadius { get; set; }

        public double CircleSpacing { get; set; }

        public int CircleCount { get; set; }

        public List<int> HolesPerCircle { get; set; } = new();
    }

    public sealed class StarryHoleParameters : HoleParameters
    {
        public double StartRadius { get; set; }

        public double CircleSpacing { get; set; }

        public int CircleCount { get; set; }

        public double OffsetAngle { get; set; }

        public int HoleIncrement { get; set; }

        public int FirstCircleHoles { get; set; }
    }

    public sealed class RingSpinneretParameters : HoleParameters
    {
        public double StartRadius { get; set; }

        public double CircleSpacing { get; set; }

        public int CircleCount { get; set; }

        public double OffsetAngle { get; set; }

        public int HoleIncrement { get; set; }

        public int FirstCircleHoles { get; set; }

        public double FirstCircleDiameter { get; set; }
    }

    public sealed class HoleGenerationResult
    {
        public List<HoleCoordinate> Coordinates { get; set; } = new();

        public int TotalHoles { get; set; }

        public int CircleCount { get; set; }

        public int SectorCount { get; set; }

        public bool Success { get; set; } = true;

        public string ErrorMessage { get; set; } = string.Empty;
    }

    public sealed class CircleInfo
    {
        public int Number { get; set; }

        public int HoleIncrement { get; set; }

        public double Radius { get; set; }

        public double StartAngle { get; set; }

        public int HoleCount { get; set; }
    }

    public sealed class RingSpinneretCircleInfo
    {
        public int Number { get; set; }

        public double Diameter { get; set; }

        public int HoleCount { get; set; }

        public double StartAngle { get; set; }
    }

    public sealed class SectorHoleGenerator
    {
        public HoleGenerationResult GenerateCoordinates(SectorHoleParameters parameters)
        {
            try
            {
                var result = new HoleGenerationResult
                {
                    CircleCount = parameters.CircleCount,
                    SectorCount = parameters.SectorCount
                };

                var coordinates = new List<HoleCoordinate>();
                double sectorAngleRad = 2 * Math.PI / parameters.SectorCount;

                for (int circle = 0; circle < parameters.CircleCount; circle++)
                {
                    if (circle >= parameters.HolesPerCircle.Count)
                    {
                        break;
                    }

                    double currentRadius = parameters.StartRadius + circle * parameters.CircleSpacing;
                    int holesInThisCircle = parameters.HolesPerCircle[circle];
                    if (holesInThisCircle <= 0)
                    {
                        continue;
                    }

                    double alphaRad = CalculateBoundaryAngle(currentRadius, parameters.RibWidth);
                    double effectiveAngleRad = holesInThisCircle == 1
                        ? 0
                        : (sectorAngleRad - 2 * alphaRad) / (holesInThisCircle - 1);

                    for (int sector = 0; sector < parameters.SectorCount; sector++)
                    {
                        double sectorCenterAngle = sector * sectorAngleRad;
                        double sectorStartAngle = sectorCenterAngle - sectorAngleRad / 2 + alphaRad;

                        for (int hole = 0; hole < holesInThisCircle; hole++)
                        {
                            double currentAngle = sectorStartAngle + hole * effectiveAngleRad;
                            double x = parameters.CenterX + currentRadius * Math.Cos(currentAngle);
                            double y = parameters.CenterY + currentRadius * Math.Sin(currentAngle);

                            coordinates.Add(new HoleCoordinate(
                                Math.Round(x, 6),
                                Math.Round(y, 6),
                                circle + 1,
                                sector + 1,
                                hole + 1));
                        }
                    }
                }

                result.Coordinates = coordinates.OrderBy(c => c.CircleIndex)
                    .ThenBy(c => c.SectorIndex)
                    .ThenBy(c => c.PointIndex)
                    .ToList();
                result.TotalHoles = result.Coordinates.Count;
                return result;
            }
            catch (Exception ex)
            {
                return new HoleGenerationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static double CalculateBoundaryAngle(double radius, double marginDistance)
        {
            if (marginDistance >= radius)
            {
                return Math.PI / 2;
            }

            double sinAlpha = marginDistance / (radius * 2);
            return Math.Asin(sinAlpha);
        }
    }

    public sealed class StarryHoleGenerator
    {
        public HoleGenerationResult GenerateCoordinates(StarryHoleParameters parameters)
        {
            try
            {
                var result = new HoleGenerationResult
                {
                    CircleCount = parameters.CircleCount,
                    SectorCount = 1
                };

                var coordinates = new List<HoleCoordinate>();

                for (int circle = 0; circle < parameters.CircleCount; circle++)
                {
                    double currentRadius = parameters.StartRadius + circle * parameters.CircleSpacing;
                    int holesInThisCircle = circle == 0
                        ? parameters.FirstCircleHoles
                        : parameters.FirstCircleHoles + circle * parameters.HoleIncrement;

                    if (holesInThisCircle <= 0)
                    {
                        continue;
                    }

                    double currentStartAngleRad = circle == 0
                        ? 0
                        : parameters.OffsetAngle * circle * Math.PI / 180.0;
                    double angleIncrementRad = 2 * Math.PI / holesInThisCircle;

                    for (int hole = 0; hole < holesInThisCircle; hole++)
                    {
                        double currentAngle = currentStartAngleRad + hole * angleIncrementRad;
                        double x = parameters.CenterX + currentRadius * Math.Cos(currentAngle);
                        double y = parameters.CenterY + currentRadius * Math.Sin(currentAngle);

                        coordinates.Add(new HoleCoordinate(
                            Math.Round(x, 6),
                            Math.Round(y, 6),
                            circle + 1,
                            0,
                            hole + 1));
                    }
                }

                result.Coordinates = coordinates.OrderBy(c => c.CircleIndex)
                    .ThenBy(c => c.PointIndex)
                    .ToList();
                result.TotalHoles = result.Coordinates.Count;
                return result;
            }
            catch (Exception ex)
            {
                return new HoleGenerationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public List<CircleInfo> GenerateCircleInfo(StarryHoleParameters parameters)
        {
            var circleInfos = new List<CircleInfo>();
            int previousHoles = 0;

            for (int i = 1; i <= parameters.CircleCount; i++)
            {
                int currentHoleIncrement = i == 1 ? 0 : parameters.HoleIncrement;
                double currentRadius = parameters.StartRadius + (i - 1) * parameters.CircleSpacing;
                double currentStartAngle = i == 1 ? 0 : parameters.OffsetAngle * (i - 1);
                int currentHoleCount = i == 1 ? parameters.FirstCircleHoles : previousHoles + currentHoleIncrement;

                circleInfos.Add(new CircleInfo
                {
                    Number = i,
                    HoleIncrement = currentHoleIncrement,
                    Radius = currentRadius,
                    StartAngle = currentStartAngle,
                    HoleCount = currentHoleCount
                });

                previousHoles = currentHoleCount;
            }

            return circleInfos;
        }
    }

    public sealed class RingSpinneretGenerator
    {
        public HoleGenerationResult GenerateCoordinates(RingSpinneretParameters parameters)
        {
            try
            {
                var result = new HoleGenerationResult
                {
                    CircleCount = parameters.CircleCount,
                    SectorCount = 1
                };

                var coordinates = new List<HoleCoordinate>();

                for (int circle = 0; circle < parameters.CircleCount; circle++)
                {
                    double currentRadius = parameters.StartRadius + circle * parameters.CircleSpacing;
                    int holesInThisCircle = circle == 0
                        ? parameters.FirstCircleHoles
                        : parameters.FirstCircleHoles + circle * parameters.HoleIncrement;

                    if (holesInThisCircle <= 0)
                    {
                        continue;
                    }

                    double currentStartAngleRad = circle == 0
                        ? 0
                        : parameters.OffsetAngle * circle * Math.PI / 180.0;
                    double angleIncrementRad = 2 * Math.PI / holesInThisCircle;

                    for (int hole = 0; hole < holesInThisCircle; hole++)
                    {
                        double currentAngle = currentStartAngleRad + hole * angleIncrementRad;
                        double x = parameters.CenterX + currentRadius * Math.Cos(currentAngle);
                        double y = parameters.CenterY + currentRadius * Math.Sin(currentAngle);

                        coordinates.Add(new HoleCoordinate(
                            Math.Round(x, 6),
                            Math.Round(y, 6),
                            circle + 1,
                            0,
                            hole + 1));
                    }
                }

                result.Coordinates = coordinates.OrderBy(c => c.CircleIndex)
                    .ThenBy(c => c.PointIndex)
                    .ToList();
                result.TotalHoles = result.Coordinates.Count;
                return result;
            }
            catch (Exception ex)
            {
                return new HoleGenerationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public List<RingSpinneretCircleInfo> GenerateCircleInfo(RingSpinneretParameters parameters)
        {
            var circleInfos = new List<RingSpinneretCircleInfo>();
            int previousHoles = 0;

            for (int i = 1; i <= parameters.CircleCount; i++)
            {
                int currentHoleIncrement = i == 1 ? 0 : parameters.HoleIncrement;
                double currentStartAngle = i == 1 ? 0 : parameters.OffsetAngle * (i - 1);
                int currentHoleCount = i == 1 ? parameters.FirstCircleHoles : previousHoles + currentHoleIncrement;
                double currentDiameter = i == 1 ? parameters.FirstCircleDiameter : 0;

                circleInfos.Add(new RingSpinneretCircleInfo
                {
                    Number = i,
                    Diameter = currentDiameter,
                    HoleCount = currentHoleCount,
                    StartAngle = currentStartAngle
                });

                previousHoles = currentHoleCount;
            }

            return circleInfos;
        }
    }

    public sealed class HoleDataExporter
    {
        public bool ExportToDefaultFile(List<HoleCoordinate> coordinates)
        {
            try
            {
                const string filePath = "孔坐标分布.txt";
                using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

                var sortedByCircle = coordinates.OrderBy(c => c.CircleIndex)
                    .ThenBy(c => c.SectorIndex)
                    .ThenBy(c => c.PointIndex)
                    .ToList();

                int currentCircle = 0;
                int holeCounter = 1;
                foreach (var coord in sortedByCircle)
                {
                    if (coord.CircleIndex != currentCircle)
                    {
                        currentCircle = coord.CircleIndex;
                        holeCounter = 1;
                    }

                    writer.WriteLine($"{coord.X:F3},    {coord.Y:F3},      {coord.CircleIndex},      {holeCounter}");
                    holeCounter++;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导出文件时出错: {ex.Message}", "错误");
                return false;
            }
        }

        public bool ExportToTwoFiles(List<HoleCoordinate> coordinates, List<CircleInfo> circleInfos, double startRadius, double circleSpacing, int circleCount)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存坐标文件",
                    Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    FileName = "Coordinate.txt",
                    DefaultExt = ".txt",
                    AddExtension = true
                };

                if (saveFileDialog.ShowDialog() != true)
                {
                    return false;
                }

                string selectedFilePath = saveFileDialog.FileName;
                string directory = Path.GetDirectoryName(selectedFilePath) ?? Environment.CurrentDirectory;
                string coordFileName = Path.GetFileNameWithoutExtension(selectedFilePath);
                string coordFilePath = Path.Combine(directory, coordFileName + ".txt");
                string circleFilePath = Path.Combine(directory, "Circle.txt");

                ProcessAndSaveBothFiles(coordFilePath, circleFilePath, coordinates, circleInfos, startRadius, circleSpacing, circleCount);
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存文件时出错：{ex.Message}", "错误");
                return false;
            }
        }

        public bool ExportRingSpinneretData(List<HoleCoordinate> coordinates, List<RingSpinneretCircleInfo> ringSpinneretCircleInfos)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "保存环形喷丝板数据",
                    Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                    FileName = "RingSpinneret.txt",
                    DefaultExt = ".txt",
                    AddExtension = true
                };

                if (saveFileDialog.ShowDialog() != true)
                {
                    return false;
                }

                using var writer = new StreamWriter(saveFileDialog.FileName, false, Encoding.UTF8);
                writer.WriteLine("环形喷丝板圈信息:");
                writer.WriteLine("圈号, 直径, 孔数, 起始角度");
                foreach (var circleInfo in ringSpinneretCircleInfos)
                {
                    writer.WriteLine($"{circleInfo.Number}, {circleInfo.Diameter:F3}, {circleInfo.HoleCount}, {circleInfo.StartAngle:F2}");
                }

                writer.WriteLine();
                writer.WriteLine("孔坐标信息:");
                writer.WriteLine("X坐标, Y坐标, 圈号, 孔号");

                var sortedCoordinates = coordinates.OrderBy(c => c.CircleIndex)
                    .ThenBy(c => c.PointIndex)
                    .ToList();

                int currentCircle = 0;
                int holeCounter = 1;
                foreach (var coord in sortedCoordinates)
                {
                    if (coord.CircleIndex != currentCircle)
                    {
                        currentCircle = coord.CircleIndex;
                        holeCounter = 1;
                    }

                    writer.WriteLine($"{coord.X:F3}, {coord.Y:F3}, {coord.CircleIndex}, {holeCounter}");
                    holeCounter++;
                }

                System.Windows.MessageBox.Show($"文件已成功保存到: {saveFileDialog.FileName}", "保存成功");
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存文件时出错：{ex.Message}", "错误");
                return false;
            }
        }

        private static void ProcessAndSaveBothFiles(string coordFilePath, string circleFilePath, List<HoleCoordinate> coordinates, List<CircleInfo> circleInfos, double startRadius, double circleSpacing, int circleCount)
        {
            var sortedCoordinates = coordinates.OrderBy(c => c.CircleIndex)
                .ThenBy(c => c.SectorIndex)
                .ThenBy(c => c.PointIndex)
                .ToList();

            using var coordWriter = new StreamWriter(coordFilePath, false, Encoding.UTF8);
            using var circleWriter = new StreamWriter(circleFilePath, false, Encoding.UTF8);

            if (circleInfos.Count > 0)
            {
                foreach (var circleInfo in circleInfos)
                {
                    circleWriter.WriteLine($"{circleInfo.Number},    {circleInfo.HoleCount},      {circleInfo.Radius:F3}");
                }
            }
            else
            {
                for (int circle = 1; circle <= circleCount; circle++)
                {
                    double radius = startRadius + (circle - 1) * circleSpacing;
                    int holeCount = coordinates.Count(c => c.CircleIndex == circle);
                    circleWriter.WriteLine($"{circle},    {holeCount},      {radius:F3}");
                }
            }

            int currentCircle = 0;
            int holeCounter = 1;
            foreach (var coord in sortedCoordinates)
            {
                if (coord.CircleIndex != currentCircle)
                {
                    currentCircle = coord.CircleIndex;
                    holeCounter = 1;
                }

                coordWriter.WriteLine($"{coord.X:F3},    {coord.Y:F3},      {coord.CircleIndex},      {holeCounter}");
                holeCounter++;
            }
        }
    }
}
