"""
PeriodicTimer implementation for Python, similar to .NET's System.Threading.PeriodicTimer.

Provides an async periodic timer that enables waiting asynchronously for timer ticks.
This is particularly useful for rendering and periodic frame updates.
"""

from __future__ import annotations

import asyncio
import contextlib
import time
from datetime import timedelta
from typing import TYPE_CHECKING, Any, List, Optional, Union

if TYPE_CHECKING:
    from types import TracebackType
else:
    TracebackType = type


class PeriodicTimer:
    """
    A periodic timer that enables waiting asynchronously for timer ticks.

    Similar to .NET's PeriodicTimer, this class provides:
    - Async-first design with wait_for_next_tick_async()
    - Single consumer model (only one wait call at a time)
    - Proper cancellation support
    - No callback-based design (work is done in the calling scope)

    Example:
        async def render_loop():
            timer = PeriodicTimer(timedelta(seconds=1/60))  # 60 FPS
            try:
                while await timer.wait_for_next_tick_async():
                    # Render frame
                    await render_frame()
            finally:
                timer.dispose()
    """

    def __init__(self, period: Union[timedelta, float]):
        """
        Initialize a new PeriodicTimer.

        Args:
            period: Time interval between ticks. Can be timedelta or float (seconds).

        Raises:
            ValueError: If period is negative or zero.
        """
        if isinstance(period, timedelta):
            self._period_seconds = period.total_seconds()
        else:
            self._period_seconds = float(period)

        if self._period_seconds <= 0:
            raise ValueError("Period must be positive")

        self._start_time = time.monotonic()
        self._tick_count = 0
        self._is_disposed = False
        self._waiting = False
        self._dispose_event = asyncio.Event()

    @property
    def period(self) -> timedelta:
        """Get the current period as a timedelta."""
        return timedelta(seconds=self._period_seconds)

    @period.setter
    def period(self, value: Union[timedelta, float]) -> None:
        """
        Set a new period (supported in .NET 8+).

        Args:
            value: New time interval between ticks.
        """
        new_period = value.total_seconds() if isinstance(value, timedelta) else float(value)

        if new_period <= 0:
            raise ValueError("Period must be positive")

        self._period_seconds = new_period

    async def wait_for_next_tick_async(
        self, cancellation_token: Optional[asyncio.Event] = None
    ) -> bool:
        """
        Wait asynchronously for the next timer tick.

        Args:
            cancellation_token: Optional cancellation token to stop waiting.

        Returns:
            True if the timer ticked successfully, False if canceled or disposed.

        Raises:
            RuntimeError: If multiple consumers try to wait simultaneously.

        Note:
            - Only one call to this method may be in flight at any time
            - If a tick occurred while no one was waiting, the next call
              will complete immediately
            - The timer starts when the instance is created, not when first called
        """
        if self._is_disposed:
            return False

        if self._waiting:
            raise RuntimeError("Only one consumer may wait on PeriodicTimer at a time")

        self._waiting = True
        try:
            # Calculate next tick time
            self._tick_count += 1
            next_tick_time = self._start_time + (self._tick_count * self._period_seconds)

            # Calculate wait time
            current_time = time.monotonic()
            wait_time = max(0, next_tick_time - current_time)

            # If we're behind schedule, complete immediately
            if wait_time == 0:
                return not self._is_disposed

            # Create tasks for waiting
            tasks: List[asyncio.Task[Any]] = [asyncio.create_task(asyncio.sleep(wait_time))]

            # Add dispose event task
            dispose_task = asyncio.create_task(self._dispose_event.wait())
            tasks.append(dispose_task)

            # Add cancellation token if provided
            if cancellation_token:
                cancel_task = asyncio.create_task(cancellation_token.wait())
                tasks.append(cancel_task)

            # Wait for first task to complete
            done, pending = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)

            # Cancel pending tasks
            for task in pending:
                task.cancel()
                with contextlib.suppress(asyncio.CancelledError):
                    await task

            # Check what completed
            if dispose_task in done or self._is_disposed:
                return False

            return not (cancellation_token and cancel_task in done)

        finally:
            self._waiting = False

    def dispose(self) -> None:
        """
        Dispose of the timer and release resources.

        This will cause any pending wait_for_next_tick_async() calls to return False.
        """
        if not self._is_disposed:
            self._is_disposed = True
            self._dispose_event.set()

    def __enter__(self) -> PeriodicTimer:
        """Context manager entry."""
        return self

    def __exit__(
        self,
        exc_type: Optional[type],
        exc_val: Optional[BaseException],
        exc_tb: Optional[TracebackType],
    ) -> None:
        """Context manager exit - ensures disposal."""
        self.dispose()

    def __del__(self) -> None:
        """Ensure timer is disposed when garbage collected."""
        self.dispose()


