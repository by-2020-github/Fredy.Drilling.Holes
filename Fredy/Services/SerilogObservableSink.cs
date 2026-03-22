using Serilog.Core;
using Serilog.Events;

namespace Fredy.Drilling.Holes.Services
{
    public sealed class SerilogObservableSink : ILogEventSink
    {
        private readonly IAppLogStore _logStore;

        public SerilogObservableSink(IAppLogStore logStore)
        {
            _logStore = logStore;
        }

        public void Emit(LogEvent logEvent)
        {
            _logStore.Add(logEvent);
        }
    }
}
