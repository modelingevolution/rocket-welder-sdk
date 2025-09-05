# UI Events Subscription Implementation

## Overview
Implemented the missing event subscription mechanism for receiving events from the UI back to the container, completing the bidirectional communication pattern.

## Architecture

### Key Components

1. **UiEventsProjection** (`ui_events_projection.py`)
   - Separate projection class implementing RAII pattern
   - Subscribes to `Ui.Events-{session_id}` stream
   - Handles incoming events: ButtonDown, ButtonUp, KeyDown, KeyUp
   - Forwards events to UiService's event queue for processing

2. **Integration with UiService**
   - Projection starts automatically on `initialize()`
   - Cleans up properly on `dispose()` or via context manager
   - Events flow: EventStore → Projection → Event Queue → Controls

## Design Decisions

### Separation of Concerns
- **Projection as separate class**: Following single responsibility principle
- **Not in py-micro-plumberd**: Too complex for that library at this time
- **Lives in rocket-welder-sdk**: Where the domain logic belongs

### Resource Management (RAII)
```python
async with ui_service:  # Context manager ensures cleanup
    await ui_service.initialize(eventstore_client)
    # ... use the service
# Automatic cleanup: stops projection, disposes controls
```

### Event Subscription Pattern
- Uses catch-up subscription from esdbclient
- Starts from beginning of stream
- Handles reconnection with retry logic
- Processes events asynchronously

### Type Safety
- Full type annotations throughout
- Protocol for IEventQueue interface
- TYPE_CHECKING imports for circular dependencies

## Implementation Details

### Event Flow
1. UI generates event (e.g., user clicks button)
2. Event written to `Ui.Events-{session_id}` stream
3. UiEventsProjection receives via subscription
4. Event normalized from PascalCase to snake_case
5. Event enqueued in UiService
6. Next `do()` call dispatches to appropriate control
7. Control's event handler executes

### Error Handling
- Subscription errors logged and retried
- Invalid events logged but don't crash subscription
- Graceful cancellation via threading.Event

### Testing
- Mocked projection in tests to avoid EventStore dependency
- All existing tests pass with new subscription
- Happy path tests verify complete lifecycle

## Usage Example
```python
# Initialize with subscription
ui_service = UiService(session_id)
async with ui_service:
    await ui_service.initialize(eventstore_client)
    
    # Create controls
    button = ui_service.factory.define_icon_button(...)
    button.on_button_down = lambda c: print("Clicked!")
    
    # Process commands and events
    await ui_service.do()
```

## Benefits
- **Complete bidirectional communication**: Commands to UI, Events from UI
- **Clean separation**: Projection isolated from core service logic
- **Robust**: Handles disconnections, retries, errors
- **Enterprise-ready**: Full typing, RAII, proper logging
- **Testable**: Easily mocked for unit tests