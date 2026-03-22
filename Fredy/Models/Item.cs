using CommunityToolkit.Mvvm.ComponentModel;

namespace Fredy.Models
{
    public partial class Item : ObservableObject
    {
        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private bool isDone;

        public Item() { }

        public Item(string title)
        {
            Title = title;
        }
    }
}
