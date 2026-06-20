using Fredy.Drilling.Holes.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Fredy.Drilling.Holes.UserControls.HoleGeneration
{
    /// <summary>
    /// 满天星孔位生成控件。
    /// </summary>
    public partial class StarryGenerationControl : UserControl
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel),
            typeof(StarryGeneratorViewModel),
            typeof(StarryGenerationControl),
            new PropertyMetadata(null));

        public StarryGenerationControl()
        {
            InitializeComponent();
        }

        public StarryGeneratorViewModel? ViewModel
        {
            get => (StarryGeneratorViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }
    }
}
