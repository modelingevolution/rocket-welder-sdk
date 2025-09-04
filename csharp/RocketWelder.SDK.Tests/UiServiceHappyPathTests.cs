using System;
using System.Collections.Immutable;
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
                $"Ui.Events-{sessionId}"
            );
            
            // Act 1: Create and add an IconButton to a region
            var controlId = (ControlId)"test-button";
            var iconButton = uiService.Factory.DefineIconButton(
                controlId, 
                "M12,2A10,10", 
                new() { ["color"] = "Primary", ["size"] = "Medium" }
            );
            
            // Add to region - this should schedule DefineControl
            uiService[RegionName.TopRight].Add(iconButton);
            
            // Act 2: Process scheduled definitions
            await uiService.Do();
            
            // Assert 1: DefineControl command should have been sent
            await commandBus.Received(1).SendAsync(
                sessionId,
                Arg.Is<DefineControl>(cmd => 
                    cmd.ControlId == controlId &&
                    cmd.Type == ControlType.IconButton &&
                    cmd.RegionName.ToString() == "TopRight" &&
                    cmd.Properties.ContainsKey("icon") && cmd.Properties["icon"] == "M12,2A10,10" &&
                    cmd.Properties.ContainsKey("color") && cmd.Properties["color"] == "Primary" &&
                    cmd.Properties.ContainsKey("size") && cmd.Properties["size"] == "Medium"
                ),
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
                    cmd.Updates[controlId]["color"] == "Success" &&
                    cmd.Updates[controlId]["text"] == "Clicked!"
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
    }
}