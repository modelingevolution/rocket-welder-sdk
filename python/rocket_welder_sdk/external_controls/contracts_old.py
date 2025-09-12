"""External Controls event contracts for RocketWelder SDK (legacy - for backward compatibility)."""

from dataclasses import dataclass, field
from enum import Enum
from uuid import UUID, uuid4

from rocket_welder_sdk.ui.value_types import ControlType


class ArrowDirection(Enum):
    """Arrow directions for ArrowGrid control."""

    UP = "Up"
    DOWN = "Down"
    LEFT = "Left"
    RIGHT = "Right"


# Container → UI Commands (Stream: ExternalCommands-{SessionId})


@dataclass
class DefineControl:
    """Command to define a new control in the UI."""

    control_id: str
    type: ControlType
    properties: dict[str, str]
    region_name: str
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> dict[str, object]:
        """Convert to dictionary for EventStore."""
        return {
            "Id": str(self.id),
            "ControlId": self.control_id,
            "Type": self.type.value,
            "Properties": self.properties,
            "RegionName": self.region_name,
        }


@dataclass
class DeleteControl:
    """Command to delete a control from the UI."""

    control_id: str
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> dict[str, str]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "ControlId": self.control_id}


@dataclass
class ChangeControls:
    """Command to update properties of multiple controls."""

    updates: dict[str, dict[str, str]]  # ControlId -> { PropertyId -> Value }
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> dict[str, object]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "Updates": self.updates}


# UI → Container Events (Stream: ExternalEvents-{SessionId})


@dataclass
class ButtonDown:
    """Event when button is pressed."""

    control_id: str
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> dict[str, str]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "ControlId": self.control_id}

    @classmethod
    def from_dict(cls, data: dict[str, object]) -> "ButtonDown":
        """Create from EventStore data."""
        return cls(
            control_id=str(data["ControlId"]), id=UUID(str(data["Id"])) if "Id" in data else uuid4()
        )


@dataclass
class ButtonUp:
    """Event when button is released."""

    control_id: str
    id: UUID = field(default_factory=uuid4)

    def to_dict(self) -> dict[str, str]:
        """Convert to dictionary for EventStore."""
        return {"Id": str(self.id), "ControlId": self.control_id}

    @classmethod
    def from_dict(cls, data: dict[str, object]) -> "ButtonUp":
        """Create from EventStore data."""
        return cls(
            control_id=str(data["ControlId"]), id=UUID(str(data["Id"])) if "Id" in data else uuid4()
        )
