// Re-export types from RocketWelder.SDK.Shared for backwards compatibility
// Existing code using RocketWelder.SDK.SessionStreamId will continue to work

global using RocketWelder.SDK.Shared;

// Type aliases for backwards compatibility with the RocketWelder.SDK namespace
namespace RocketWelder.SDK;

// Note: Types are now defined in RocketWelder.SDK.Shared namespace.
// The global using above makes them available without qualification.
// For explicit RocketWelder.SDK namespace usage, add type aliases here if needed.
