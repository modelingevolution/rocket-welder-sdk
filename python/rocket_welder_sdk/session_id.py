"""SessionId parsing utilities for NNG URL generation.

SessionId format: ps-{guid} (e.g., ps-a1b2c3d4-e5f6-7890-abcd-ef1234567890)
Prefix "ps" = PipelineSession.

This module provides utilities to:
1. Parse SessionId from environment variable
2. Extract the Guid portion
3. Generate NNG IPC URLs for streaming results
4. Read explicit NNG URLs from environment variables (preferred)

## URL Configuration Priority

The SDK supports two ways to configure NNG URLs:

1. **Explicit URLs (PREFERRED)** - Set by rocket-welder2:
   - SEGMENTATION_SINK_URL
   - KEYPOINTS_SINK_URL
   - ACTIONS_SINK_URL

2. **Derived from SessionId (FALLBACK)** - For backwards compatibility:
   - SessionId env var → parse GUID → generate URLs

Use `get_nng_urls_from_env()` for explicit URLs (preferred).
Use `get_nng_urls(session_id)` for SessionId-derived URLs (fallback).
"""

from __future__ import annotations

import logging
import os
import uuid

logger = logging.getLogger(__name__)

SESSION_ID_PREFIX = "ps-"
SESSION_ID_ENV_VAR = "SessionId"

# Explicit URL environment variables (set by rocket-welder2)
SEGMENTATION_SINK_URL_ENV = "SEGMENTATION_SINK_URL"
KEYPOINTS_SINK_URL_ENV = "KEYPOINTS_SINK_URL"
ACTIONS_SINK_URL_ENV = "ACTIONS_SINK_URL"


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


# ============================================================================
# Explicit URL functions (PREFERRED - URLs set by rocket-welder2)
# ============================================================================


def get_nng_urls_from_env() -> dict[str, str | None]:
    """Get NNG URLs from explicit environment variables.

    This is the PREFERRED method for getting NNG URLs. rocket-welder2
    sets these environment variables when starting containers.

    Returns:
        Dictionary with 'segmentation', 'keypoints', 'actions' URLs.
        Values are None if not configured.

    Examples:
        >>> os.environ["SEGMENTATION_SINK_URL"] = "ipc:///tmp/rw-abc-seg.sock"
        >>> urls = get_nng_urls_from_env()
        >>> urls["segmentation"]
        'ipc:///tmp/rw-abc-seg.sock'
    """
    return {
        "segmentation": os.environ.get(SEGMENTATION_SINK_URL_ENV),
        "keypoints": os.environ.get(KEYPOINTS_SINK_URL_ENV),
        "actions": os.environ.get(ACTIONS_SINK_URL_ENV),
    }


def get_segmentation_url_from_env() -> str | None:
    """Get segmentation NNG URL from environment variable.

    Returns:
        IPC URL for segmentation stream, or None if not configured.
    """
    return os.environ.get(SEGMENTATION_SINK_URL_ENV)


def get_keypoints_url_from_env() -> str | None:
    """Get keypoints NNG URL from environment variable.

    Returns:
        IPC URL for keypoints stream, or None if not configured.
    """
    return os.environ.get(KEYPOINTS_SINK_URL_ENV)


def get_actions_url_from_env() -> str | None:
    """Get actions NNG URL from environment variable.

    Returns:
        IPC URL for actions stream, or None if not configured.
    """
    return os.environ.get(ACTIONS_SINK_URL_ENV)


def has_explicit_nng_urls() -> bool:
    """Check if explicit NNG URLs are configured.

    Returns:
        True if at least segmentation OR keypoints URL is configured.
    """
    urls = get_nng_urls_from_env()
    return bool(urls["segmentation"] or urls["keypoints"])


def get_configured_nng_urls() -> dict[str, str]:
    """Get all configured NNG URLs (explicit or derived from SessionId).

    Priority:
    1. Explicit URLs from environment (SEGMENTATION_SINK_URL, etc.)
    2. Derived from SessionId environment variable (fallback)

    Returns:
        Dictionary with 'segmentation', 'keypoints', 'actions' URLs.
        Only includes URLs that are actually configured.

    Raises:
        ValueError: If no NNG URLs are configured (neither explicit nor SessionId).
    """
    # Try explicit URLs first (preferred)
    explicit_urls = get_nng_urls_from_env()
    result: dict[str, str] = {}

    for name, url in explicit_urls.items():
        if url:
            result[name] = url

    # If we have at least one explicit URL, return what we have
    if result:
        return result

    # Fallback: derive from SessionId
    session_id = get_session_id_from_env()
    if session_id:
        return get_nng_urls(session_id)

    raise ValueError(
        "No NNG URLs configured. Set SEGMENTATION_SINK_URL/KEYPOINTS_SINK_URL "
        "environment variables, or set SessionId for URL derivation."
    )
