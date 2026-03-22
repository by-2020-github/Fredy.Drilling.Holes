using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fredy.Drilling.Holes.Services
{
    public interface IAppLogExportService
    {
        Task<string?> ExportAsync(IEnumerable<AppLogEntry> entries, CancellationToken cancellationToken = default);
    }
}
