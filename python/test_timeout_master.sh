#!/bin/bash

# Master script to test timeout behavior with increasing delays
# Uses Fibonacci-like sequence: 1s, 2s, 3s, 5s, 8s, 13s, 21s, 34s, 55s, 89s (up to ~2 minutes)
# Can optionally specify a timeout value (default 5000ms)

set -e

# Configuration
TIMEOUT_MS=5000  # Default timeout in milliseconds

# Parse command line arguments
for arg in "$@"; do
    case $arg in
        timeout=*)
            TIMEOUT_MS="${arg#*=}"
            shift
            ;;
    esac
done

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo "=============================================="
echo "Timeout Behavior Test - Master Script"
echo "=============================================="
echo "This will test the Python client timeout behavior"
echo "when GStreamer pipeline is delayed by various amounts."
TIMEOUT_SEC=$(echo "scale=1; $TIMEOUT_MS / 1000" | bc)
echo "Connection timeout: ${TIMEOUT_MS}ms (${TIMEOUT_SEC}s)"
echo "Expected: Timeout should occur when delay > ${TIMEOUT_SEC}s"
echo ""

# Delay values in milliseconds (Fibonacci-like sequence)
DELAYS=(1000 2000 3000 5000 8000 13000 21000 34000 55000 89000 120000)

# Results storage
declare -a RESULTS

echo "Test sequence (delays in milliseconds):"
echo "${DELAYS[@]}"
echo ""
echo "Starting tests..."
echo "=============================================="

for DELAY in "${DELAYS[@]}"; do
    DELAY_SEC=$(echo "scale=1; $DELAY / 1000" | bc)

    echo ""
    echo -e "${BLUE}=== Test with delay: ${DELAY}ms (${DELAY_SEC}s) ===${NC}"
    echo "----------------------------------------------"

    # Run the test script with delay and timeout parameters
    if ./test_integration_aravis_delay.sh delay=$DELAY timeout=$TIMEOUT_MS > test_timeout_${DELAY}ms_${TIMEOUT_MS}ms.log 2>&1; then
        RESULT="PASS"
        echo -e "${GREEN}✓ Test completed successfully${NC}"

        # Check if timeout occurred
        if grep -q "Client timed out" test_timeout_${DELAY}ms_${TIMEOUT_MS}ms.log; then
            echo -e "${YELLOW}  → Client timeout detected${NC}"
            RESULT="TIMEOUT"
        fi
    else
        RESULT="FAIL"
        echo -e "${RED}✗ Test failed${NC}"

        # Check for specific timeout
        if grep -q "Client timed out" test_timeout_${DELAY}ms_${TIMEOUT_MS}ms.log; then
            echo -e "${YELLOW}  → Client timeout detected${NC}"
            RESULT="TIMEOUT"
        fi
    fi

    # Store result
    RESULTS+=("${DELAY}ms:${RESULT}")

    # Show key information from log
    if grep -q "Frames processed:" test_timeout_${DELAY}ms_${TIMEOUT_MS}ms.log; then
        PROCESSED=$(grep "Frames processed:" test_timeout_${DELAY}ms_${TIMEOUT_MS}ms.log | tail -1)
        echo "  $PROCESSED"
    fi

    # Clean up shared memory between tests
    echo "  Cleaning up shared memory..."
    rm -f /dev/shm/test_aravis* 2>/dev/null || true
    rm -f /dev/shm/sem.test_aravis* 2>/dev/null || true
    rm -f /dev/shm/sem.sem-*test_aravis* 2>/dev/null || true

    # Brief pause between tests
    sleep 2
done

# Summary Report
echo ""
echo "=============================================="
echo "TIMEOUT TEST SUMMARY REPORT"
echo "=============================================="
echo ""
echo -e "${BLUE}Delay\t\tResult\t\tExpected${NC}"
echo "----------------------------------------------"

for i in "${!DELAYS[@]}"; do
    DELAY="${DELAYS[$i]}"
    DELAY_SEC=$(echo "scale=1; $DELAY / 1000" | bc)
    RESULT="${RESULTS[$i]}"
    RESULT_VALUE="${RESULT#*:}"

    # Determine expected result (timeout expected when delay > timeout)
    if [ $DELAY -le $TIMEOUT_MS ]; then
        EXPECTED="PASS"
    else
        EXPECTED="TIMEOUT"
    fi

    # Color code based on whether result matches expectation
    if [ "$RESULT_VALUE" = "$EXPECTED" ]; then
        COLOR=$GREEN
        STATUS="✓"
    else
        COLOR=$RED
        STATUS="✗"
    fi

    echo -e "${COLOR}${DELAY}ms (${DELAY_SEC}s)\t${RESULT_VALUE}\t\t${EXPECTED} ${STATUS}${NC}"
done

echo ""
echo "=============================================="
echo "Analysis:"
echo "----------------------------------------------"

# Count timeouts
TIMEOUT_COUNT=0
FIRST_TIMEOUT_DELAY=""
for RESULT in "${RESULTS[@]}"; do
    if [[ "$RESULT" == *"TIMEOUT"* ]]; then
        ((TIMEOUT_COUNT++))
        if [ -z "$FIRST_TIMEOUT_DELAY" ]; then
            FIRST_TIMEOUT_DELAY="${RESULT%%:*}"
        fi
    fi
done

echo "Total tests run: ${#DELAYS[@]}"
echo "Timeouts detected: $TIMEOUT_COUNT"
if [ -n "$FIRST_TIMEOUT_DELAY" ]; then
    FIRST_SEC=$(echo "scale=1; ${FIRST_TIMEOUT_DELAY%ms} / 1000" | bc)
    echo -e "${YELLOW}First timeout at: ${FIRST_TIMEOUT_DELAY} (${FIRST_SEC}s)${NC}"

    TIMEOUT_SEC=$(echo "scale=1; $TIMEOUT_MS / 1000" | bc)
    if [ "${FIRST_TIMEOUT_DELAY%ms}" -gt $TIMEOUT_MS ]; then
        echo -e "${GREEN}✓ Timeout behavior is correct (first timeout > ${TIMEOUT_SEC}s)${NC}"
    else
        echo -e "${RED}✗ Unexpected: timeout occurred before ${TIMEOUT_SEC}s delay${NC}"
    fi
else
    echo -e "${RED}No timeouts detected - this may indicate an issue${NC}"
fi

echo ""
echo "Log files available for detailed analysis:"
for DELAY in "${DELAYS[@]}"; do
    echo "  - test_timeout_${DELAY}ms_${TIMEOUT_MS}ms.log"
done

echo ""
echo "=============================================="
echo "Test complete!"
echo "=============================================="