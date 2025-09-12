"""UI Service for managing controls and commands."""

from __future__ import annotations

from collections import UserList
from typing import Any

from py_micro_plumberd import CommandBus, EventStoreClient

from rocket_welder_sdk.external_controls.contracts import (
    ChangeControls,
    DefineControl,
    DeleteControls,
)

from .controls import (
    ArrowGridControl,
    ControlBase,
    IconButtonControl,
    LabelControl,
)
from .ui_events_projection import UiEventsProjection
from .value_types import RegionName


class ItemsControl(UserList[ControlBase]):
    """Collection of controls for a region with automatic command scheduling."""

    data: list[ControlBase]  # Type annotation for the data attribute

    def __init__(self, ui_service: UiService, region_name: RegionName) -> None:
        """
        Initialize items control for a region.

        Args:
            ui_service: Parent UiService
            region_name: Region where controls are placed
        """
        super().__init__()
        self._ui_service: UiService = ui_service
        self._region_name: RegionName = region_name

    def append(self, item: ControlBase) -> None:
        """Add control and schedule DefineControl command."""
        if not isinstance(item, ControlBase):
            raise TypeError("Only ControlBase instances can be added")

        # Schedule DefineControl command
        self._ui_service.schedule_define_control(item, self._region_name)
        super().append(item)

    def add(self, item: ControlBase) -> None:
        """Add control (alias for append to match C# API)."""
        self.append(item)

    def remove(self, item: ControlBase) -> None:
        """Remove control and schedule deletion."""
        if item in self.data:
            self._ui_service.schedule_delete(item.id)
            super().remove(item)

    def clear(self) -> None:
        """Clear all controls and schedule deletions."""
        for control in self.data:
            self._ui_service.schedule_delete(control.id)
        super().clear()


class UiControlFactory:
    """Factory for creating UI controls."""

    def __init__(self, ui_service: UiService) -> None:
        """
        Initialize factory with UiService reference.

        Args:
            ui_service: Parent UiService
        """
        self._ui_service: UiService = ui_service

    def define_icon_button(
        self, control_id: str, icon: str, properties: dict[str, str] | None = None
    ) -> IconButtonControl:
        """
        Create an icon button control.

        Args:
            control_id: Unique identifier
            icon: SVG path for the icon
            properties: Additional properties

        Returns:
            Created IconButtonControl
        """
        control = IconButtonControl(control_id, self._ui_service, icon, properties)
        self._ui_service.register_control(control)
        return control

    def define_arrow_grid(
        self, control_id: str, properties: dict[str, str] | None = None
    ) -> ArrowGridControl:
        """
        Create an arrow grid control.

        Args:
            control_id: Unique identifier
            properties: Additional properties

        Returns:
            Created ArrowGridControl
        """
        control = ArrowGridControl(control_id, self._ui_service, properties)
        self._ui_service.register_control(control)
        return control

    def define_label(
        self, control_id: str, text: str, properties: dict[str, str] | None = None
    ) -> LabelControl:
        """
        Create a label control.

        Args:
            control_id: Unique identifier
            text: Label text
            properties: Additional properties

        Returns:
            Created LabelControl
        """
        control = LabelControl(control_id, self._ui_service, text, properties)
        self._ui_service.register_control(control)
        return control


