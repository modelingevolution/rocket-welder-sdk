using System;
using System.Threading.Tasks;
using MicroPlumberd.Testing;
using Xunit;

namespace RocketWelder.SDK.Tests;

/// <summary>
/// xUnit collection fixture that provides a shared EventStore instance for tests.
/// Uses MicroPlumberd.Testing to spin up an in-memory EventStore container.
/// </summary>
public class EventStoreFixture : IAsyncLifetime
{
    private readonly EventStoreServer _eventStore = new();

    /// <summary>
    /// Gets the EventStore connection string in esdb:// format.
    /// </summary>
    public string ConnectionString => _eventStore.HttpUrl?.ToString()
        ?? throw new InvalidOperationException("EventStore not started");

    /// <summary>
    /// Gets the EventStore server instance.
    /// </summary>
    public EventStoreServer Server => _eventStore;

    public async Task InitializeAsync()
    {
        await _eventStore.StartInDocker(wait: true, inMemory: true);
    }

    public async Task DisposeAsync()
    {
        await _eventStore.DisposeAsync();
    }
}

/// <summary>
/// Collection definition for tests requiring EventStore.
/// Tests using [Collection("EventStore")] will share a single EventStore instance.
/// </summary>
[CollectionDefinition("EventStore")]
public class EventStoreCollection : ICollectionFixture<EventStoreFixture>
{
    // This class has no code, it's just for collection definition
}
