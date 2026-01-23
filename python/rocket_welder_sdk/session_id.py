"""SessionId parsing utilities.

SessionId format: ps-{guid} (e.g., ps-a1b2c3d4-e5f6-7890-abcd-ef1234567890)
Prefix "ps" = PipelineSession.

This module provides utilities to:
1. Parse SessionId from environment variable
2. Extract the Guid portion

## URL Configuration

Output URLs are configured via environment variables set by rocket-welder2:
- SEGMENTATION_SINK_URL: URL for segmentation output (e.g., socket:///tmp/seg.sock)
- KEYPOINTS_SINK_URL: URL for keypoints output (e.g., socket:///tmp/kp.sock)
- ACTIONS_SINK_URL: URL for actions output
"""

from __future__ import annotations

import os
import uuid

SESSION_ID_PREFIX = "ps-"
SESSION_ID_ENV_VAR = "SessionId"

# Explicit URL environment variables (set by rocket-welder2)
SEGMENTATION_SINK_URL_ENV = "SEGMENTATION_SINK_URL"
KEYPOINTS_SINK_URL_ENV = "KEYPOINTS_SINK_URL"
ACTIONS_SINK_URL_ENV = "ACTIONS_SINK_URL"
GRAPHICS_SINK_URL_ENV = "GRAPHICS_SINK_URL"


def parse_session_id(session_id: str) -> uuid.UUID:
    """Parse SessionId (ps-{guid}) to extract Guid.

    Args:
        session_id: SessionId string (e.g., "ps-a1b2c3d4-...")

    Returns:
        UUID extracted from SessionId

    Raises:
        ValueError: If session_id format is invalid

    Examples:
        >>> parse_session_id("ps-a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        UUID('a1b2c3d4-e5f6-7890-abcd-ef1234567890')
        >>> parse_session_id("a1b2c3d4-e5f6-7890-abcd-ef1234567890")  # backwards compat
        UUID('a1b2c3d4-e5f6-7890-abcd-ef1234567890')
    """
    if session_id.startswith(SESSION_ID_PREFIX):
        return uuid.UUID(session_id[len(SESSION_ID_PREFIX) :])
    # Fallback: try parsing as raw guid for backwards compatibility
    return uuid.UUID(session_id)


def get_session_id_from_env() -> str | None:
    """Get SessionId from environment variable.

    Returns:
        SessionId string or None if not set
    """
    return os.environ.get(SESSION_ID_ENV_VAR)
