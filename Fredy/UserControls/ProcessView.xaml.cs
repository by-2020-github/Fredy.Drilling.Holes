using System.Windows;
using System.Windows.Controls;
using Fredy.Drilling.Holes.ViewModels;

namespace Fredy.Drilling.Holes.Views
{
    /// <summary>
    /// ProcessWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ProcessView : UserControl
    {
        public static readonly DependencyProperty RecipeViewModelProperty = DependencyProperty.Register(
            nameof(RecipeViewModel),
            typeof(RecipeViewModel),
            typeof(ProcessView),
            new PropertyMetadata(null));

        public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
            nameof(IsEditing),
            typeof(bool),
            typeof(ProcessView),
            new PropertyMetadata(false));

        public ProcessView()
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
