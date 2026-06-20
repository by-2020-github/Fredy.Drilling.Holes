using Fredy.Drilling.Holes.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Fredy.Drilling.Holes.UserControls.HoleGeneration
{
    /// <summary>
    /// 扇区盘（橘子瓣）孔位生成控件。
    /// </summary>
    public partial class SectorGenerationControl : UserControl
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel),
            typeof(SectorGeneratorViewModel),
            typeof(SectorGenerationControl),
            new PropertyMetadata(null));

        public SectorGenerationControl()
        {
            InitializeComponent();
        }

        public SectorGeneratorViewModel? ViewModel
        {
            get => (SectorGeneratorViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }
    }
}
