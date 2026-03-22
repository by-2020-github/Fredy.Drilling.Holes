using System.Collections.ObjectModel;
using Serilog.Events;

namespace Fredy.Drilling.Holes.Services
{
    public interface IAppLogStore
    {
        int Capacity { get; }

        ReadOnlyObservableCollection<AppLogEntry> Entries { get; }

        void Add(LogEvent logEvent);

        void Clear();
    }
}
