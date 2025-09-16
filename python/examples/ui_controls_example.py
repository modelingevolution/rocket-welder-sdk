#!/usr/bin/env python3
"""Simple example of UI controls with RocketWelder SDK."""

import asyncio
import os
from typing import Any
from uuid import uuid4

from py_micro_plumberd import EventStoreClient

from rocket_welder_sdk.ui import Color, RegionName, Size, UiService


async def main() -> None:
    """Main entry point for UI controls example."""
    # Setup
    session_id = os.environ.get("SessionId", str(uuid4()))
    eventstore = os.environ.get("EventStore", "esdb://localhost:2113?tls=false")

    print(f"Session ID: {session_id}")

    # Create UI service
    ui = UiService(session_id)
    client = EventStoreClient(eventstore)
    await ui.initialize(client)

    # Create controls
    button = ui.factory.define_icon_button(
        "record-btn",
        icon="M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
        properties={"Color": Color.PRIMARY.value, "Size": Size.LARGE.value},
    )

    label = ui.factory.define_label(
        "status", text="Ready", properties={"Color": Color.TEXT_PRIMARY.value}
    )

    # Add to regions
    ui[RegionName.TOP_RIGHT].append(button)
    ui[RegionName.TOP].append(label)

    # Event handlers with proper types
    def on_button_down(control: Any) -> None:
        """Handle button press."""
        label.text = "Recording..."

    def on_button_up(control: Any) -> None:
        """Handle button release."""
        label.text = "Stopped"

    button.on_button_down = on_button_down
    button.on_button_up = on_button_up

    # Send to UI
    await ui.do()

    # Keep running for 30 seconds
    print("UI controls active for 30 seconds...")
    await asyncio.sleep(30)

    # Cleanup
    button.dispose()
    label.dispose()
    await ui.dispose()
    client.close()


if __name__ == "__main__":
    asyncio.run(main())
