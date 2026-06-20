using System;

namespace Fredy.Drilling.Holes.UserControls.HoleGeneration
{
    /// <summary>
    /// 标记一个点位生成 ViewModel，程序启动时自动扫描并加载为生成 Tab。
    /// 新增点位模式只需标记此特性并添加 DataTemplate 映射即可，无需修改任何其他文件。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class GenerationTabAttribute : Attribute
    {
        /// <summary>
        /// Tab 页显示名称。
        /// </summary>
        public string Header { get; }

        /// <summary>
        /// Tab 排序（升序），默认 0。
        /// </summary>
        public int Order { get; set; }

        public GenerationTabAttribute(string header)
        {
            Header = header;
        }
    }
}
