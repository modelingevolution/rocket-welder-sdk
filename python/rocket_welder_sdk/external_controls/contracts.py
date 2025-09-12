"""External Controls contracts using Pydantic v2 for modern serialization.

This module defines the contracts for external controls communication between
containers and the RocketWelder UI via EventStore, using Pydantic v2 for
proper serialization with PascalCase support.
"""

from enum import Enum
from uuid import UUID, uuid4

from pydantic import BaseModel, ConfigDict, Field, field_serializer
from pydantic.alias_generators import to_pascal

from rocket_welder_sdk.ui.value_types import ControlType


class ArrowDirection(str, Enum):
    """Arrow directions for arrow grid control."""

    UP = "Up"
    DOWN = "Down"
    LEFT = "Left"
    RIGHT = "Right"


class BaseContract(BaseModel):
    """Base class for all contracts with PascalCase serialization."""

    model_config = ConfigDict(
        # Convert snake_case fields to PascalCase for serialization
        alias_generator=to_pascal,
        # Allow both snake_case and PascalCase when deserializing
        populate_by_name=True,
        # Use Enum values for serialization
        use_enum_values=True,
    )

    id: UUID = Field(default_factory=uuid4)

    @field_serializer("id")
    def serialize_id(self, value: UUID) -> str:
        """Serialize UUID as string for JSON compatibility."""
        return str(value)


# Container → UI Commands (Stream: ExternalCommands-{SessionId})


class DefineControl(BaseContract):
    """Command to define a new control in the UI."""

    control_id: str
    type: ControlType
    properties: dict[str, str]
    region_name: str


class DeleteControls(BaseContract):
    """Command to delete multiple controls from the UI."""

    control_ids: list[str]


# Legacy alias for backward compatibility (will be removed)
DeleteControl = DeleteControls


class ChangeControls(BaseContract):
    """Command to update properties of multiple controls."""

    updates: dict[str, dict[str, str]]  # ControlId -> {PropertyId -> Value}


# UI → Container Events (Stream: ExternalEvents-{SessionId})


class ButtonDown(BaseContract):
    """Event when a button is pressed down."""

    control_id: str


class ButtonUp(BaseContract):
    """Event when a button is released."""

    control_id: str


class KeyDown(BaseContract):
    """Event when a key is pressed down."""

    control_id: str
    code: str  # KeyCode value like "ArrowUp", "Enter", etc.


class KeyUp(BaseContract):
    """Event when a key is released."""

    control_id: str
    code: str  # KeyCode value like "ArrowUp", "Enter", etc.
