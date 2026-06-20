using Fredy.Drilling.Holes.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Fredy.Drilling.Holes.UserControls.HoleGeneration
{
    /// <summary>
    /// 环形喷丝板孔位生成控件。
    /// </summary>
    public partial class RingSpinneretGenerationControl : UserControl
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel),
            typeof(RingSpinneretGeneratorViewModel),
            typeof(RingSpinneretGenerationControl),
            new PropertyMetadata(null));

        public RingSpinneretGenerationControl()
        {
            InitializeComponent();
        }

        public RingSpinneretGeneratorViewModel? ViewModel
        {
            get => (RingSpinneretGeneratorViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }
    }
}
