using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BLL
{
    public sealed record HardwareStateSnapshot(
        bool IsMotionCardReady,
        bool IsCameraConnected,
        double X,
        double Y,
        double Z,
        IReadOnlyDictionary<int, bool> Inputs,
        IReadOnlyDictionary<int, bool> Outputs,
        DateTime Timestamp)
    {
        public static HardwareStateSnapshot Empty { get; } = new(
            false,
            false,
            0d,
            0d,
            0d,
            new Dictionary<int, bool>(),
            new Dictionary<int, bool>(),
            DateTime.MinValue);
    }

    public sealed class HardwareStateChangedEventArgs : EventArgs
    {
        public HardwareStateChangedEventArgs(HardwareStateSnapshot state)
        {
            State = state;
        }

        public HardwareStateSnapshot State { get; }
    }

    public interface IHardwareStateService : IDisposable
    {
        int InputCount { get; }

        int OutputCount { get; }

        HardwareStateSnapshot CurrentState { get; }

        event EventHandler<HardwareStateChangedEventArgs>? StateChanged;

        Task RefreshAsync(CancellationToken cancellationToken = default);
    }
}