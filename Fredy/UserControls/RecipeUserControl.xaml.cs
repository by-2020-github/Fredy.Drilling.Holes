using Fredy.Drilling.Holes.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fredy.Drilling.Holes.UserControls
{
    /// <summary>
    /// RecipeUserControl.xaml 的交互逻辑
    /// </summary>
    public sealed class RecipePointVisual
    {
        public required double Left { get; init; }

        public required double Top { get; init; }

        public required Brush Fill { get; init; }

        public required string ToolTip { get; init; }
    }

    public partial class RecipeUserControl : UserControl
    {
        private const double MapSize = 360;
        private const double PointSize = 10;
        private const double MapPadding = 24;
        private static readonly Brush CompletedBrush = Brushes.ForestGreen;
        private static readonly Brush PendingBrush = Brushes.Gray;

        public static readonly DependencyProperty RecipeViewModelProperty = DependencyProperty.Register(
            nameof(RecipeViewModel),
            typeof(RecipeViewModel),
            typeof(RecipeUserControl),
            new PropertyMetadata(null, OnRecipeViewModelChanged));

        public RecipeUserControl()
        {
            InitializeComponent();
        }

        public ObservableCollection<RecipePointVisual> DisplayPoints { get; } = new();

        public RecipeViewModel? RecipeViewModel
        {
            get => (RecipeViewModel?)GetValue(RecipeViewModelProperty);
            set => SetValue(RecipeViewModelProperty, value);
        }

        private static void OnRecipeViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (RecipeUserControl)d;

            if (e.OldValue is RecipeViewModel oldViewModel)
            {
                oldViewModel.PropertyChanged -= control.RecipeViewModel_PropertyChanged;
            }

            if (e.NewValue is RecipeViewModel newViewModel)
            {
                newViewModel.PropertyChanged += control.RecipeViewModel_PropertyChanged;
            }

            control.RefreshMap();
        }

        private void RecipeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RefreshMap();
        }

        private void RefreshMap()
        {
            DisplayPoints.Clear();

            if (RecipeViewModel?.PunchPoints is not { Count: > 0 } punchPoints || RecipeViewModel.Radius <= 0)
            {
                return;
            }

            var drawableRadius = (MapSize / 2) - MapPadding;

            foreach (var point in punchPoints)
            {
                var centerX = (MapSize / 2) + ((point.X / RecipeViewModel.Radius) * drawableRadius);
                var centerY = (MapSize / 2) - ((point.Y / RecipeViewModel.Radius) * drawableRadius);

                DisplayPoints.Add(new RecipePointVisual
                {
                    Left = centerX - (PointSize / 2),
                    Top = centerY - (PointSize / 2),
                    Fill = point.Complete ? CompletedBrush : PendingBrush,
                    ToolTip = $"圈:{point.RingNumber} 序号:{point.SequenceIndex} 坐标:({point.X:N3}, {point.Y:N3}) 状态:{(point.Complete ? "已完成" : "未完成")}"
                });
            }
        }
    }
}
