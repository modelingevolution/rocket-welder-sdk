"""
Enterprise-grade unit tests for ConnectionString class.
"""

import pytest

from rocket_welder_sdk import BytesSize, ConnectionMode, ConnectionString, Protocol


class TestConnectionString:
    """Test suite for ConnectionString class."""

    def test_parse_shm_basic(self) -> None:
        """Test parsing basic SHM connection string."""
        conn = ConnectionString.parse("shm://test_buffer")

        assert conn.protocol == Protocol.SHM
        assert conn.buffer_name == "test_buffer"
        assert conn.buffer_size == BytesSize.parse("256MB")
        assert conn.metadata_size == BytesSize.parse("4KB")
        assert conn.connection_mode == ConnectionMode.ONE_WAY
        assert conn.host is None
        assert conn.port is None

    def test_parse_shm_with_parameters(self) -> None:
        """Test parsing SHM connection string with parameters."""
        conn = ConnectionString.parse("shm://my_buffer?size=512MB&metadata=8KB&mode=Duplex")

        assert conn.protocol == Protocol.SHM
        assert conn.buffer_name == "my_buffer"
        assert conn.buffer_size == BytesSize.parse("512MB")
        assert conn.metadata_size == BytesSize.parse("8KB")
        assert conn.connection_mode == ConnectionMode.DUPLEX

    def test_parse_shm_with_timeout(self) -> None:
        """Test parsing SHM connection string with timeout."""
        conn = ConnectionString.parse(
            "shm://buffer?size=256MB&metadata=4KB&mode=OneWay&timeout=10000"
        )

        assert conn.protocol == Protocol.SHM
        assert conn.buffer_name == "buffer"
        assert conn.timeout_ms == 10000

    def test_parse_shm_case_insensitive(self) -> None:
        """Test case-insensitive parameter parsing."""
        conn = ConnectionString.parse("shm://buffer?SIZE=128MB&METADATA=2KB&MODE=duplex")

        assert conn.buffer_size == BytesSize.parse("128MB")
        assert conn.metadata_size == BytesSize.parse("2KB")
        assert conn.connection_mode == ConnectionMode.DUPLEX

    def test_parse_mjpeg_with_host_port(self) -> None:
        """Test parsing MJPEG connection string."""
        conn = ConnectionString.parse("mjpeg://192.168.1.100:8080")

        assert conn.protocol == Protocol.MJPEG
        assert conn.host == "192.168.1.100"
        assert conn.port == 8080
        assert conn.buffer_name is None

    def test_parse_mjpeg_http(self) -> None:
        """Test parsing MJPEG+HTTP connection string."""
        conn = ConnectionString.parse("mjpeg+http://camera.local:80")

        assert Protocol.MJPEG in conn.protocol
        assert Protocol.HTTP in conn.protocol
        assert conn.host == "camera.local"
        assert conn.port == 80

    def test_parse_mjpeg_default_ports(self) -> None:
        """Test default ports for MJPEG protocols."""
        # Plain MJPEG defaults to 8080
        conn = ConnectionString.parse("mjpeg://localhost")
        assert conn.port == 8080

        # MJPEG+HTTP defaults to 80
        conn = ConnectionString.parse("mjpeg+http://localhost")
        assert conn.port == 80

    def test_parse_invalid_format(self) -> None:
        """Test parsing invalid connection strings."""
        with pytest.raises(ValueError):
            ConnectionString.parse("")

        with pytest.raises(ValueError):
            ConnectionString.parse("invalid")

        with pytest.raises(ValueError):
            ConnectionString.parse("unknown://host")

        with pytest.raises(ValueError):
            ConnectionString.parse("shm:")

    def test_string_representation(self) -> None:
        """Test string representation of ConnectionString."""
        # SHM connection
        conn = ConnectionString.parse("shm://buffer?size=256MB&metadata=4KB&mode=OneWay")
        conn_str = str(conn)
        assert "shm://" in conn_str
        assert "buffer" in conn_str
        assert "256MB" in conn_str
        assert "4KB" in conn_str
        assert "OneWay" in conn_str

        # Parse round-trip
        conn2 = ConnectionString.parse(conn_str)
        assert conn2.protocol == conn.protocol
        assert conn2.buffer_name == conn.buffer_name
        assert conn2.buffer_size == conn.buffer_size
        assert conn2.metadata_size == conn.metadata_size
        assert conn2.connection_mode == conn.connection_mode

    def test_mjpeg_string_representation(self) -> None:
        """Test string representation of MJPEG connection."""
        conn = ConnectionString.parse("mjpeg://192.168.1.100:8080")
        conn_str = str(conn)
        assert "mjpeg://" in conn_str
        assert "192.168.1.100" in conn_str
        assert "8080" in conn_str

    def test_to_dict(self) -> None:
        """Test dictionary representation."""
        conn = ConnectionString.parse("shm://test?size=128MB&metadata=2KB&mode=Duplex")
        data = conn.to_dict()

        assert data["protocol"] == str(Protocol.SHM)
        assert data["buffer_name"] == "test"
        assert data["buffer_size"] == "128MB"
        assert data["metadata_size"] == "2KB"
        assert data["connection_mode"] == "Duplex"
        assert data["timeout_ms"] == 5000

    def test_immutability(self) -> None:
        """Test that ConnectionString is immutable."""
        conn = ConnectionString.parse("shm://buffer")

        # Should not be able to modify attributes
        with pytest.raises(AttributeError):
            conn.buffer_name = "new_buffer"  # type: ignore

        with pytest.raises(AttributeError):
            conn.buffer_size = BytesSize.parse("512MB")  # type: ignore

    def test_special_characters_in_buffer_name(self) -> None:
        """Test buffer names with special characters."""
        conn = ConnectionString.parse("shm://test_buffer-123")
        assert conn.buffer_name == "test_buffer-123"

        conn = ConnectionString.parse("shm://test.buffer.456")
        assert conn.buffer_name == "test.buffer.456"

    def test_ipv6_host(self) -> None:
        """Test parsing IPv6 addresses."""
        # Note: Full IPv6 support might need additional work
        conn = ConnectionString.parse("mjpeg://[::1]:8080")
        assert conn.host == "[::1]"
        assert conn.port == 8080

    def test_protocol_combinations(self) -> None:
        """Test parsing combined protocols."""
        conn = ConnectionString.parse("mjpeg+tcp://server:9000")
        assert Protocol.MJPEG in conn.protocol
        assert Protocol.TCP in conn.protocol

        # Test string representation maintains both
        conn_str = str(conn)
        assert "mjpeg" in conn_str.lower()
        assert "tcp" in conn_str.lower()
