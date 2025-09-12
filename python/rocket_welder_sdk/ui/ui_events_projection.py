"""UI Events Projection for handling incoming events from the UI."""

from __future__ import annotations

import asyncio
import logging
from contextlib import suppress
from threading import Event as ThreadingEvent
from typing import TYPE_CHECKING, Any, Protocol

from rocket_welder_sdk.external_controls.contracts import (
    ButtonDown,
    ButtonUp,
    KeyDown,
    KeyUp,
)

if TYPE_CHECKING:
    from esdbclient import EventStoreDBClient, RecordedEvent

logger = logging.getLogger(__name__)


class IEventQueue(Protocol):
    """Protocol for event queue."""

    def enqueue_event(self, event: Any) -> None:
        """Add event to the queue."""
        ...


class UiEventsProjection:
    """
    Projection that subscribes to UI events stream and forwards them to the UiService.

    Implements RAII pattern for resource management.
    """

    def __init__(
        self,
        session_id: str,
        event_queue: IEventQueue,
        eventstore_client: EventStoreDBClient,
    ) -> None:
        """
        Initialize the UI events projection.

        Args:
            session_id: The session ID to subscribe to
            event_queue: Queue to forward events to (typically UiService)
            eventstore_client: EventStore client for subscription
        """
        self._session_id: str = session_id
        self._event_queue: IEventQueue = event_queue
        self._eventstore_client: EventStoreDBClient = eventstore_client
        self._stream_name: str = f"Ui.Events-{session_id}"
        self._subscription: Any | None = None
        self._subscription_task: asyncio.Task[None] | None = None
        self._cancellation_token: ThreadingEvent = ThreadingEvent()
        self._is_running: bool = False

    async def start(self) -> None:
        """Start the subscription to UI events."""
        if self._is_running:
            raise RuntimeError("Projection is already running")

        self._is_running = True
        self._cancellation_token.clear()

        # Start subscription task
        self._subscription_task = asyncio.create_task(self._run_subscription())
        logger.info(f"Started UI events projection for session {self._session_id}")

    async def stop(self) -> None:
        """Stop the subscription and clean up resources."""
        if not self._is_running:
            return

        self._is_running = False
        self._cancellation_token.set()

        # Cancel subscription task
        if self._subscription_task:
            self._subscription_task.cancel()
            with suppress(asyncio.CancelledError):
                await self._subscription_task
            self._subscription_task = None

        # Close subscription
        if self._subscription:
            try:
                # Context manager will handle cleanup
                pass
            except Exception as e:
                logger.warning(f"Error closing subscription: {e}")
            finally:
                self._subscription = None

        logger.info(f"Stopped UI events projection for session {self._session_id}")

    async def _run_subscription(self) -> None:
        """Run the subscription loop."""
        while self._is_running and not self._cancellation_token.is_set():
            try:
                # Subscribe from the beginning of the stream
                # Using catch-up subscription to get all events
                with self._eventstore_client.subscribe_to_stream(
                    stream_name=self._stream_name,
                    stream_position=None,  # Start from beginning
                    include_caught_up=True,
                ) as subscription:
                    self._subscription = subscription
                    logger.debug(f"Subscribed to stream {self._stream_name}")

                    for recorded_event in subscription:
                        if self._cancellation_token.is_set():
                            break

                        # Check if this is a caught-up event
                        if hasattr(recorded_event, "is_caught_up") and recorded_event.is_caught_up:
                            logger.debug(f"Caught up with stream {self._stream_name}")
                            continue

                        # Process the event
                        await self._handle_event(recorded_event)

            except asyncio.CancelledError:
                # Task was cancelled, exit cleanly
                break
            except Exception as e:
                logger.error(f"Error in subscription: {e}")
                if self._is_running and not self._cancellation_token.is_set():
                    # Wait before retrying
                    await asyncio.sleep(5)
                else:
                    break

    async def _handle_event(self, recorded_event: RecordedEvent) -> None:
        """
        Handle a recorded event from the stream.

        Args:
            recorded_event: The recorded event from EventStore
        """
        try:
            # Parse event type and data
            event_type: str = recorded_event.type
            # Handle both dict and bytes (esdbclient may return bytes)
            event_data: dict[str, Any]
            if isinstance(recorded_event.data, bytes):
                import json

                event_data = json.loads(recorded_event.data)
            else:
                event_data = recorded_event.data

            # Map event type to control event class
            event_class: type[Any] | None = None

            if event_type == "ButtonDown":
                event_class = ButtonDown
            elif event_type == "ButtonUp":
                event_class = ButtonUp
            elif event_type == "KeyDown":
                event_class = KeyDown
            elif event_type == "KeyUp":
                event_class = KeyUp
            else:
                logger.debug(f"Ignoring unknown event type: {event_type}")
                return

            # Create event instance
            if event_class:
                # Convert from PascalCase to snake_case if needed
                normalized_data = self._normalize_event_data(event_data)
                event = event_class.model_validate(normalized_data)

                # Enqueue event for processing
                self._event_queue.enqueue_event(event)
                logger.debug(f"Enqueued {event_type} for control {event.control_id}")

        except Exception as e:
            logger.error(f"Error handling event {recorded_event.id}: {e}")

    def _normalize_event_data(self, data: dict[str, Any]) -> dict[str, Any]:
        """
        Normalize event data from PascalCase to snake_case.

        Args:
            data: Event data dictionary

        Returns:
            Normalized data dictionary
        """
        # Map common field names
        field_mapping = {
            "ControlId": "control_id",
            "Code": "code",
            "Direction": "direction",
        }

        normalized: dict[str, Any] = {}
        for key, value in data.items():
            # Use mapping if available, otherwise convert to snake_case
            if key in field_mapping:
                normalized[field_mapping[key]] = value
            else:
                # Simple PascalCase to snake_case conversion
                snake_key = key[0].lower() + key[1:]
                normalized[snake_key] = value

        return normalized

    async def __aenter__(self) -> UiEventsProjection:
        """Async context manager entry."""
        await self.start()
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        """Async context manager exit - ensures cleanup."""
        await self.stop()

    @property
    def is_running(self) -> bool:
        """Check if the projection is running."""
        return self._is_running
