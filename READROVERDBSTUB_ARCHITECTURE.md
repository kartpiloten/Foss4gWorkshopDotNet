# ReadRoverDBStub Architecture Split

This document describes the architectural split of ReadRoverDBStub into a reusable library and a separate test client.

## Overview

The original ReadRoverDBStub project has been split into two projects to improve reusability and separation of concerns:

1. **ReadRoverDBStubLibrary** - Silent, reusable library
2. **ReadRoverDBStubTester** - Console test client

## Project Structure

### ReadRoverDBStubLibrary (Silent Library)

**Purpose**: Provides rover data reading functionality that can be used by other applications without console output.

**Key Features**:
- ? **Silent Operation**: No console output, perfect for library usage
- ? **PostgreSQL Support**: Full PostgreSQL connectivity with PostGIS
- ? **GeoPackage Support**: Local file-based data access
- ? **Connection Validation**: Timeout and retry logic
- ? **Event-Driven**: Uses events for notifications instead of console output
- ? **Exception-Based**: Uses exceptions for error handling

**Classes**:
- `IRoverDataReader` - Interface for data readers
- `PostgresRoverDataReader` - PostgreSQL implementation
- `GeoPackageRoverDataReader` - GeoPackage implementation  
- `NullRoverDataReader` - Null object pattern implementation
- `RoverDataMonitor` - Event-driven monitoring with `DataUpdated` and `StatusUpdate` events
- `ReaderConfiguration` - Silent configuration and validation
- `RoverMeasurement` - Data record structure

### ReadRoverDBStubTester (Test Client)

**Purpose**: Console application that demonstrates and tests the library functionality.

**Key Features**:
- ? **Verbose Output**: Detailed console logging for debugging
- ? **Connection Diagnostics**: PostgreSQL connection testing and troubleshooting
- ? **Real-time Monitoring**: Live display of rover data updates
- ? **Event Handling**: Subscribe to library events and display results
- ? **Error Diagnostics**: Detailed error messages and resolution steps

## Usage Patterns

### Library Usage (Silent)

```csharp
using ReadRoverDBStubLibrary;

// Create reader (silent operation)
var reader = new GeoPackageRoverDataReader(@"C:\temp\Rover1\");
await reader.InitializeAsync();

// Get data (no console output)
var measurements = await reader.GetAllMeasurementsAsync();
var latest = await reader.GetLatestMeasurementAsync();

// Use monitor with events
var monitor = new RoverDataMonitor(reader);
monitor.DataUpdated += (sender, e) => {
    // Handle new data silently
    ProcessNewData(e.NewMeasurementsCount, e.LatestMeasurement);
};
await monitor.StartAsync();
```

### Test Client Usage (Verbose)

```csharp
// Test client handles all console output and diagnostics
var reader = await ReaderConfiguration.CreateReaderWithValidationAsync("postgres");
var monitor = new RoverDataMonitor(reader);

// Subscribe to events for console display
monitor.DataUpdated += (sender, e) => {
    Console.WriteLine($"[UPDATE] Added {e.NewMeasurementsCount} new measurements");
};

monitor.StatusUpdate += (sender, e) => {
    Console.WriteLine($"[STATUS] Total: {e.TotalMeasurementsCount}");
};
```

## Project Dependencies

### Updated Dependencies

| Project | Old Dependency | New Dependency |
|---------|---------------|----------------|
| ConvertWinddataToPolygon | ReadRoverDBStub | ReadRoverDBStubLibrary |
| FrontendVersion2 | (none) | ReadRoverDBStubLibrary |
| ReadRoverDBStubTester | (none) | ReadRoverDBStubLibrary |

### Dependency Graph

```
ReadRoverDBStubLibrary (Library)
    ?
    ??? ReadRoverDBStubTester (Test Client)
    ??? ConvertWinddataToPolygon (Uses silently)
    ??? FrontendVersion2 (Uses silently)
```

## Configuration

### Library Configuration (Silent)

