# RocketWelder.SDK.Types

Value types and abstractions for the RocketWelder SDK ecosystem.

## Purpose

This package contains lightweight, dependency-minimal types that are shared across:
- `RocketWelder.SDK` - Full SDK with video streaming capabilities
- `RocketWelder.SDK.Blazor` - Blazor components for rendering
- `ModelingEvolution.RocketWelder.Contracts` - Event/command contracts

## Types

### SessionStreamId

Strongly-typed identifier for streaming sessions.

```csharp
// Create new
var id = SessionStreamId.New();

// From existing Guid
var id = SessionStreamId.From(existingGuid);

// Parse from string (format: ps-{guid})
var id = SessionStreamId.Parse("ps-a1b2c3d4-e5f6-7890-abcd-ef1234567890");

// Implicit conversion to Guid
Guid guid = id;

// Implicit conversion to string
string s = id; // "ps-a1b2c3d4-..."
```

### VideoRecordingIdentifier

Strongly-typed identifier for video recordings.

```csharp
// Create new
var id = VideoRecordingIdentifier.New();

// From existing Guid
var id = VideoRecordingIdentifier.From(existingGuid);

// Parse from string (format: rec-{guid})
var id = VideoRecordingIdentifier.Parse("rec-a1b2c3d4-e5f6-7890-abcd-ef1234567890");
```

## Design Principles

- **Minimal dependencies**: Only `ModelingEvolution.JsonParsableConverter` for JSON serialization
- **No ASP.NET Core references**: Safe for use in Blazor WASM projects
- **IParsable<T> support**: Works with model binding and JSON serialization
- **Immutable**: All types are `readonly record struct`

## License

MIT
