using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HAL
{
    public sealed class IOCardSimulator : IIOCard
    {
        private readonly ConcurrentDictionary<int, bool> _inputs = new();
        private readonly ConcurrentDictionary<int, bool> _outputs = new();

        public int InputCount { get; }

        public int OutputCount { get; }

        public IOCardSimulator(int inputCount = 24, int outputCount = 9)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(inputCount);
            ArgumentOutOfRangeException.ThrowIfNegative(outputCount);

            InputCount = inputCount;
            OutputCount = outputCount;

            for (var i = 0; i < inputCount; i++)
            {
                _inputs[i] = false;
            }

            for (var i = 0; i < outputCount; i++)
            {
                _outputs[i] = false;
            }
        }

        public Task<bool> ReadInputAsync(int portNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateInputPort(portNo);
            return Task.FromResult(_inputs[portNo]);
        }

        public Task<IReadOnlyDictionary<int, bool>> ReadInputsAsync(int[] portNos, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(portNos);

            IReadOnlyDictionary<int, bool> result = ReadPorts(portNos, ValidateInputPort, _inputs);
            return Task.FromResult(result);
        }

        public Task<bool> ReadOutputAsync(int portNo, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateOutputPort(portNo);
            return Task.FromResult(_outputs[portNo]);
        }

        public Task<IReadOnlyDictionary<int, bool>> ReadOutputsAsync(int[] portNos, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(portNos);

            IReadOnlyDictionary<int, bool> result = ReadPorts(portNos, ValidateOutputPort, _outputs);
            return Task.FromResult(result);
        }

        public Task WriteOutputAsync(int portNo, bool value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateOutputPort(portNo);
            _outputs[portNo] = value;
            return Task.CompletedTask;
        }

        public Task WriteOutputsAsync(IReadOnlyDictionary<int, bool> outputs, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(outputs);

            foreach (var output in outputs)
            {
                ValidateOutputPort(output.Key);
                _outputs[output.Key] = output.Value;
            }

            return Task.CompletedTask;
        }

        public Task SetInputAsync(int portNo, bool value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateInputPort(portNo);
            _inputs[portNo] = value;
            return Task.CompletedTask;
        }

        public Task SetInputsAsync(IReadOnlyDictionary<int, bool> inputs, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(inputs);

            foreach (var input in inputs)
            {
                ValidateInputPort(input.Key);
                _inputs[input.Key] = input.Value;
            }

            return Task.CompletedTask;
        }

        private static IReadOnlyDictionary<int, bool> ReadPorts(
            int[] portNos,
            Action<int> validatePort,
            ConcurrentDictionary<int, bool> values)
        {
            var result = new Dictionary<int, bool>(portNos.Length);
            foreach (var portNo in portNos)
            {
                validatePort(portNo);
                result[portNo] = values[portNo];
            }

            return result;
        }

        private void ValidateInputPort(int portNo)
        {
            if (portNo < 0 || portNo >= InputCount)
            {
                throw new ArgumentOutOfRangeException(nameof(portNo), portNo, $"Input port must be between 0 and {InputCount - 1}.");
            }
        }

        private void ValidateOutputPort(int portNo)
        {
            if (portNo < 0 || portNo >= OutputCount)
            {
                throw new ArgumentOutOfRangeException(nameof(portNo), portNo, $"Output port must be between 0 and {OutputCount - 1}.");
            }
        }
    }
}