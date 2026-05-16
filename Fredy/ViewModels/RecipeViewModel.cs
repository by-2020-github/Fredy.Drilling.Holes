using Common.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fredy.Drilling.Holes.Models;
using Fredy.Drilling.Holes.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class PunchPointViewModel : ObservableObject
    {
        private double _x;
        private double _y;
        private int _ringNumber;
        private int _sequenceIndex;
        private bool _complete;

        public PunchPointViewModel(PunchPoint punchPoint)
        {
            _x = punchPoint.X;
            _y = punchPoint.Y;
            _ringNumber = punchPoint.RingNumber;
            _sequenceIndex = punchPoint.SequenceIndex;
        }

        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        public int RingNumber
        {
            get => _ringNumber;
            set => SetProperty(ref _ringNumber, value);
        }

        public int SequenceIndex
        {
            get => _sequenceIndex;
            set => SetProperty(ref _sequenceIndex, value);
        }

        public bool Complete
        {
            get => _complete;
            set => SetProperty(ref _complete, value);
        }

        public PunchPoint ToModel()
        {
            return new PunchPoint
            {
                X = X,
                Y = Y,
                RingNumber = RingNumber,
                SequenceIndex = SequenceIndex
            };
        }
    }

    public partial class RecipeDetectionItemViewModel : ObservableObject
    {
        private int _index;
        private int _detectionCount;

        public RecipeDetectionItemViewModel(RecipeDetectionItem model)
        {
            _index = model.Index;
            _detectionCount = model.DetectionCount;
        }

        public int Index
        {
            get => _index;
            set => SetProperty(ref _index, value);
        }

        public int DetectionCount
        {
            get => _detectionCount;
            set => SetProperty(ref _detectionCount, Math.Max(0, value));
        }

        public RecipeDetectionItem ToModel()
        {
            return new RecipeDetectionItem
            {
                Index = Index,
                DetectionCount = DetectionCount
            };
        }
    }

    public partial class RecipeDepthItemViewModel : ObservableObject
    {
        private string _label;
        private int _value;

        public RecipeDepthItemViewModel(RecipeDepthItem model)
        {
            _label = model.Label;
            _value = Math.Max(0, model.Value);
        }

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        public int Value
        {
            get => _value;
            set => SetProperty(ref _value, Math.Max(0, value));
        }

        public RecipeDepthItem ToModel()
        {
            return new RecipeDepthItem
            {
                Label = Label,
                Value = Value
            };
        }
    }

    public partial class RecipeViewModel : ObservableObject
    {
        private readonly Recipe _recipe;
        private readonly RecipePunchParameters _punchParameters;
        private readonly ILogger _logger;
        private string _recipeName = string.Empty;
        private string _typeName = string.Empty;
        private double _radius;
        private int _rings;
        private PunchPointViewModel? _selectedPunchPoint;
        private int _detectionOffsetThreshold;
        private int _secondPassOffsetThreshold;
        private bool _isFirstPass = true;
        private IReadOnlyDictionary<int, (double X, double Y)>? _matchedPoints;
        private RecipeDepthItemViewModel? _selectedPunchDepthItem;
        private int _addPunchDepthCount = 1;
        private int _newPunchDepthValue;

        public RecipeViewModel(Recipe recipe)
            : this(recipe, Log.Logger)
        {
        }

        public RecipeViewModel(Recipe recipe, ILogger logger)
        {
            _recipe = recipe;
            _logger = (logger ?? Log.Logger).ForContext<RecipeViewModel>();
            recipe.PunchParameters ??= new RecipePunchParameters();
            recipe.ProcessParameters ??= new RecipeProcessParameters();
            _punchParameters = recipe.PunchParameters;
            ProcessParameters = recipe.ProcessParameters;
            _recipeName = recipe.RecipeName;
            _typeName = recipe.TypeName;
            _radius = _punchParameters.Radius;
            _rings = _punchParameters.Rings;
            PunchPoints = new ObservableCollection<PunchPointViewModel>(_punchParameters.PunchPoints
                .OrderBy(x => x.RingNumber)
                .ThenBy(x => x.SequenceIndex)
                .Select(x => new PunchPointViewModel(x)));

            DetectionItems = new ObservableCollection<RecipeDetectionItemViewModel>();
            SecondPassDetectionItems = new ObservableCollection<RecipeDetectionItemViewModel>();
            PunchDepthItems = new ObservableCollection<RecipeDepthItemViewModel>((ProcessParameters.PunchDepths ?? new List<RecipeDepthItem>())
                .Select(x => new RecipeDepthItemViewModel(x)));

            PunchPoints.CollectionChanged += PunchPoints_CollectionChanged;
            DetectionItems.CollectionChanged += DetectionItems_CollectionChanged;
            SecondPassDetectionItems.CollectionChanged += SecondPassDetectionItems_CollectionChanged;
            PunchDepthItems.CollectionChanged += PunchDepthItems_CollectionChanged;

            AddPunchPointCommand = new RelayCommand(AddPunchPoint);
            RemoveSelectedPunchPointCommand = new RelayCommand(RemoveSelectedPunchPoint, CanRemoveSelectedPunchPoint);
            SortPunchPointsCommand = new RelayCommand(SortPunchPoints);
            ResetDetectionCommand = new RelayCommand(ResetDetection);
            ResetSecondPassDetectionCommand = new RelayCommand(ResetSecondPassDetection);
            AddPunchDepthItemCommand = new RelayCommand(AddPunchDepthItem);
            RemoveSelectedPunchDepthItemCommand = new RelayCommand(RemoveSelectedPunchDepthItem, CanRemoveSelectedPunchDepthItem);
            ClearPunchDepthItemsCommand = new RelayCommand(ClearPunchDepthItems, CanClearPunchDepthItems);

            foreach (var punchPoint in PunchPoints)
            {
                SubscribePunchPoint(punchPoint);
            }

            foreach (var punchDepthItem in PunchDepthItems)
            {
                SubscribePunchDepthItem(punchDepthItem);
            }

            InitializeDetectionParameters(ResolveDefaultConfig());
            SyncPunchDepthsToRecipe();
            _logger.Information("配方视图模型已初始化: {RecipeName}", _recipeName);
        }

        public string RecipeName
        {
            get => _recipeName;
            set
            {
                if (SetProperty(ref _recipeName, value))
                {
                    _recipe.RecipeName = value;
                }
            }
        }

        public int AddPunchDepthCount
        {
            get => _addPunchDepthCount;
            set => SetProperty(ref _addPunchDepthCount, Math.Max(1, value));
        }

        public int NewPunchDepthValue
        {
            get => _newPunchDepthValue;
            set => SetProperty(ref _newPunchDepthValue, Math.Max(0, value));
        }

        public string TypeName
        {
            get => _typeName;
            set
            {
                if (SetProperty(ref _typeName, value))
                {
                    _recipe.TypeName = value;
                }
            }
        }

        public double Radius
        {
            get => _radius;
            set
            {
                if (SetProperty(ref _radius, value))
                {
                    _punchParameters.Radius = value;
                }
            }
        }

        public int Rings
        {
            get => _rings;
            set
            {
                if (SetProperty(ref _rings, value))
                {
                    _punchParameters.Rings = value;
                }
            }
        }

        public int DetectionOffsetThreshold
        {
            get => _detectionOffsetThreshold;
            set
            {
                if (SetProperty(ref _detectionOffsetThreshold, Math.Max(0, value)))
                {
                    ProcessParameters.FirstPassOffsetThreshold = _detectionOffsetThreshold;
                }
            }
        }

        public int SecondPassOffsetThreshold
        {
            get => _secondPassOffsetThreshold;
            set
            {
                if (SetProperty(ref _secondPassOffsetThreshold, Math.Max(0, value)))
                {
                    ProcessParameters.SecondPassOffsetThreshold = _secondPassOffsetThreshold;
                }
            }
        }

        public PunchPointViewModel? SelectedPunchPoint
        {
            get => _selectedPunchPoint;
            set
            {
                if (SetProperty(ref _selectedPunchPoint, value))
                {
                    RemoveSelectedPunchPointCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool IsFirstPass
        {
            get => _isFirstPass;
            set => SetProperty(ref _isFirstPass, value);
        }

        public IReadOnlyDictionary<int, (double X, double Y)>? MatchedPoints
        {
            get => _matchedPoints;
            set => SetProperty(ref _matchedPoints, value);
        }

        public RecipeDepthItemViewModel? SelectedPunchDepthItem
        {
            get => _selectedPunchDepthItem;
            set
            {
                if (SetProperty(ref _selectedPunchDepthItem, value))
                {
                    RemoveSelectedPunchDepthItemCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public RecipeProcessParameters ProcessParameters { get; }

        public RecipePunchParameters PunchParameters => _punchParameters;

        public Recipe Recipe => _recipe;

        public ObservableCollection<PunchPointViewModel> PunchPoints { get; }

        public ObservableCollection<RecipeDetectionItemViewModel> DetectionItems { get; }

        public ObservableCollection<RecipeDetectionItemViewModel> SecondPassDetectionItems { get; }

        public ObservableCollection<RecipeDepthItemViewModel> PunchDepthItems { get; }

        public IRelayCommand AddPunchPointCommand { get; }

        public IRelayCommand RemoveSelectedPunchPointCommand { get; }

        public IRelayCommand SortPunchPointsCommand { get; }

        public IRelayCommand ResetDetectionCommand { get; }

        public IRelayCommand ResetSecondPassDetectionCommand { get; }

        public IRelayCommand AddPunchDepthItemCommand { get; }

        public IRelayCommand RemoveSelectedPunchDepthItemCommand { get; }

        public IRelayCommand ClearPunchDepthItemsCommand { get; }

        public int TotalCount => PunchPoints.Count;

        public int CompletedCount => PunchPoints.Count(x => x.Complete);

        public int RemainingCount => Math.Max(0, TotalCount - CompletedCount);

        public int FirstPunchTotalDepth => PunchDepthItems.Sum(x => x.Value);

        public void UpdateCompletedCount(int completedCount)
        {
            var clampedCount = Math.Clamp(completedCount, 0, TotalCount);

            for (int i = 0; i < PunchPoints.Count; i++)
            {
                PunchPoints[i].Complete = i < clampedCount;
            }

            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(RemainingCount));
        }

        public void SetCompletedIndices(IEnumerable<int> completedIndices)
        {
            var completedIndexSet = completedIndices is HashSet<int> hashSet
                ? hashSet
                : completedIndices.ToHashSet();

            for (int i = 0; i < PunchPoints.Count; i++)
            {
                PunchPoints[i].Complete = completedIndexSet.Contains(i);
            }

            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(RemainingCount));
        }

        private void PunchDepthItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<RecipeDepthItemViewModel>())
                {
                    UnsubscribePunchDepthItem(item);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<RecipeDepthItemViewModel>())
                {
                    SubscribePunchDepthItem(item);
                }
            }

            RenumberPunchDepthLabels();
            SyncPunchDepthsToRecipe();
            OnPropertyChanged(nameof(PunchDepthItems));
            OnPropertyChanged(nameof(FirstPunchTotalDepth));
            RemoveSelectedPunchDepthItemCommand.NotifyCanExecuteChanged();
            ClearPunchDepthItemsCommand.NotifyCanExecuteChanged();
        }

        public void ReplacePunchPoints(IEnumerable<PunchPointViewModel> points)
        {
            var orderedPoints = points
                .OrderBy(x => x.RingNumber)
                .ThenBy(x => x.SequenceIndex)
                .ThenBy(x => x.X)
                .ThenBy(x => x.Y)
                .ToList();

            ApplyOrderedPunchPoints(orderedPoints);
            SelectedPunchPoint = PunchPoints.FirstOrDefault();
            _logger.Information("配方点位已整体替换，共 {Count} 个", PunchPoints.Count);
        }

        private static AppConfig? ResolveDefaultConfig()
        {
            try
            {
                return App.ServiceProvider.GetService<ConfigService>()?.CurrentConfig;
            }
            catch
            {
                return null;
            }
        }

        private void InitializeDetectionParameters(AppConfig? defaultConfig)
        {
            var firstPassSource = ProcessParameters.FirstPassDetectionItems;
            if (firstPassSource is { Count: > 0 })
            {
                LoadDetectionItems(DetectionItems, firstPassSource);
            }
            else if (defaultConfig?.DetectionRingItems is { Count: > 0 } configFirstPass)
            {
                LoadDetectionItems(DetectionItems, configFirstPass.Select(x => new RecipeDetectionItem
                {
                    Index = x.Index,
                    DetectionCount = x.DetectionCount
                }));
            }
            else
            {
                LoadDetectionItems(DetectionItems, Enumerable.Range(1, 32).Select(i => new RecipeDetectionItem { Index = i, DetectionCount = 0 }));
            }

            var secondPassSource = ProcessParameters.SecondPassDetectionItems;
            if (secondPassSource is { Count: > 0 })
            {
                LoadDetectionItems(SecondPassDetectionItems, secondPassSource);
            }
            else if (defaultConfig?.SecondPassDetectionItems is { Count: > 0 } configSecondPass)
            {
                LoadDetectionItems(SecondPassDetectionItems, configSecondPass.Select(x => new RecipeDetectionItem
                {
                    Index = x.Index,
                    DetectionCount = x.DetectionCount
                }));
            }
            else
            {
                LoadDetectionItems(SecondPassDetectionItems, Enumerable.Range(1, 32).Select(i => new RecipeDetectionItem { Index = i, DetectionCount = 0 }));
            }

            DetectionOffsetThreshold = ProcessParameters.FirstPassOffsetThreshold
                ?? defaultConfig?.DetectionOffsetThreshold
                ?? 0;

            SecondPassOffsetThreshold = ProcessParameters.SecondPassOffsetThreshold
                ?? defaultConfig?.SecondPassOffsetThreshold
                ?? 35;

            SyncFirstPassDetectionToRecipe();
            SyncSecondPassDetectionToRecipe();
        }

        private static void LoadDetectionItems(ObservableCollection<RecipeDetectionItemViewModel> target, IEnumerable<RecipeDetectionItem> source)
        {
            target.Clear();
            foreach (var item in source.OrderBy(x => x.Index))
            {
                target.Add(new RecipeDetectionItemViewModel(item));
            }
        }

        private void DetectionItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<RecipeDetectionItemViewModel>())
                {
                    item.PropertyChanged -= DetectionItem_PropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<RecipeDetectionItemViewModel>())
                {
                    item.PropertyChanged += DetectionItem_PropertyChanged;
                }
            }

            SyncFirstPassDetectionToRecipe();
        }

        private void SecondPassDetectionItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<RecipeDetectionItemViewModel>())
                {
                    item.PropertyChanged -= SecondPassDetectionItem_PropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<RecipeDetectionItemViewModel>())
                {
                    item.PropertyChanged += SecondPassDetectionItem_PropertyChanged;
                }
            }

            SyncSecondPassDetectionToRecipe();
        }

        private void DetectionItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SyncFirstPassDetectionToRecipe();
        }

        private void SecondPassDetectionItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SyncSecondPassDetectionToRecipe();
        }

        private void SyncFirstPassDetectionToRecipe()
        {
            ProcessParameters.FirstPassDetectionItems = DetectionItems
                .OrderBy(x => x.Index)
                .Select(x => x.ToModel())
                .ToList();
            ProcessParameters.FirstPassOffsetThreshold = DetectionOffsetThreshold;
        }

        private void SyncSecondPassDetectionToRecipe()
        {
            ProcessParameters.SecondPassDetectionItems = SecondPassDetectionItems
                .OrderBy(x => x.Index)
                .Select(x => x.ToModel())
                .ToList();
            ProcessParameters.SecondPassOffsetThreshold = SecondPassOffsetThreshold;
        }

        private void ResetDetection()
        {
            foreach (var item in DetectionItems)
            {
                item.DetectionCount = 0;
            }

            DetectionOffsetThreshold = 0;
            SyncFirstPassDetectionToRecipe();
            _logger.Information("头道探测参数已重置");
        }

        private void ResetSecondPassDetection()
        {
            foreach (var item in SecondPassDetectionItems)
            {
                item.DetectionCount = 0;
            }

            SecondPassOffsetThreshold = 0;
            SyncSecondPassDetectionToRecipe();
            _logger.Information("二道探测参数已重置");
        }

        private void AddPunchPoint()
        {
            var nextRingNumber = PunchPoints.Count == 0 ? 1 : PunchPoints.Max(x => x.RingNumber);
            var nextSequenceIndex = PunchPoints
                .Where(x => x.RingNumber == nextRingNumber)
                .Select(x => x.SequenceIndex)
                .DefaultIfEmpty(0)
                .Max() + 1;

            var point = new PunchPointViewModel(new PunchPoint
            {
                X = 0,
                Y = 0,
                RingNumber = nextRingNumber,
                SequenceIndex = nextSequenceIndex
            });

            PunchPoints.Add(point);
            SelectedPunchPoint = point;
            _logger.Information("已新增配方点位: 圈={RingNumber}, 序号={SequenceIndex}", point.RingNumber, point.SequenceIndex);
        }

        private void RemoveSelectedPunchPoint()
        {
            if (SelectedPunchPoint is null)
            {
                return;
            }

            var removedPoint = SelectedPunchPoint;
            PunchPoints.Remove(removedPoint);
            SelectedPunchPoint = null;
            _logger.Information("已删除配方点位: 圈={RingNumber}, 序号={SequenceIndex}", removedPoint.RingNumber, removedPoint.SequenceIndex);
        }

        private void SortPunchPoints()
        {
            var orderedPoints = PunchPoints
                .OrderBy(x => x.RingNumber)
                .ThenBy(x => x.SequenceIndex)
                .ThenBy(x => x.X)
                .ThenBy(x => x.Y)
                .ToList();

            ApplyOrderedPunchPoints(orderedPoints);
            _logger.Information("配方点位已排序");
        }

        private bool CanRemoveSelectedPunchPoint()
        {
            return SelectedPunchPoint is not null;
        }

        private void PunchPoints_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems.OfType<PunchPointViewModel>())
                {
                    UnsubscribePunchPoint(item);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems.OfType<PunchPointViewModel>())
                {
                    SubscribePunchPoint(item);
                }
            }

            SyncRecipePunchPoints();
            OnPropertyChanged(nameof(PunchPoints));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(RemainingCount));
        }

        private void PunchDepthItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not nameof(RecipeDepthItemViewModel.Value) and not nameof(RecipeDepthItemViewModel.Label))
            {
                return;
            }

            SyncPunchDepthsToRecipe();
            OnPropertyChanged(nameof(FirstPunchTotalDepth));
        }

        private void PunchPoint_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SyncRecipePunchPoints();
            OnPropertyChanged(nameof(PunchPoints));

            if (e.PropertyName != nameof(PunchPointViewModel.Complete))
            {
                return;
            }

            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(RemainingCount));
        }

        private void SubscribePunchPoint(PunchPointViewModel punchPoint)
        {
            punchPoint.PropertyChanged += PunchPoint_PropertyChanged;
        }

        private void SubscribePunchDepthItem(RecipeDepthItemViewModel punchDepthItem)
        {
            punchDepthItem.PropertyChanged += PunchDepthItem_PropertyChanged;
        }

        private void UnsubscribePunchPoint(PunchPointViewModel punchPoint)
        {
            punchPoint.PropertyChanged -= PunchPoint_PropertyChanged;
        }

        private void UnsubscribePunchDepthItem(RecipeDepthItemViewModel punchDepthItem)
        {
            punchDepthItem.PropertyChanged -= PunchDepthItem_PropertyChanged;
        }

        private void ApplyOrderedPunchPoints(IReadOnlyList<PunchPointViewModel> orderedPoints)
        {
            PunchPoints.CollectionChanged -= PunchPoints_CollectionChanged;

            try
            {
                PunchPoints.Clear();

                foreach (var point in orderedPoints)
                {
                    PunchPoints.Add(point);
                }
            }
            finally
            {
                PunchPoints.CollectionChanged += PunchPoints_CollectionChanged;
            }

            SyncRecipePunchPoints();
            OnPropertyChanged(nameof(PunchPoints));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(RemainingCount));
        }

        private void SyncRecipePunchPoints()
        {
            _punchParameters.PunchPoints = PunchPoints.Select(x => x.ToModel()).ToList();
        }

        private void AddPunchDepthItem()
        {
            RecipeDepthItemViewModel? lastItem = null;

            for (int i = 0; i < AddPunchDepthCount; i++)
            {
                lastItem = new RecipeDepthItemViewModel(new RecipeDepthItem
                {
                    Label = $"No.{PunchDepthItems.Count + 1}",
                    Value = NewPunchDepthValue
                });

                PunchDepthItems.Add(lastItem);
            }

            SelectedPunchDepthItem = lastItem;
            _logger.Information("已新增冲孔深度项 {Count} 条, 默认深度 {Depth}", AddPunchDepthCount, NewPunchDepthValue);
        }

        private void RemoveSelectedPunchDepthItem()
        {
            if (SelectedPunchDepthItem is null)
            {
                return;
            }

            var removedItem = SelectedPunchDepthItem;
            PunchDepthItems.Remove(removedItem);
            SelectedPunchDepthItem = PunchDepthItems.LastOrDefault();
            _logger.Information("已删除冲孔深度项: {Label}", removedItem.Label);
        }

        private bool CanRemoveSelectedPunchDepthItem()
        {
            return SelectedPunchDepthItem is not null;
        }

        private void ClearPunchDepthItems()
        {
            PunchDepthItems.Clear();
            SelectedPunchDepthItem = null;
            _logger.Information("已清空全部冲孔深度项");
        }

        private bool CanClearPunchDepthItems()
        {
            return PunchDepthItems.Count > 0;
        }

        private void RenumberPunchDepthLabels()
        {
            for (int i = 0; i < PunchDepthItems.Count; i++)
            {
                var expectedLabel = $"No.{i + 1}";
                if (PunchDepthItems[i].Label != expectedLabel)
                {
                    PunchDepthItems[i].Label = expectedLabel;
                }
            }
        }

        private void SyncPunchDepthsToRecipe()
        {
            ProcessParameters.PunchDepths = PunchDepthItems.Select(x => x.ToModel()).ToList();
            ProcessParameters.FirstPunchDepth = FirstPunchTotalDepth;
        }
    }
}
