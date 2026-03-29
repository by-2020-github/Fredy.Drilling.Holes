using Fredy.Drilling.Holes.ViewModels;
using Fredy.Drilling.Holes.Views;
using System.Windows;
using System.Windows.Controls;

namespace Fredy.Drilling.Holes.UserControls
{
    /// <summary>
    /// RecipeEditUserControl.xaml 的交互逻辑
    /// </summary>
    public partial class RecipeEditUserControl : UserControl
    {
        private Window? _holeGeneratorWindow;

        public static readonly DependencyProperty RecipeViewModelProperty = DependencyProperty.Register(
            nameof(RecipeViewModel),
            typeof(RecipeViewModel),
            typeof(RecipeEditUserControl),
            new PropertyMetadata(null, OnRecipeContextChanged));

        public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
            nameof(IsEditing),
            typeof(bool),
            typeof(RecipeEditUserControl),
            new PropertyMetadata(false, OnRecipeContextChanged));

        public RecipeEditUserControl()
        {
            InitializeComponent();
            Unloaded += RecipeEditUserControl_Unloaded;
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

        private static void OnRecipeContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((RecipeEditUserControl)d).SyncHoleGeneratorViewContext();
        }

        private void OpenHoleGeneratorWindowButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditing)
            {
                return;
            }

            if (_holeGeneratorWindow is { IsVisible: true })
            {
                SyncHoleGeneratorViewContext();
                _holeGeneratorWindow.Activate();
                return;
            }

            var generatorView = new HoleGeneratorView
            {
                RecipeViewModel = RecipeViewModel,
                IsEditing = IsEditing,
                MinHeight = 620,
                MinWidth = 980
            };

            _holeGeneratorWindow = new Window
            {
                Title = "点集生成器",
                Content = generatorView,
                Width = 1180,
                Height = 820,
                MinWidth = 1000,
                MinHeight = 680,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };

            _holeGeneratorWindow.Closed += (_, _) => _holeGeneratorWindow = null;
            _holeGeneratorWindow.Show();
        }

        private void SyncHoleGeneratorViewContext()
        {
            if (_holeGeneratorWindow?.Content is not HoleGeneratorView view)
            {
                return;
            }

            view.RecipeViewModel = RecipeViewModel;
            view.IsEditing = IsEditing;
        }

        private void RecipeEditUserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_holeGeneratorWindow is null)
            {
                return;
            }

            _holeGeneratorWindow.Close();
            _holeGeneratorWindow = null;
        }
    }
}