class UiService:
    """Main service for managing UI controls and commands."""

    @classmethod
    def from_session_id(cls, session_id: str | Any) -> UiService:
        """
        Create UiService from session ID.

        Args:
            session_id: Session ID (string or UUID)

        Returns:
            New UiService instance
        """
        # Handle UUID or string
        session_str = str(session_id) if not isinstance(session_id, str) else session_id
        return cls(session_str)

    def __init__(self, session_id: str) -> None:
        """
        Initialize UiService with session ID.

        Args:
            session_id: UI session ID for command routing
        """
        self.session_id: str = session_id
        self.command_bus: CommandBus | None = None
        self.factory: UiControlFactory = UiControlFactory(self)

        # Control tracking
        self._index: dict[str, ControlBase] = {}

        # Scheduled operations
        self._scheduled_definitions: list[tuple[ControlBase, RegionName]] = []
        self._scheduled_deletions: list[str] = []

        # Initialize regions - include all standard and preview regions
        self._regions: dict[RegionName, ItemsControl] = {
            RegionName.TOP: ItemsControl(self, RegionName.TOP),
            RegionName.TOP_LEFT: ItemsControl(self, RegionName.TOP_LEFT),
            RegionName.TOP_RIGHT: ItemsControl(self, RegionName.TOP_RIGHT),
            RegionName.BOTTOM: ItemsControl(self, RegionName.BOTTOM),
            RegionName.BOTTOM_LEFT: ItemsControl(self, RegionName.BOTTOM_LEFT),
            RegionName.BOTTOM_RIGHT: ItemsControl(self, RegionName.BOTTOM_RIGHT),
            # Preview regions for compatibility
            RegionName.PREVIEW_TOP: ItemsControl(self, RegionName.PREVIEW_TOP),
            RegionName.PREVIEW_TOP_LEFT: ItemsControl(self, RegionName.PREVIEW_TOP_LEFT),
            RegionName.PREVIEW_TOP_RIGHT: ItemsControl(self, RegionName.PREVIEW_TOP_RIGHT),
            RegionName.PREVIEW_BOTTOM: ItemsControl(self, RegionName.PREVIEW_BOTTOM),
            RegionName.PREVIEW_BOTTOM_LEFT: ItemsControl(self, RegionName.PREVIEW_BOTTOM_LEFT),
            RegionName.PREVIEW_BOTTOM_RIGHT: ItemsControl(self, RegionName.PREVIEW_BOTTOM_RIGHT),
            RegionName.PREVIEW_BOTTOM_CENTER: ItemsControl(self, RegionName.PREVIEW_BOTTOM_CENTER),
        }

        # Event queue
        self._event_queue: list[Any] = []

        # Event projection
        self._events_projection: UiEventsProjection | None = None

    def __getitem__(self, region: RegionName) -> ItemsControl:
        """Get controls for a region."""
        return self._regions[region]

    async def initialize(self, eventstore_client: EventStoreClient) -> None:
        """
        Initialize with EventStore client and start event projection.

        Args:
            eventstore_client: EventStore client for commands and events
        """
        self.command_bus = CommandBus(eventstore_client)

        # Start the events projection to receive UI events
        self._events_projection = UiEventsProjection(
            session_id=self.session_id,
            event_queue=self,  # UiService implements the IEventQueue protocol
            eventstore_client=eventstore_client._client,  # Use the underlying esdbclient
        )
        await self._events_projection.start()

    async def dispose(self) -> None:
        """Dispose the service and clean up resources."""
        # Stop the events projection
        if self._events_projection:
            await self._events_projection.stop()
            self._events_projection = None

        # Clear all controls
        for control_id in list(self._index.keys()):
            control = self._index[control_id]
            control.dispose()

        # Clear regions
        for region in self._regions.values():
            region.clear()

    def register_control(self, control: ControlBase) -> None:
        """
        Register a control in the index.

        Args:
            control: Control to register
        """
        self._index[control.id] = control

    def schedule_define_control(self, control: ControlBase, region: RegionName) -> None:
        """
        Schedule a DefineControl command.

        Args:
            control: Control to define
            region: Region where control is placed
        """
        self._scheduled_definitions.append((control, region))

    def schedule_delete(self, control_id: str) -> None:
        """
        Schedule a control deletion.

        Args:
            control_id: ID of control to delete
        """
        self._scheduled_deletions.append(control_id)

    def enqueue_event(self, event: Any) -> None:
        """
        Enqueue an event for processing.

        Args:
            event: Event to enqueue
        """
        self._event_queue.append(event)

    async def do(self) -> None:
        """Process all scheduled operations and events."""
        # Dispatch events
        self._dispatch_events()

        # Process scheduled definitions
        await self._process_scheduled_definitions()

        # Process scheduled deletions
        await self._process_scheduled_deletions()

        # Send property updates
        await self._send_property_updates()

    def _dispatch_events(self) -> None:
        """Dispatch queued events to controls."""
        for event in self._event_queue:
            if hasattr(event, "control_id"):
                control_id: str = event.control_id
                control = self._index.get(control_id)
                if control:
                    control.handle_event(event)
        self._event_queue.clear()

    async def _process_scheduled_definitions(self) -> None:
        """Process scheduled DefineControl commands."""
        for control, region in self._scheduled_definitions:
            # Add to index when actually defining
            self._index[control.id] = control

            command = DefineControl(
                control_id=control.id,
                type=control.control_type,
                properties=control.properties,
                region_name=region.value,
            )

            if self.command_bus:
                await self.command_bus.send_async(recipient_id=self.session_id, command=command)

            control.commit_changes()

        self._scheduled_definitions.clear()

    async def _process_scheduled_deletions(self) -> None:
        """Process scheduled DeleteControls commands."""
        if not self._scheduled_deletions:
            return

        # Batch delete command
        command = DeleteControls(control_ids=self._scheduled_deletions.copy())

        if self.command_bus:
            await self.command_bus.send_async(recipient_id=self.session_id, command=command)

        # Remove from index and regions
        for control_id in self._scheduled_deletions:
            control = self._index.pop(control_id, None)
            if control:
                for region in self._regions.values():
                    if control in region:
                        region.data.remove(control)

        self._scheduled_deletions.clear()

    async def _send_property_updates(self) -> None:
        """Send ChangeControls command for dirty controls."""
        updates: dict[str, dict[str, str]] = {}

        for region in self._regions.values():
            for control in region:
                if control.is_dirty:
                    updates[control.id] = control.changed

        if updates and self.command_bus:
            command = ChangeControls(updates=updates)

            await self.command_bus.send_async(recipient_id=self.session_id, command=command)

            # Commit changes
            for control_id in updates:
                self._index[control_id].commit_changes()

    async def __aenter__(self) -> UiService:
        """Async context manager entry."""
        return self

    async def __aexit__(self, exc_type: Any, exc_val: Any, exc_tb: Any) -> None:
        """Async context manager exit - ensures cleanup."""
        await self.dispose()
