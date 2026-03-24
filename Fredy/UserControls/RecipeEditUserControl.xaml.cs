using Fredy.Drilling.Holes.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Fredy.Drilling.Holes.UserControls
{
    /// <summary>
    /// RecipeEditUserControl.xaml 的交互逻辑
    /// </summary>
    public partial class RecipeEditUserControl : UserControl
    {
        public static readonly DependencyProperty RecipeViewModelProperty = DependencyProperty.Register(
            nameof(RecipeViewModel),
            typeof(RecipeViewModel),
            typeof(RecipeEditUserControl),
            new PropertyMetadata(null));

        public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
            nameof(IsEditing),
            typeof(bool),
            typeof(RecipeEditUserControl),
            new PropertyMetadata(false));

        public RecipeEditUserControl()
        {
            InitializeComponent();
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
    }
}
