using System;

namespace ModelingEvolution.ZeroBuffer
{
    public interface IDuplexChannel : IDisposable
    {
        IBufferReader Reader { get; }
        IBufferWriter Writer { get; }
    }
}