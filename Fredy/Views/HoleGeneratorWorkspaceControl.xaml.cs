using Common.Models;
using Fredy.Drilling.Holes.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Fredy.Drilling.Holes.Views
{
    /// <summary>
    /// 自动根据 ViewModel 类型从资源字典中查找对应的 DataTemplate。
    /// 无需手动注册，DataTemplate 在 Themes/GenerationTabDataTemplates.xaml 中统一维护。
    /// </summary>
    public class GenerationTabTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is not GenerationTabDescriptor tab || tab.ViewModel is null)
                return null;

            var element = container as FrameworkElement;
            var key = new DataTemplateKey(tab.ViewModel.GetType());
            return element?.TryFindResource(key) as DataTemplate;
        }
    }

    /// <summary>
    /// 孔位生成工作区 WPF 视图。
    /// </summary>
    public partial class HoleGeneratorView : UserControl
    {
        public static readonly DependencyProperty RecipeViewModelProperty = DependencyProperty.Register(
            nameof(RecipeViewModel),
            typeof(RecipeViewModel),
            typeof(HoleGeneratorView),
            new PropertyMetadata(null));

        public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
            nameof(IsEditing),
            typeof(bool),
            typeof(HoleGeneratorView),
            new PropertyMetadata(false));

        public HoleGeneratorView()
        {
            InitializeComponent();
            DataContext = new HoleGeneratorViewModel(ApplyGeneratedPointsToRecipe);
        }

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

        private void ApplyGeneratedPointsToRecipe(IReadOnlyList<HoleCoordinate> coordinates)
        {
            if (!IsEditing || RecipeViewModel is null || coordinates.Count == 0)
            {
                return;
            }

            var groupedByRing = coordinates
                .GroupBy(c => Math.Max(1, c.CircleIndex))
                .OrderBy(g => g.Key);

            var punchPoints = new List<PunchPointViewModel>();
            foreach (var ring in groupedByRing)
            {
                var orderedRingPoints = ring
                    .OrderBy(p => p.PointIndex)
                    .ThenBy(p => p.SectorIndex)
                    .ThenBy(p => p.X)
                    .ThenBy(p => p.Y)
                    .ToList();

                for (int i = 0; i < orderedRingPoints.Count; i++)
                {
                    var coordinate = orderedRingPoints[i];
                    punchPoints.Add(new PunchPointViewModel(new PunchPoint
                    {
                        X = coordinate.X,
                        Y = coordinate.Y,
                        RingNumber = ring.Key,
                        SequenceIndex = i + 1
                    }));
                }
            }

            RecipeViewModel.ReplacePunchPoints(punchPoints);
        }
    }
}
