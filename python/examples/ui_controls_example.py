"""Example usage of UI controls with RocketWelder SDK."""

import asyncio
import os
from typing import Any
from uuid import uuid4

from py_micro_plumberd import EventStoreClient

from rocket_welder_sdk.ui import (
    Color,
    RegionName,
    Size,
    Typography,
    UiService,
)


async def main() -> None:
    """Example of using UI controls."""

    # Get session ID from environment or generate one
    session_id = os.environ.get("ROCKET_WELDER_SESSION_ID", str(uuid4()))
    print(f"Using session ID: {session_id}")

    # Create EventStore client
    connection_string = os.environ.get(
        "EventStore", "esdb://localhost:2113?tls=false"  # noqa: SIM112 - Must match C# SDK
    )
    eventstore_client = EventStoreClient(connection_string)

    # Create UI service
    ui_service = UiService(session_id)
    await ui_service.initialize(eventstore_client)

    print("Creating UI controls...")

    # Create some controls using the factory
    factory = ui_service.factory

    # Create an icon button in top-right region
    button = factory.define_icon_button(
        control_id="btn-record",
        icon="M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
        properties={"Color": Color.PRIMARY.value, "Size": Size.LARGE.value, "Text": "Record"},
    )
    ui_service[RegionName.TOP_RIGHT].append(button)

    # Create a status label in top region
    label = factory.define_label(
        control_id="lbl-status",
        text="Ready",
        properties={"Typography": Typography.H6.value, "Color": Color.TEXT_PRIMARY.value},
    )
    ui_service[RegionName.TOP].append(label)

    # Create arrow grid in bottom region
    arrows = factory.define_arrow_grid(
        control_id="arrow-nav",
        properties={"Size": Size.MEDIUM.value, "Color": Color.SECONDARY.value},
    )
    ui_service[RegionName.BOTTOM].append(arrows)

    # Set up event handlers
    def on_button_down(control: Any) -> None:
        print(f"Button {control.id} pressed!")
        # Update label when button is pressed
        label.text = "Recording..."
        label.color = Color.ERROR

    def on_button_up(control: Any) -> None:
        print(f"Button {control.id} released!")
        label.text = "Recording stopped"
        label.color = Color.SUCCESS

    def on_arrow(control: Any, direction: Any) -> None:
        print(f"Arrow {direction.value} pressed")
        label.text = f"Direction: {direction.value}"

    button.on_button_down = on_button_down
    button.on_button_up = on_button_up
    arrows.on_arrow_down = on_arrow

    # Process all scheduled commands
    print("Sending control definitions to UI...")
    await ui_service.do()

    # Simulate some changes
    await asyncio.sleep(1)

    # Update button properties
    print("Updating button color...")
    button.color = Color.SUCCESS
    button.text = "Recording Active"

    # Process updates
    await ui_service.do()

    # Simulate button press (would normally come from UI events)
    from rocket_welder_sdk.external_controls import ButtonDown, ButtonUp

    print("Simulating button press...")
    ui_service.enqueue_event(ButtonDown(control_id=button.id))
    await ui_service.do()

    await asyncio.sleep(1)

    ui_service.enqueue_event(ButtonUp(control_id=button.id))
    await ui_service.do()

    # Clean up
    await asyncio.sleep(1)
    print("Cleaning up...")
    button.dispose()
    label.dispose()
    arrows.dispose()
    await ui_service.do()

    print("Done!")


if __name__ == "__main__":
    asyncio.run(main())
