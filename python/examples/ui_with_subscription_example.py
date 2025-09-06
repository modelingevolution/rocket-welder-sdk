"""Example demonstrating UI controls with event subscription."""

import asyncio
import logging
from typing import Any
from uuid import uuid4

from py_micro_plumberd import EventStoreClient

from rocket_welder_sdk.ui import (
    Color,
    RegionName,
    Size,
    UiService,
)

# Setup logging to see what's happening
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


async def main() -> None:
    """Main example demonstrating UI controls with subscription."""
    # Create session ID
    session_id = str(uuid4())
    logger.info(f"Starting UI service with session {session_id}")

    # Create EventStore client
    connection_string = "esdb://localhost:2113?tls=false"
    eventstore_client = EventStoreClient(connection_string)

    # Create UI service
    ui_service = UiService(session_id)

    # Use context manager for proper cleanup
    async with ui_service:
        # Initialize with EventStore (starts subscription)
        await ui_service.initialize(eventstore_client)
        logger.info("UI service initialized with event subscription")

        # Create some controls
        button = ui_service.factory.define_icon_button(
            control_id="submit-button",
            icon="M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z",  # Plus icon
            properties={
                "Color": Color.PRIMARY.value,
                "Size": Size.LARGE.value,
            },
        )

        # Set up event handler
        def on_button_click(control: Any) -> None:
            logger.info(f"Button {control.id} was clicked!")
            # Update button appearance
            control.color = Color.SUCCESS
            control.text = "Submitted!"

        button.on_button_down = on_button_click

        # Add button to UI
        ui_service[RegionName.TOP_RIGHT].append(button)

        # Create a label
        label = ui_service.factory.define_label(
            control_id="status-label",
            text="Ready for input",
            properties={
                "Color": Color.TEXT_PRIMARY.value,
            },
        )
        ui_service[RegionName.TOP].append(label)

        # Process initial definitions
        await ui_service.do()
        logger.info("Controls defined and sent to UI")

        # Simulate receiving events for 30 seconds
        logger.info("Waiting for UI events (30 seconds)...")
        await asyncio.sleep(30)

        # Update label before closing
        label.text = "Closing application..."
        label.color = Color.WARNING
        await ui_service.do()

        logger.info("Shutting down...")

    # Cleanup
    eventstore_client.close()
    logger.info("Example complete")


if __name__ == "__main__":
    asyncio.run(main())
