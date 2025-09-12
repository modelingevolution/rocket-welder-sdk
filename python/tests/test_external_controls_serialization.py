"""Cross-platform serialization tests for External Controls contracts."""

import json
from pathlib import Path
from uuid import UUID

import pytest

from rocket_welder_sdk.external_controls import (
    ButtonDown,
    ButtonUp,
    ChangeControls,
    ControlType,
    DefineControl,
    DeleteControls,
)


class TestExternalControlsSerialization:
    """Test round-trip serialization of all event types."""

    @pytest.fixture(autouse=True)
    def setup(self):
        """Create output directory for test files."""
        self.output_path = Path("test_output")
        self.output_path.mkdir(exist_ok=True)

    def test_define_control_round_trip(self):
        """Test DefineControl serialization and deserialization."""
        define_control = DefineControl(
            id=UUID("12345678-1234-1234-1234-123456789012"),
            control_id="test-button",
            type=ControlType.ICON_BUTTON,
            properties={
                "Icon": "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                "Color": "Primary",
                "Size": "Medium",
            },
            region_name="preview-top-right",
        )
        self._test_round_trip(define_control, "DefineControl")

    def test_delete_controls_round_trip(self):
        """Test DeleteControls serialization and deserialization."""
        delete_controls = DeleteControls(
            id=UUID("23456789-2345-2345-2345-234567890123"),
            control_ids=["test-label", "test-button"],
        )
        self._test_round_trip(delete_controls, "DeleteControls")

    def test_change_controls_round_trip(self):
        """Test ChangeControls serialization and deserialization."""
        change_controls = ChangeControls(
            id=UUID("34567890-3456-3456-3456-345678901234"),
            updates={
                "test-button": {
                    "Text": "Clicked!",
                    "Color": "Success",
                },
                "test-label": {
                    "Text": "Status: Running",
                },
            },
        )
        self._test_round_trip(change_controls, "ChangeControls")

    def test_button_down_round_trip(self):
        """Test ButtonDown serialization and deserialization."""
        button_down = ButtonDown(
            id=UUID("45678901-4567-4567-4567-456789012345"),
            control_id="test-button",
        )
        self._test_round_trip(button_down, "ButtonDown")

    def test_button_up_round_trip(self):
        """Test ButtonUp serialization and deserialization."""
        button_up = ButtonUp(
            id=UUID("56789012-5678-5678-5678-567890123456"),
            control_id="test-button",
        )
        self._test_round_trip(button_up, "ButtonUp")

    def _test_round_trip(self, original, type_name: str):
        """Test serialization and deserialization of a single object."""
        # Serialize using model_dump with by_alias=True (returns PascalCase for EventStore)
        data = original.model_dump(by_alias=True, mode="json")

        # Write to file (no conversion needed - EventStore expects PascalCase)
        file_path = self.output_path / f"{type_name}_python.json"
        with open(file_path, "w") as f:
            json.dump(data, f, indent=2)

        # Test deserialization using model_validate
        # No conversion needed - data is already in PascalCase
        deserialized = original.__class__.model_validate(data)

        # Verify round-trip
        if hasattr(original, "control_id"):
            assert deserialized.control_id == original.control_id
        if hasattr(original, "control_ids"):
            assert deserialized.control_ids == original.control_ids
        assert deserialized.id == original.id
        if hasattr(original, "direction"):
            assert deserialized.direction == original.direction

        # Verify we can serialize again
        data2 = original.model_dump(by_alias=True, mode="json")
        assert data == data2
