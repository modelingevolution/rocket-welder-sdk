#!/bin/bash
# Code quality verification script for RocketWelder SDK Python
# Enforces enterprise-grade code quality standards

# Don't exit on error immediately - we want to run all checks
set +e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Track results
MYPY_PASS=0
BLACK_PASS=0
RUFF_PASS=0
TEST_PASS=0
OVERALL_PASS=1

echo "========================================="
echo "Running Code Quality Checks"
echo "========================================="

# Check if virtual environment exists, create if not
if [ ! -d "venv" ]; then
    echo -e "${YELLOW}Creating virtual environment...${NC}"
    python3 -m venv venv
fi

# Install dependencies if needed
echo -e "${YELLOW}Installing dependencies...${NC}"
venv/bin/pip install --quiet --upgrade pip 2>/dev/null
venv/bin/pip install --quiet mypy black ruff pytest pytest-cov numpy opencv-python 2>/dev/null || {
    echo -e "${YELLOW}Installing packages (this may take a moment)...${NC}"
    venv/bin/pip install mypy black ruff pytest pytest-cov numpy opencv-python
}

# Run mypy for type checking
echo ""
echo -e "${YELLOW}Running mypy type checking...${NC}"
if venv/bin/python -m mypy rocket_welder_sdk --strict --no-error-summary; then
    echo -e "${GREEN}✓ Type checking passed${NC}"
    MYPY_PASS=1
else
    echo -e "${RED}✗ Type checking failed${NC}"
    OVERALL_PASS=0
fi

# Run black for code formatting check
echo ""
echo -e "${YELLOW}Checking code formatting with black...${NC}"
if venv/bin/python -m black --check rocket_welder_sdk tests 2>/dev/null; then
    echo -e "${GREEN}✓ Code formatting is correct${NC}"
    BLACK_PASS=1
else
    echo -e "${RED}✗ Code formatting issues found${NC}"
    echo "Run 'venv/bin/python -m black rocket_welder_sdk tests' to auto-format"
    OVERALL_PASS=0
fi

# Run ruff for linting
echo ""
echo -e "${YELLOW}Running ruff linter...${NC}"
if venv/bin/python -m ruff check rocket_welder_sdk tests; then
    echo -e "${GREEN}✓ Linting passed${NC}"
    RUFF_PASS=1
else
    echo -e "${RED}✗ Linting issues found${NC}"
    OVERALL_PASS=0
fi

# Run tests with coverage if they exist
if [ -d "tests" ] && [ "$(ls -A tests/*.py 2>/dev/null)" ]; then
    echo ""
    echo -e "${YELLOW}Running tests with coverage...${NC}"
    if venv/bin/python -m pytest tests --cov=rocket_welder_sdk --cov-report=term-missing --cov-fail-under=55; then
        echo -e "${GREEN}✓ Tests passed with sufficient coverage${NC}"
        TEST_PASS=1
    else
        echo -e "${RED}✗ Tests failed or insufficient coverage${NC}"
        OVERALL_PASS=0
    fi
fi

# Summary
echo ""
echo "========================================="
echo -e "${GREEN}Code Quality Summary${NC}"
echo "========================================="

if [ $MYPY_PASS -eq 1 ]; then
    echo -e "${GREEN}✓${NC} Type checking: Passed (mypy strict mode)"
else
    echo -e "${RED}✗${NC} Type checking: Failed"
fi

if [ $BLACK_PASS -eq 1 ]; then
    echo -e "${GREEN}✓${NC} Code formatting: Passed (black)"
else
    echo -e "${RED}✗${NC} Code formatting: Failed"
fi

if [ $RUFF_PASS -eq 1 ]; then
    echo -e "${GREEN}✓${NC} Linting: Passed (ruff)"
else
    echo -e "${RED}✗${NC} Linting: Failed"
fi

if [ -d "tests" ] && [ "$(ls -A tests/*.py 2>/dev/null)" ]; then
    if [ $TEST_PASS -eq 1 ]; then
        echo -e "${GREEN}✓${NC} Tests: Passed with coverage ≥55%"
    else
        echo -e "${RED}✗${NC} Tests: Failed or insufficient coverage"
    fi
fi

echo ""
echo "========================================="

if [ $OVERALL_PASS -eq 1 ]; then
    echo -e "${GREEN}All code quality checks passed!${NC}"
    echo "========================================="
    exit 0
else
    echo -e "${RED}Some checks failed. Please fix the issues above.${NC}"
    echo "========================================="
    exit 1
fi