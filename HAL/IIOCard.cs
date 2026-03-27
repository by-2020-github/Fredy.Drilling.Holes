using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HAL
{
    public interface IIOCard
    {
        int InputCount { get; }

        int OutputCount { get; }

        Task<bool> ReadInputAsync(int portNo, CancellationToken cancellationToken = default);

        Task<IReadOnlyDictionary<int, bool>> ReadInputsAsync(int[] portNos, CancellationToken cancellationToken = default);

        Task<bool> ReadOutputAsync(int portNo, CancellationToken cancellationToken = default);

        Task<IReadOnlyDictionary<int, bool>> ReadOutputsAsync(int[] portNos, CancellationToken cancellationToken = default);

        Task WriteOutputAsync(int portNo, bool value, CancellationToken cancellationToken = default);

        Task WriteOutputsAsync(IReadOnlyDictionary<int, bool> outputs, CancellationToken cancellationToken = default);
    }
}
