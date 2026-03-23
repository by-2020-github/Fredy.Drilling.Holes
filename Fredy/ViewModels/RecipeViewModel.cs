using Common.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Fredy.Drilling.Holes.ViewModels
{
    public partial class PunchPointViewModel : ObservableObject
    {
        private bool _complete;

        public PunchPointViewModel(PunchPoint punchPoint)
        {
            X = punchPoint.X;
            Y = punchPoint.Y;
            RingNumber = punchPoint.RingNumber;
            SequenceIndex = punchPoint.SequenceIndex;
        }

        public double X { get; }

        public double Y { get; }

        public int RingNumber { get; }

        public int SequenceIndex { get; }

        public bool Complete
        {
            get => _complete;
            set => SetProperty(ref _complete, value);
        }
    }

    public partial class RecipeViewModel : ObservableObject
    {
        public RecipeViewModel(Recipe recipe)
        {
            RecipeName = recipe.RecipeName;
            TypeName = recipe.TypeName;
            Radius = recipe.Radius;
            Rings = recipe.Rings;
            PunchPoints = new ObservableCollection<PunchPointViewModel>(recipe.PunchPoints
                .OrderBy(x => x.RingNumber)
                .ThenBy(x => x.SequenceIndex)
                .Select(x => new PunchPointViewModel(x)));

            foreach (var punchPoint in PunchPoints)
            {
                punchPoint.PropertyChanged += PunchPoint_PropertyChanged;
            }
        }

        public string RecipeName { get; }

        public string TypeName { get; }

        public double Radius { get; }

        public int Rings { get; }

        public ObservableCollection<PunchPointViewModel> PunchPoints { get; }

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

        private void PunchPoint_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PunchPointViewModel.Complete))
            {
                return;
            }

            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(RemainingCount));
        }
    }
}
