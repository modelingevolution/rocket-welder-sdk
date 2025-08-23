"""Test file to verify mypy catches zerobuffer type errors."""
from zerobuffer import Writer

# This should be caught by mypy as an error
writer = Writer("test", 1024, 128)
writer.commit_frame(123)  # ERROR: commit_frame() takes no arguments!