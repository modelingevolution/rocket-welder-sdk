# ZeroBuffer Exception Handling Specification

## Exception Types (Available in ZeroBuffer v1.1.4)

### Base Exception
- **ZeroBufferException** - Base class for all ZeroBuffer exceptions

### Communication Exceptions
- **ReaderDeadException** - Writer has disconnected
- **WriterDeadException** - Reader has disconnected

### Buffer Exceptions  
- **BufferFullException** - No space for new frame
- **FrameTooLargeException** - Frame exceeds buffer capacity
- **BufferNotFoundException** - Shared memory buffer not found
- **WriterAlreadyConnectedException** - Multiple writers attempting to connect
- **ReaderAlreadyConnectedException** - Multiple readers attempting to connect

## Handling Requirements

1. Log exception at appropriate level
2. Stop processing thread gracefully
3. Clean up resources
4. Raise proper events in the RocketWelderClient

## Current Implementation Analysis

### Issues Found

1. **Inconsistent Logging Levels**
   - ReaderDeadException logged as Information (should be consistent)
   - Generic exceptions logged as Error

2. **Missing Exception Types in Current Implementation**
   - WriterDeadException not handled (should log as Info)
   - BufferFullException not handled (should log as Error)
   - FrameTooLargeException not handled (should log as Error)
   - BufferNotFoundException not handled (should log as Error)
   - WriterAlreadyConnectedException not handled (should log as Error)
   - ReaderAlreadyConnectedException not handled (should log as Error)
   - ZeroBufferException base type not handled specifically

3. **Incomplete Thread Termination**
   - OnFirstFrame throws on ReaderDeadException but continues on generic exceptions
   - ProcessFrames sets _isRunning=false only in OnFirstFrame exception path
   - No consistent pattern for thread cleanup

4. **Resource Cleanup**
   - DuplexShmController has NO exception handling at all
     - The IImmutableDuplexServer.Start() runs ProcessRequests internally (lines 131-192)
     - Exceptions in ProcessFrame callback are caught and logged (lines 168-172) but processing continues
     - ReaderDeadException/WriterDeadException are caught internally (lines 174-184) and cause server to stop
     - But DuplexShmController has NO visibility when server stops due to exceptions
     - Need to monitor _server.IsRunning or add event handlers
   - MjpegController only handles generic exceptions

### Recommendations

1. Create events on RocketWelderClient 
   - Started, Stopped
   - Failed with exception details
   
2. Set _isRunning=false in all terminal exception paths
3. Add specific handling for each exception type
4. Ensure thread Join() completes in Stop/Dispose
5. Log communication exceptions as Warning, protocol exceptions as Error (except process termination - XYZ-DeadExceptions);

### Plan

1. **ZeroBuffer SDK Changes Required**
   - ZeroBuffer v1.1.4 provides all necessary exception types
   - ImmutableDuplexServer.ProcessRequests currently swallows exceptions (lines 174-184)
   - **Solution**: Change IImmutableDuplexServer contract to expose exceptions
     - Add exception callback/event to notify caller when exceptions occur
     - Modify ProcessRequests to invoke callback instead of just logging
     - This allows DuplexShmController to handle exceptions properly
   
2. **Make sure ZeroBuffer SDK works correctly after changes**
   - Will verify with existing tests after implementation
   
3. **RocketWelderClient Implementation Gaps**
   - Need to add Started, Stopped, Failed events
   - Need proper exception handling in all controllers
   - Need consistent thread lifecycle management
   
4. **Implementation Tasks**
   - Add comprehensive exception handling to OneWayShmController
   - Add exception handling to DuplexShmController  
   - Update MjpegController exception handling
   - Add events to RocketWelderClient
   - Verify with test.sh