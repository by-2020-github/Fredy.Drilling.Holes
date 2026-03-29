using Common.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public partial class RecipeViewModel : ObservableObject
    {
        private readonly Recipe _recipe;
        private readonly RecipePunchParameters _punchParameters;
        private string _recipeName = string.Empty;
        private string _typeName = string.Empty;
        private double _radius;
        private int _rings;
        private PunchPointViewModel? _selectedPunchPoint;

        public RecipeViewModel(Recipe recipe)
        {
            _recipe = recipe;
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

            PunchPoints.CollectionChanged += PunchPoints_CollectionChanged;

            AddPunchPointCommand = new RelayCommand(AddPunchPoint);
            RemoveSelectedPunchPointCommand = new RelayCommand(RemoveSelectedPunchPoint, CanRemoveSelectedPunchPoint);
            SortPunchPointsCommand = new RelayCommand(SortPunchPoints);

            foreach (var punchPoint in PunchPoints)
            {
                SubscribePunchPoint(punchPoint);
            }
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

        public RecipeProcessParameters ProcessParameters { get; }

        public RecipePunchParameters PunchParameters => _punchParameters;

        public Recipe Recipe => _recipe;

        public ObservableCollection<PunchPointViewModel> PunchPoints { get; }

        public IRelayCommand AddPunchPointCommand { get; }

        public IRelayCommand RemoveSelectedPunchPointCommand { get; }

        public IRelayCommand SortPunchPointsCommand { get; }

        public int TotalCount => PunchPoints.Count;

        public int CompletedCount => PunchPoints.Count(x => x.Complete);

        public int RemainingCount => Math.Max(0, TotalCount - CompletedCount);

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
        }

        private void RemoveSelectedPunchPoint()
        {
            if (SelectedPunchPoint is null)
            {
                return;
            }

            PunchPoints.Remove(SelectedPunchPoint);
            SelectedPunchPoint = null;
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

        private void UnsubscribePunchPoint(PunchPointViewModel punchPoint)
        {
            punchPoint.PropertyChanged -= PunchPoint_PropertyChanged;
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
    }
}
