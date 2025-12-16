"""Tests for session_id module."""

import os
import uuid
from unittest import mock

import pytest

from rocket_welder_sdk.session_id import (
    SESSION_ID_PREFIX,
    get_actions_url,
    get_keypoints_url,
    get_nng_urls,
    get_segmentation_url,
    get_session_id_from_env,
    parse_session_id,
)


class TestParseSessionId:
    """Tests for parse_session_id function."""

    def test_parse_with_prefix(self) -> None:
        """parse_session_id handles ps-{guid} format."""
        guid = uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        session_id = f"ps-{guid}"

        result = parse_session_id(session_id)

        assert result == guid

    def test_parse_without_prefix(self) -> None:
        """parse_session_id handles raw guid for backwards compat."""
        guid = uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        session_id = str(guid)

        result = parse_session_id(session_id)

        assert result == guid

    def test_parse_invalid_raises_value_error(self) -> None:
        """parse_session_id raises ValueError for invalid input."""
        with pytest.raises(ValueError):
            parse_session_id("invalid-session-id")

    def test_parse_empty_raises_value_error(self) -> None:
        """parse_session_id raises ValueError for empty string."""
        with pytest.raises(ValueError):
            parse_session_id("")


class TestGetNngUrls:
    """Tests for get_nng_urls function."""

    def test_generates_correct_urls(self) -> None:
        """get_nng_urls generates correct IPC URLs."""
        guid = uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        session_id = f"ps-{guid}"

        urls = get_nng_urls(session_id)

        assert urls["segmentation"] == f"ipc:///tmp/rw-{guid}-seg.sock"
        assert urls["keypoints"] == f"ipc:///tmp/rw-{guid}-kp.sock"
        assert urls["actions"] == f"ipc:///tmp/rw-{guid}-actions.sock"

    def test_works_with_raw_guid(self) -> None:
        """get_nng_urls works with raw guid for backwards compat."""
        guid = uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        session_id = str(guid)

        urls = get_nng_urls(session_id)

        assert f"{guid}" in urls["segmentation"]


class TestGetIndividualUrls:
    """Tests for individual URL getter functions."""

    def test_get_segmentation_url(self) -> None:
        """get_segmentation_url returns correct URL."""
        guid = uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        session_id = f"ps-{guid}"

        url = get_segmentation_url(session_id)

        assert url == f"ipc:///tmp/rw-{guid}-seg.sock"

    def test_get_keypoints_url(self) -> None:
        """get_keypoints_url returns correct URL."""
        guid = uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        session_id = f"ps-{guid}"

        url = get_keypoints_url(session_id)

        assert url == f"ipc:///tmp/rw-{guid}-kp.sock"

    def test_get_actions_url(self) -> None:
        """get_actions_url returns correct URL."""
        guid = uuid.UUID("a1b2c3d4-e5f6-7890-abcd-ef1234567890")
        session_id = f"ps-{guid}"

        url = get_actions_url(session_id)

        assert url == f"ipc:///tmp/rw-{guid}-actions.sock"


class TestGetSessionIdFromEnv:
    """Tests for get_session_id_from_env function."""

    def test_returns_value_when_set(self) -> None:
        """get_session_id_from_env returns value when SessionId is set."""
        with mock.patch.dict(os.environ, {"SessionId": "ps-test-guid"}):
            result = get_session_id_from_env()
            assert result == "ps-test-guid"

    def test_returns_none_when_not_set(self) -> None:
        """get_session_id_from_env returns None when SessionId not set."""
        with mock.patch.dict(os.environ, clear=True):
            # Ensure SessionId is not set
            os.environ.pop("SessionId", None)
            result = get_session_id_from_env()
            assert result is None


class TestSessionIdPrefix:
    """Tests for SESSION_ID_PREFIX constant."""

    def test_prefix_is_ps_dash(self) -> None:
        """SESSION_ID_PREFIX is 'ps-'."""
        assert SESSION_ID_PREFIX == "ps-"