class PeriodicTimerSync:
    """
    Synchronous version of PeriodicTimer for non-async contexts.

    Provides similar functionality but with blocking wait methods.
    Useful when async is not available or desired.

    Example:
        timer = PeriodicTimerSync(1.0/60)  # 60 FPS
        try:
            while timer.wait_for_next_tick():
                render_frame()
        finally:
            timer.dispose()
    """

    def __init__(self, period: Union[timedelta, float]):
        """
        Initialize a new synchronous PeriodicTimer.

        Args:
            period: Time interval between ticks. Can be timedelta or float (seconds).
        """
        if isinstance(period, timedelta):
            self._period_seconds = period.total_seconds()
        else:
            self._period_seconds = float(period)

        if self._period_seconds <= 0:
            raise ValueError("Period must be positive")

        self._start_time = time.monotonic()
        self._tick_count = 0
        self._is_disposed = False
        self._waiting = False

    @property
    def period(self) -> timedelta:
        """Get the current period as a timedelta."""
        return timedelta(seconds=self._period_seconds)

    @period.setter
    def period(self, value: Union[timedelta, float]) -> None:
        """Set a new period."""
        new_period = value.total_seconds() if isinstance(value, timedelta) else float(value)

        if new_period <= 0:
            raise ValueError("Period must be positive")

        self._period_seconds = new_period

    def wait_for_next_tick(self, timeout: Optional[float] = None) -> bool:
        """
        Wait synchronously for the next timer tick.

        Args:
            timeout: Optional timeout in seconds. None means wait indefinitely.

        Returns:
            True if the timer ticked successfully, False if timed out or disposed.

        Raises:
            RuntimeError: If multiple consumers try to wait simultaneously.
        """
        if self._is_disposed:
            return False

        if self._waiting:
            raise RuntimeError("Only one consumer may wait on PeriodicTimer at a time")

        self._waiting = True
        try:
            # Calculate next tick time
            self._tick_count += 1
            next_tick_time = self._start_time + (self._tick_count * self._period_seconds)

            # Calculate wait time
            current_time = time.monotonic()
            wait_time = max(0, next_tick_time - current_time)

            # Apply timeout if specified
            if timeout is not None:
                wait_time = min(wait_time, timeout)

            # If we need to wait, sleep
            if wait_time > 0:
                time.sleep(wait_time)

            # Check if we're disposed or timed out
            if self._is_disposed:
                return False

            if timeout is not None:
                actual_time = time.monotonic()
                if actual_time < next_tick_time:
                    return False  # Timed out

            return True

        finally:
            self._waiting = False

    def dispose(self) -> None:
        """Dispose of the timer."""
        self._is_disposed = True

    def __enter__(self) -> PeriodicTimerSync:
        """Context manager entry."""
        return self

    def __exit__(
        self,
        exc_type: Optional[type],
        exc_val: Optional[BaseException],
        exc_tb: Optional[TracebackType],
    ) -> None:
        """Context manager exit."""
        self.dispose()
