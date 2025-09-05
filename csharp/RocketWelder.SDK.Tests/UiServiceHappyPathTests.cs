using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using RocketWelder.SDK.Ui;
using RocketWelder.SDK.Ui.Internals;
using Xunit;
using MicroPlumberd;
using MicroPlumberd.Services;

namespace RocketWelder.SDK.Tests
{
    public class UiServiceHappyPathTests
    {
        [Fact]
        public async Task IconButtonControl_CompleteLifecycle_ShouldSendCorrectCommands()
        {
            // Arrange
            var commandBus = Substitute.For<ICommandBus>();
            var plumber = Substitute.For<IPlumberInstance>();
            var sessionId = Guid.NewGuid();
            var uiService = new UiService(sessionId);
            
            // Initialize with mocked dependencies
            await uiService.InitializeWith(plumber, commandBus);
            
            // Verify subscription was set up
            await plumber.Received(1).SubscribeEventHandler(
                Arg.Any<EventProjection>(), 
                $"Ui.Events-{sessionId}",null,false
            );
            
            // Act 1: Create and add an IconButton to a region
            var controlId = (ControlId)"test-button";
            var iconButton = uiService.Factory.DefineIconButton(
                controlId, 
                "M12,2A10,10", 
                new() { ["Color"] = "Primary", ["Size"] = "Medium" }
            );
            
            // Add to region - this should schedule DefineControl
            uiService[RegionName.TopRight].Add(iconButton);
            
            // Act 2: Process scheduled definitions
            await uiService.Do();
            
            // Assert 1: DefineControl command should have been sent
            var defineControlCalls = commandBus.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == "SendAsync")
                .Select(call => call.GetArguments())
                .Where(args => args.Length >= 2 && args[1] is DefineControl)
                .Select(args => (DefineControl)args[1])
                .FirstOrDefault(cmd => cmd.ControlId == controlId);
            
            Assert.NotNull(defineControlCalls);
            Assert.Equal(controlId, defineControlCalls.ControlId);
            Assert.Equal(ControlType.IconButton, defineControlCalls.Type);
            Assert.Equal("TopRight", defineControlCalls.RegionName.ToString());
            Assert.True(defineControlCalls.Properties.ContainsKey("Icon") && defineControlCalls.Properties["Icon"] == "M12,2A10,10");
            Assert.True(defineControlCalls.Properties.ContainsKey("Color") && defineControlCalls.Properties["Color"] == "Primary");
            Assert.True(defineControlCalls.Properties.ContainsKey("Size") && defineControlCalls.Properties["Size"] == "Medium");
            
            await commandBus.Received(1).SendAsync(
                sessionId,
                Arg.Any<DefineControl>(),
                fireAndForget: true
            );
            
            // Act 3: Simulate button click event
            var buttonDownEvent = new ButtonDown 
            { 
                Id = Guid.NewGuid(),
                ControlId = controlId 
            };
            
            bool buttonDownFired = false;
            iconButton.ButtonDown += (sender, args) => buttonDownFired = true;
            
            // Enqueue event and dispatch it
            await uiService.EnqueueEvent(buttonDownEvent);
            await uiService.Do(); // This will dispatch the event
            
            // Assert 2: Event handler should have been called
            Assert.True(buttonDownFired, "Button down event should have been fired");
            
            // Act 4: Change control properties
            iconButton.Color = Color.Success;
            iconButton.Text = "Clicked!";
            
            // Act 5: Process property updates
            await uiService.Do();
            
            // Assert 3: ChangeControls command should have been sent with updated properties
            await commandBus.Received(1).SendAsync(
                sessionId,
                Arg.Is<ChangeControls>(cmd =>
                    cmd.Updates.ContainsKey(controlId) &&
                    cmd.Updates[controlId]["Color"] == "Success" &&
                    cmd.Updates[controlId]["Text"] == "Clicked!"
                ),
                fireAndForget: true
            );
            
            // Act 6: Dispose control (schedules deletion)
            iconButton.Dispose();
            
            // Act 7: Process scheduled deletions
            await uiService.Do();
            
            // Assert 4: DeleteControls command should have been sent
            await commandBus.Received(1).SendAsync(
                sessionId,
                Arg.Is<DeleteControls>(cmd =>
                    cmd.ControlIds.Contains(controlId)
                ),
                fireAndForget: true
            );
            
