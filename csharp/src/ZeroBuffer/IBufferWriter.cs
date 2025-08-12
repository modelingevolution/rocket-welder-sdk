using System;
using System.Threading;
using System.Threading.Tasks;

namespace ModelingEvolution.ZeroBuffer
{
    public interface IBufferWriter : IDisposable
    {
        Task WriteAsync(byte[] data, CancellationToken cancellationToken);
    }
}