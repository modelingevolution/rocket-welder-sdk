#!/usr/bin/env python3
"""UI controls with event subscription example."""

import asyncio
import logging
from typing import Any
from uuid import uuid4

from py_micro_plumberd import EventStoreClient

from rocket_welder_sdk.ui import Color, RegionName, Size, UiService

logging.basicConfig(level=logging.INFO)


async def main() -> None:
    """Main entry point for UI subscription example."""
    session_id = str(uuid4())
    print(f"Session: {session_id}")

    # Create UI with EventStore subscription
    ui = UiService(session_id)
    async with ui:
        client = EventStoreClient("esdb://localhost:2113?tls=false")
        await ui.initialize(client)

        # Create button
        button = ui.factory.define_icon_button(
            "submit",
            icon="M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z",  # Plus icon
            properties={"Color": Color.PRIMARY.value, "Size": Size.LARGE.value},
        )

        # Handle clicks with proper type annotation
        def on_click(control: Any) -> None:
            """Handle button click event."""
            logging.info(f"Button clicked: {control.id}")
            control.color = Color.SUCCESS
            control.text = "Done!"

        button.on_button_down = on_click
        ui[RegionName.TOP_RIGHT].append(button)

        # Send controls
        await ui.do()

        # Wait for events
        print("Waiting 30 seconds for UI events...")
        await asyncio.sleep(30)

    client.close()
    print("Done")


if __name__ == "__main__":
    asyncio.run(main())
