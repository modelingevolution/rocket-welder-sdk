"""UI Service for managing controls and commands."""

from collections import UserList
from typing import Dict, List, Optional, Tuple
from uuid import UUID

from py_micro_plumberd import EventStoreClient, CommandBus

from ..external_controls.contracts import (
    ChangeControls,
    DefineControl,
    DeleteControls,
    RocketWelderControlType,
)
from .controls import (
    ArrowGridControl,
    ControlBase,
    ControlType,
    IconButtonControl,
    LabelControl,
    RegionName,
)


class ItemsControl(UserList):
    """Collection of controls for a region with automatic command scheduling."""
    
    def __init__(self, ui_service: 'UiService', region_name: RegionName):
        """
        Initialize items control for a region.
        
        Args:
            ui_service: Parent UiService
            region_name: Region where controls are placed
        """
        super().__init__()
        self._ui_service = ui_service
        self._region_name = region_name
    
    def append(self, item: ControlBase) -> None:
        """Add control and schedule DefineControl command."""
        if not isinstance(item, ControlBase):
            raise TypeError("Only ControlBase instances can be added")
        
        # Schedule DefineControl command
        self._ui_service.schedule_define_control(item, self._region_name)
        super().append(item)
    
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
    
    def __init__(self, ui_service: 'UiService'):
        """
        Initialize factory with UiService reference.
        
        Args:
            ui_service: Parent UiService
        """
        self._ui_service = ui_service
    
    def define_icon_button(
        self,
        control_id: str,
        icon: str,
        properties: Optional[Dict[str, str]] = None
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
        self,
        control_id: str,
        properties: Optional[Dict[str, str]] = None
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
        self,
        control_id: str,
        text: str,
        properties: Optional[Dict[str, str]] = None
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
    
    def __init__(self, session_id: str):
        """
        Initialize UiService with session ID.
        
        Args:
            session_id: UI session ID for command routing
        """
        self.session_id = session_id
        self.command_bus: Optional[CommandBus] = None
        self.factory = UiControlFactory(self)
        
        # Control tracking
        self._index: Dict[str, ControlBase] = {}
        
        # Scheduled operations
        self._scheduled_definitions: List[Tuple[ControlBase, RegionName]] = []
        self._scheduled_deletions: List[str] = []
        
        # Initialize regions
        self._regions = {
            RegionName.TOP: ItemsControl(self, RegionName.TOP),
            RegionName.TOP_LEFT: ItemsControl(self, RegionName.TOP_LEFT),
            RegionName.TOP_RIGHT: ItemsControl(self, RegionName.TOP_RIGHT),
            RegionName.BOTTOM: ItemsControl(self, RegionName.BOTTOM),
            RegionName.BOTTOM_LEFT: ItemsControl(self, RegionName.BOTTOM_LEFT),
            RegionName.BOTTOM_RIGHT: ItemsControl(self, RegionName.BOTTOM_RIGHT),
        }
        
        # Event queue
        self._event_queue: List[any] = []
    
    def __getitem__(self, region: RegionName) -> ItemsControl:
        """Get controls for a region."""
        return self._regions[region]
    
    async def initialize(self, eventstore_client: EventStoreClient) -> None:
        """
        Initialize with EventStore client.
        
        Args:
            eventstore_client: EventStore client for commands
        """
        self.command_bus = CommandBus(eventstore_client)
        # TODO: Subscribe to Ui.Events-{session_id} for incoming events
    
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
    
    def enqueue_event(self, event: any) -> None:
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
            if hasattr(event, 'control_id'):
                control = self._index.get(event.control_id)
                if control:
                    control.handle_event(event)
        self._event_queue.clear()
    
    async def _process_scheduled_definitions(self) -> None:
        """Process scheduled DefineControl commands."""
        for control, region in self._scheduled_definitions:
            # Add to index when actually defining
            self._index[control.id] = control
            
            # Map control type
            if control.control_type == ControlType.ICON_BUTTON:
                rw_type = RocketWelderControlType.ICON_BUTTON
            elif control.control_type == ControlType.ARROW_GRID:
                rw_type = RocketWelderControlType.ARROW_GRID
            elif control.control_type == ControlType.LABEL:
                rw_type = RocketWelderControlType.LABEL
            else:
                continue
            
            command = DefineControl(
                control_id=control.id,
                type=rw_type,
                properties=control.properties,
                region_name=region.value
            )
            
            await self.command_bus.send_async(
                recipient_id=self.session_id,
                command=command
            )
            
            control.commit_changes()
        
        self._scheduled_definitions.clear()
    
    async def _process_scheduled_deletions(self) -> None:
        """Process scheduled DeleteControls commands."""
        if not self._scheduled_deletions:
            return
        
        # Batch delete command
        command = DeleteControls(control_ids=self._scheduled_deletions.copy())
        
        await self.command_bus.send_async(
            recipient_id=self.session_id,
            command=command
        )
        
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
        updates = {}
        
        for region in self._regions.values():
            for control in region:
                if control.is_dirty:
                    updates[control.id] = control.changed
        
        if updates:
            command = ChangeControls(updates=updates)
            
            await self.command_bus.send_async(
                recipient_id=self.session_id,
                command=command
            )
            
            # Commit changes
            for control_id in updates:
                self._index[control_id].commit_changes()