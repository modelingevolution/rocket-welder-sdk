"""SessionId parsing utilities for NNG URL generation.

SessionId format: ps-{guid} (e.g., ps-a1b2c3d4-e5f6-7890-abcd-ef1234567890)
Prefix "ps" = PipelineSession.

This module provides utilities to:
1. Parse SessionId from environment variable
2. Extract the Guid portion
3. Generate NNG IPC URLs for streaming results
"""

from __future__ import annotations

import logging
import os
import uuid

logger = logging.getLogger(__name__)

SESSION_ID_PREFIX = "ps-"
SESSION_ID_ENV_VAR = "SessionId"


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


def get_nng_urls(session_id: str) -> dict[str, str]:
    """Generate NNG IPC URLs from SessionId.

    Args:
        session_id: SessionId string (e.g., "ps-a1b2c3d4-...")

    Returns:
        Dictionary with 'segmentation', 'keypoints', 'actions' URLs

    Examples:
        >>> urls = get_nng_urls("ps-a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        >>> urls["segmentation"]
        'ipc:///tmp/rw-a1b2c3d4-e5f6-7890-abcd-ef1234567890-seg.sock'
    """
    guid = parse_session_id(session_id)
    return {
        "segmentation": f"ipc:///tmp/rw-{guid}-seg.sock",
        "keypoints": f"ipc:///tmp/rw-{guid}-kp.sock",
        "actions": f"ipc:///tmp/rw-{guid}-actions.sock",
    }


def get_segmentation_url(session_id: str) -> str:
    """Get NNG URL for segmentation stream.

    Args:
        session_id: SessionId string (e.g., "ps-a1b2c3d4-...")

    Returns:
        IPC URL for segmentation stream
    """
    guid = parse_session_id(session_id)
    return f"ipc:///tmp/rw-{guid}-seg.sock"


def get_keypoints_url(session_id: str) -> str:
    """Get NNG URL for keypoints stream.

    Args:
        session_id: SessionId string (e.g., "ps-a1b2c3d4-...")

    Returns:
        IPC URL for keypoints stream
    """
    guid = parse_session_id(session_id)
    return f"ipc:///tmp/rw-{guid}-kp.sock"


def get_actions_url(session_id: str) -> str:
    """Get NNG URL for actions stream.

    Args:
        session_id: SessionId string (e.g., "ps-a1b2c3d4-...")

    Returns:
        IPC URL for actions stream
    """
    guid = parse_session_id(session_id)
    return f"ipc:///tmp/rw-{guid}-actions.sock"
