using System;
using System.Collections.Generic;
using System.Net;

namespace RocketWelder.SDK.Internal;

/// <summary>
/// Helper for parsing connection string query parameters.
/// Shared by all connection string types to avoid code duplication.
/// </summary>
internal static class ConnectionStringParser
{
    /// <summary>
    /// Extracts query parameters from a connection string.
    /// </summary>
    /// <param name="input">The full connection string (e.g., "protocol://path?foo=bar&baz=qux")</param>
    /// <param name="endpointPart">The part before the query string (e.g., "protocol://path")</param>
    /// <param name="parameters">Dictionary to populate with parsed parameters (keys are lowercase)</param>
    public static void ExtractQueryParameters(
        string input,
        out string endpointPart,
        Dictionary<string, string> parameters)
    {
        var queryIndex = input.IndexOf('?');
        endpointPart = input;

        if (queryIndex >= 0)
        {
            var queryString = input[(queryIndex + 1)..];
            endpointPart = input[..queryIndex];

            foreach (var pair in queryString.Split('&'))
            {
                var keyValue = pair.Split('=', 2);
                if (keyValue.Length == 2 && !string.IsNullOrEmpty(keyValue[0]))
                    parameters[keyValue[0].ToLowerInvariant()] = WebUtility.UrlDecode(keyValue[1]);
            }
        }
    }
}
