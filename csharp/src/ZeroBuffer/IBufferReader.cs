using System;
using System.Threading;
using System.Threading.Tasks;

namespace ModelingEvolution.ZeroBuffer
{
    public interface IBufferReader : IDisposable
    {
        Task<byte[]?> ReadAsync(CancellationToken cancellationToken);
    }
}