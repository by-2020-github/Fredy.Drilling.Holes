using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Serilog.Events;

namespace Fredy.Drilling.Holes.Services
{
    public sealed class AppLogStore : IAppLogStore
    {
        private const int MaxLogEntries = 500;
        private readonly ObservableCollection<AppLogEntry> _entries = new();
        private readonly Dispatcher _dispatcher;

        public AppLogStore()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            Entries = new ReadOnlyObservableCollection<AppLogEntry>(_entries);
        }

        public int Capacity => MaxLogEntries;

        public ReadOnlyObservableCollection<AppLogEntry> Entries { get; }

        public void Add(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);

            var message = logEvent.RenderMessage();
            if (logEvent.Exception is not null)
            {
                message = $"{message} | {logEvent.Exception.Message}";
            }

            var entry = new AppLogEntry(logEvent.Timestamp, logEvent.Level, message);

            if (_dispatcher.CheckAccess())
            {
                Append(entry);
                return;
            }

            _dispatcher.Invoke(() => Append(entry));
        }

        public void Clear()
        {
            if (_dispatcher.CheckAccess())
            {
                _entries.Clear();
                return;
            }

            _dispatcher.Invoke(_entries.Clear);
        }

        private void Append(AppLogEntry entry)
        {
            _entries.Add(entry);
            while (_entries.Count > MaxLogEntries)
            {
                _entries.RemoveAt(0);
            }
        }
    }
}
