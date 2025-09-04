using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RocketWelder.SDK.Ui;
using RocketWelder.SDK.Ui.Internals;
using Xunit;

namespace RocketWelder.SDK.Tests
{
    public class ExternalControlsSerializationTests
    {
        private readonly string _outputPath = Path.Combine(Directory.GetCurrentDirectory(), "test_output");
        private readonly JsonSerializerOptions _jsonOptions;

        public ExternalControlsSerializationTests()
        {
            Directory.CreateDirectory(_outputPath);
            
            _jsonOptions = new JsonSerializerOptions
            {
                // EventStore uses PascalCase by default for .NET events
                // Don't set PropertyNamingPolicy to keep default PascalCase
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        [Fact]
        public void DefineControl_RoundTrip()
        {
            var defineControl = new DefineControl
            {
                Id = Guid.Parse("12345678-1234-1234-1234-123456789012"),
                ControlId = "test-button",
                Type = ControlType.IconButton,
                Properties = new Dictionary<string, string>
                {
                    ["icon"] = "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                    ["color"] = "Primary",
                    ["size"] = "Medium"
                },
                RegionName = (RegionName)"preview-top-right"
            };
            TestRoundTrip(defineControl, "DefineControl");
        }

        [Fact]
        public void DeleteControls_RoundTrip()
        {
            var deleteControls = new DeleteControls
            {
                Id = Guid.Parse("23456789-2345-2345-2345-234567890123"),
                ControlIds = ImmutableHashSet<ControlId>.Empty
                    .Add((ControlId)"test-button")
                    .Add((ControlId)"test-label")
            };
            TestRoundTrip(deleteControls, "DeleteControls");
        }

        [Fact]
        public void ChangeControls_RoundTrip()
        {
            var changeControls = new ChangeControls
            {
                Id = Guid.Parse("34567890-3456-3456-3456-345678901234"),
                Updates = ImmutableDictionary<ControlId, ImmutableDictionary<string, string>>.Empty
                    .Add((ControlId)"test-button", ImmutableDictionary<string, string>.Empty
                        .Add("text", "Clicked!")
                        .Add("color", "Success"))
                    .Add((ControlId)"test-label", ImmutableDictionary<string, string>.Empty
                        .Add("text", "Status: Running"))
            };
            TestRoundTrip(changeControls, "ChangeControls");
        }

        [Fact]
        public void ButtonDown_RoundTrip()
        {
            var buttonDown = new ButtonDown
            {
                Id = Guid.Parse("45678901-4567-4567-4567-456789012345"),
                ControlId = "test-button"
            };
            TestRoundTrip(buttonDown, "ButtonDown");
        }

        [Fact]
        public void ButtonUp_RoundTrip()
        {
            var buttonUp = new ButtonUp
            {
                Id = Guid.Parse("56789012-5678-5678-5678-567890123456"),
                ControlId = "test-button"
            };
            TestRoundTrip(buttonUp, "ButtonUp");
        }

        [Fact]
        public void ArrowDown_RoundTrip()
        {
            var arrowDown = new ArrowDown
            {
                Id = Guid.Parse("67890123-6789-6789-6789-678901234567"),
                ControlId = "test-arrow",
                Direction = ArrowDirection.Up
            };
            TestRoundTrip(arrowDown, "ArrowDown");
        }

        [Fact]
        public void ArrowUp_RoundTrip()
        {
            var arrowUp = new ArrowUp
            {
                Id = Guid.Parse("78901234-7890-7890-7890-789012345678"),
                ControlId = "test-arrow",
                Direction = ArrowDirection.Down
            };
            TestRoundTrip(arrowUp, "ArrowUp");
        }

        private void TestRoundTrip<T>(T original, string typeName) where T : class
        {
            // Serialize
            var json = JsonSerializer.Serialize(original, _jsonOptions);
            var filePath = Path.Combine(_outputPath, $"{typeName}_csharp.json");
            File.WriteAllText(filePath, json);
            
            // Deserialize
            var deserialized = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            
            // Verify not null
            Assert.NotNull(deserialized);
            
            // Verify JSON can be re-serialized identically
            var json2 = JsonSerializer.Serialize(deserialized, _jsonOptions);
            Assert.Equal(json, json2);
        }
    }
}