```csharp
// In ReadRoverDBStubLibrary/ReaderConfiguration.cs
public const string DEFAULT_DATABASE_TYPE = "postgres";
public const string POSTGRES_CONNECTION_STRING = "Host=192.168.1.97;Port=5432;...";
public const string GEOPACKAGE_FOLDER_PATH = @"C:\temp\Rover1\";
```

### Connection Validation

The library provides silent connection validation:

```csharp
var (isConnected, errorMessage) = await ReaderConfiguration.TestPostgresConnectionAsync();
if (isConnected)
{
    // Use PostgreSQL reader
    var reader = new PostgresRoverDataReader(connectionString);
}
else
{
    // Handle error or fallback (application decides)
    throw new InvalidOperationException($"Connection failed: {errorMessage}");
}
```

## Event-Driven Architecture

### RoverDataMonitor Events

```csharp
public class RoverDataUpdateEventArgs : EventArgs
{
    public int NewMeasurementsCount { get; init; }
    public int TotalMeasurementsCount { get; init; }
    public RoverMeasurement? LatestMeasurement { get; init; }
}

public class RoverDataStatusEventArgs : EventArgs
{
    public int TotalMeasurementsCount { get; init; }
    public RoverMeasurement? LatestMeasurement { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

### Event Usage

```csharp
var monitor = new RoverDataMonitor(reader);

// Handle new data
monitor.DataUpdated += (sender, e) => {
    // Process e.NewMeasurementsCount new measurements
    // Access e.LatestMeasurement for most recent data
};

// Handle status updates (periodic)
monitor.StatusUpdate += (sender, e) => {
    // Display or log current status
    // e.TotalMeasurementsCount, e.LatestMeasurement, e.Timestamp
};
```

## Error Handling

### Library Error Handling (Silent)
- Uses exceptions for error conditions
- No console output
- Clear exception messages with context

### Test Client Error Handling (Verbose)
- Catches library exceptions
- Provides detailed console diagnostics
- Offers troubleshooting guidance
- Shows connection retry attempts

## Building and Testing

### Build Order
1. `ReadRoverDBStubLibrary` (library)
2. `ReadRoverDBStubTester` (test client)
3. `ConvertWinddataToPolygon` (uses library)
4. `FrontendVersion2` (uses library)

### Test Script
Run `test_readroverdbstub_architecture.ps1` to verify the new architecture:

```powershell
.\test_readroverdbstub_architecture.ps1
```

## Migration Guide

### For ConvertWinddataToPolygon
- ? **Already Updated**: Project reference changed to ReadRoverDBStubLibrary
- ? **Namespace Updated**: `using ReadRoverDBStubLibrary;`
- ? **Silent Operation**: No console output from library calls

### For FrontendVersion2  
- ? **Already Updated**: Project reference added to ReadRoverDBStubLibrary
- ? **Namespace Updated**: `using ReadRoverDBStubLibrary;`
- ? **Silent Operation**: Perfect for web application usage

### For New Projects
1. Add project reference to ReadRoverDBStubLibrary
2. Use `using ReadRoverDBStubLibrary;`
3. Library will operate silently
4. Handle errors through exceptions
5. Use events for notifications

## Benefits

### Library Benefits
- **Reusable**: Can be used by multiple applications
- **Silent**: No unwanted console output
- **Event-Driven**: Clean notification system
- **Testable**: Clear interfaces and dependency injection ready
- **Maintainable**: Single source of truth for rover data access

### Architecture Benefits
- **Separation of Concerns**: Library logic separate from UI/console logic
- **Flexibility**: Applications choose how to handle output and errors
- **Consistency**: All applications use the same core functionality
- **Debuggability**: Test client provides detailed diagnostics when needed

## Files Structure

### ReadRoverDBStubLibrary/
```
ReadRoverDBStubLibrary.csproj
IRoverDataReader.cs
RoverDataReaderBase.cs
PostgresRoverDataReader.cs
GeoPackageRoverDataReader.cs
NullRoverDataReader.cs
RoverDataMonitor.cs
ReaderConfiguration.cs
```

### ReadRoverDBStubTester/
```
ReadRoverDBStubTester.csproj
Program.cs
```

This architecture provides a clean, reusable library that can be used silently by applications, while maintaining a separate test client for debugging and diagnostics.