            // Final verification: Total number of commands sent
            // 1 DefineControl + 1 ChangeControls + 1 DeleteControls = 3 total
            await commandBus.Received(3).SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<object>(),
                fireAndForget: true
            );
        }
        
        [Fact]
        public async Task ArrowGridControl_KeyboardNavigation_ShouldTranslateEventsCorrectly()
        {
            // Arrange
            var commandBus = Substitute.For<ICommandBus>();
            var plumber = Substitute.For<IPlumberInstance>();
            var sessionId = Guid.NewGuid();
            var uiService = new UiService(sessionId);
            
            await uiService.InitializeWith(plumber, commandBus);
            
            // Act 1: Create and add ArrowGrid control
            var controlId = (ControlId)"nav-grid";
            var arrowGrid = uiService.Factory.DefineArrowGrid(
                controlId,
                new() { ["Size"] = "Large", ["Color"] = "Secondary" }
            );
            
            uiService[RegionName.Bottom].Add(arrowGrid);
            
            // Process definition
            await uiService.Do();
            
            // Assert 1: DefineControl was sent
            await commandBus.Received(1).SendAsync(
                sessionId,
                Arg.Is<DefineControl>(cmd =>
                    cmd.ControlId == controlId &&
                    cmd.Type == ControlType.ArrowGrid &&
                    cmd.RegionName.ToString() == "Bottom"
                ),
                fireAndForget: true
            );
            
            // Act 2: Setup event handlers
            var capturedEvents = new List<(string eventType, ArrowDirection direction)>();
            arrowGrid.ArrowDown += (sender, direction) => capturedEvents.Add(("down", direction));
            arrowGrid.ArrowUp += (sender, direction) => capturedEvents.Add(("up", direction));
            
            // Act 3: Simulate arrow key events
            var directions = new[]
            {
                (KeyCode.ArrowUp, ArrowDirection.Up),
                (KeyCode.ArrowDown, ArrowDirection.Down),
                (KeyCode.ArrowLeft, ArrowDirection.Left),
                (KeyCode.ArrowRight, ArrowDirection.Right)
            };
            
            foreach (var (keyCode, expectedDirection) in directions)
            {
                // Simulate key down
                await uiService.EnqueueEvent(new KeyDown
                {
                    Id = Guid.NewGuid(),
                    ControlId = controlId,
                    Code = keyCode
                });
                
                // Simulate key up
                await uiService.EnqueueEvent(new KeyUp
                {
                    Id = Guid.NewGuid(),
                    ControlId = controlId,
                    Code = keyCode
                });
            }
            
            // Process all events
            await uiService.Do();
            
            // Assert 2: All events were translated correctly
            Assert.Equal(8, capturedEvents.Count); // 4 key downs + 4 key ups
            Assert.Contains(("down", ArrowDirection.Up), capturedEvents);
            Assert.Contains(("up", ArrowDirection.Up), capturedEvents);
            Assert.Contains(("down", ArrowDirection.Down), capturedEvents);
            Assert.Contains(("up", ArrowDirection.Down), capturedEvents);
            Assert.Contains(("down", ArrowDirection.Left), capturedEvents);
            Assert.Contains(("up", ArrowDirection.Left), capturedEvents);
            Assert.Contains(("down", ArrowDirection.Right), capturedEvents);
            Assert.Contains(("up", ArrowDirection.Right), capturedEvents);
            
            // Act 4: Test non-arrow keys (should be ignored)
            capturedEvents.Clear();
            
            await uiService.EnqueueEvent(new KeyDown
            {
                Id = Guid.NewGuid(),
                ControlId = controlId,
                Code = KeyCode.Enter
            });
            
            await uiService.Do();
            
            // Assert 3: Non-arrow keys are ignored
            Assert.Empty(capturedEvents);
            
            // Act 5: Update properties and dispose
            arrowGrid.Color = Color.Warning;
            arrowGrid.Size = Size.ExtraLarge;
            
            await uiService.Do();
            
            // Assert 4: Property changes sent
            await commandBus.Received(1).SendAsync(
                sessionId,
                Arg.Is<ChangeControls>(cmd =>
                    cmd.Updates.ContainsKey(controlId) &&
                    cmd.Updates[controlId].ContainsKey("Color") &&
                    cmd.Updates[controlId]["Color"] == "Warning" &&
                    cmd.Updates[controlId].ContainsKey("Size") &&
                    cmd.Updates[controlId]["Size"] == "ExtraLarge"
                ),
                fireAndForget: true
            );
        }
        
        [Fact]
        public async Task LabelControl_BatchedUpdates_ShouldOptimizeCommands()
        {
            // Arrange
            var commandBus = Substitute.For<ICommandBus>();
            var plumber = Substitute.For<IPlumberInstance>();
            var sessionId = Guid.NewGuid();
            var uiService = new UiService(sessionId);
            
            await uiService.InitializeWith(plumber, commandBus);
            
            // Act 1: Create multiple labels
            var labels = new List<LabelControl>();
            var labelIds = new List<ControlId>();
            
            for (int i = 0; i < 5; i++)
            {
                var labelId = (ControlId)$"label-{i}";
                labelIds.Add(labelId);
                
                var label = uiService.Factory.DefineLabel(
                    labelId,
                    $"Initial Text {i}",
                    new() { ["Typography"] = "body1", ["Color"] = "TextPrimary" }
                );
                labels.Add(label);
            }
            
            // Add to different regions
            uiService[RegionName.Top].Add(labels[0]);
            uiService[RegionName.TopLeft].Add(labels[1]);
            uiService[RegionName.TopRight].Add(labels[2]);
            uiService[RegionName.Bottom].Add(labels[3]);
            uiService[RegionName.BottomLeft].Add(labels[4]);
            
            // Process all definitions
            await uiService.Do();
            
            // Assert 1: All labels defined
            await commandBus.Received(5).SendAsync(
                sessionId,
                Arg.Is<DefineControl>(cmd =>
                    cmd.Type == ControlType.Label &&
                    labelIds.Contains(cmd.ControlId)
                ),
                fireAndForget: true
            );
            
            commandBus.ClearReceivedCalls();
            
            // Act 2: Update all labels at once
            labels[0].Text = "Status: Running";
            labels[0].Typography = Typography.H6;
            labels[0].Color = Color.Success;
            
            labels[1].Text = "Warning Message";
            labels[1].Typography = Typography.Subtitle1;
            labels[1].Color = Color.Warning;
            
            labels[2].Text = "Error Occurred";
            labels[2].Typography = Typography.Caption;
            labels[2].Color = Color.Error;
            
            labels[3].Text = "Info Panel";
            labels[3].Color = Color.Info;
            
            labels[4].Text = "Debug Output";
            labels[4].Typography = Typography.Overline;
            
            // Process all updates in one batch
            await uiService.Do();
            
            // Assert 2: Single ChangeControls command with all updates
            await commandBus.Received(1).SendAsync(
                sessionId,
                Arg.Is<ChangeControls>(cmd =>
                    cmd.Updates.Count == 5 &&
                    // Label 0
                    cmd.Updates[labelIds[0]]["Text"] == "Status: Running" &&
                    cmd.Updates[labelIds[0]]["Typography"] == "h6" &&
                    cmd.Updates[labelIds[0]]["Color"] == "Success" &&
                    // Label 1
                    cmd.Updates[labelIds[1]]["Text"] == "Warning Message" &&
                    cmd.Updates[labelIds[1]]["Typography"] == "subtitle1" &&
                    cmd.Updates[labelIds[1]]["Color"] == "Warning" &&
                    // Label 2
                    cmd.Updates[labelIds[2]]["Text"] == "Error Occurred" &&
                    cmd.Updates[labelIds[2]]["Typography"] == "caption" &&
                    cmd.Updates[labelIds[2]]["Color"] == "Error" &&
                    // Label 3
                    cmd.Updates[labelIds[3]]["Text"] == "Info Panel" &&
                    cmd.Updates[labelIds[3]]["Color"] == "Info" &&
                    // Label 4
                    cmd.Updates[labelIds[4]]["Text"] == "Debug Output" &&
                    cmd.Updates[labelIds[4]]["Typography"] == "overline"
                ),
                fireAndForget: true
            );
            
            // Act 3: No changes, no command should be sent
            commandBus.ClearReceivedCalls();
            await uiService.Do();
            
            // Assert 3: No commands sent when nothing changed
            await commandBus.DidNotReceive().SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<object>(),
                fireAndForget: Arg.Any<bool>()
            );
            
            // Act 4: Dispose some labels
            labels[0].Dispose();
            labels[2].Dispose();
            labels[4].Dispose();
            
            await uiService.Do();
            
            // Assert 4: Batch delete command sent
            await commandBus.Received(1).SendAsync(
                sessionId,
                Arg.Is<DeleteControls>(cmd =>
                    cmd.ControlIds.Count == 3 &&
                    cmd.ControlIds.Contains(labelIds[0]) &&
                    cmd.ControlIds.Contains(labelIds[2]) &&
                    cmd.ControlIds.Contains(labelIds[4])
                ),
                fireAndForget: true
            );
            
            // Act 5: Update remaining labels
            commandBus.ClearReceivedCalls();
            labels[1].Text = "Still Active";
            labels[3].Text = "Also Active";
            
            await uiService.Do();
            
            // Assert 5: Only active controls are updated
            await commandBus.Received(1).SendAsync(
                sessionId,
                Arg.Is<ChangeControls>(cmd =>
                    cmd.Updates.Count == 2 &&
                    cmd.Updates[labelIds[1]]["Text"] == "Still Active" &&
                    cmd.Updates[labelIds[3]]["Text"] == "Also Active"
                ),
                fireAndForget: true
            );
        }
    }
}