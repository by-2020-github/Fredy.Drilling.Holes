using Fredy.Drilling.Holes.ViewModels;

namespace Fredy.Drilling.Holes.Views
{
    /// <summary>
    /// 孔位生成工作区 WPF 视图。
    /// </summary>
    public partial class HoleGeneratorView : System.Windows.Controls.UserControl
    {
        public HoleGeneratorView()
        {
            InitializeComponent();
            DataContext = new HoleGeneratorViewModel();
        }
    }
}
