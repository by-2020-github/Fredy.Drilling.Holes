using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fredy.Drilling.Holes.Services
{
    public sealed class AppLogExportService : IAppLogExportService
    {
        public async Task<string?> ExportAsync(IEnumerable<AppLogEntry> entries, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entries);

            var dialog = new SaveFileDialog
            {
                Title = "导出日志",
                Filter = "文本文件 (*.txt)|*.txt|日志文件 (*.log)|*.log|所有文件 (*.*)|*.*",
                DefaultExt = ".txt",
                AddExtension = true,
                FileName = $"fredy-logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
            };

            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            var lines = entries.Select(static entry => entry.DisplayText).ToArray();
            await File.WriteAllLinesAsync(dialog.FileName, lines, Encoding.UTF8, cancellationToken);
            return dialog.FileName;
        }
    }
}
