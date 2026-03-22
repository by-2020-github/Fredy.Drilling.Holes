using System;
using System.Windows.Media;
using Serilog.Events;

namespace Fredy.Drilling.Holes.Services
{
    public sealed class AppLogEntry
    {
        public AppLogEntry(DateTimeOffset timestamp, LogEventLevel level, string message)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
        }

        public DateTimeOffset Timestamp { get; }

        public LogEventLevel Level { get; }

        public string Message { get; }

        public string DisplayText => $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Level,-11} {Message}";

        public Brush Foreground => Level switch
        {
            LogEventLevel.Verbose => Brushes.DimGray,
            LogEventLevel.Debug => Brushes.SlateGray,
            LogEventLevel.Information => Brushes.Black,
            LogEventLevel.Warning => Brushes.DarkOrange,
            LogEventLevel.Error => Brushes.Crimson,
            LogEventLevel.Fatal => Brushes.White,
            _ => Brushes.Black
        };

        public Brush Background => Level switch
        {
            LogEventLevel.Fatal => Brushes.Crimson,
            _ => Brushes.Transparent
        };
    }
}
