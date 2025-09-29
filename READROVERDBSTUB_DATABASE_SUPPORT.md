# ReadRoverDBStub Database Support

This document describes the database support added to ReadRoverDBStub, making it consistent with RoverSimulator's database capabilities.

## Overview

ReadRoverDBStub now supports both PostgreSQL and GeoPackage databases, following the same pattern as RoverSimulator. This ensures consistent configuration and seamless data flow between the simulator and reader applications.

## Supported Databases

### 1. PostgreSQL (Default)
- **Purpose**: Read from centralized PostgreSQL database
- **Use Case**: When RoverSimulator is writing to PostgreSQL
- **Connection**: Uses same connection settings as RoverSimulator
- **Schema**: Reads from `roverdata.rover_measurements` table

### 2. GeoPackage (Local Files)
- **Purpose**: Read from local .gpkg files
- **Use Case**: When RoverSimulator is writing to GeoPackage files
- **Location**: `C:\temp\Rover1\rover_data.gpkg`
- **Compatibility**: Works with RoverSimulator GeoPackage output

## Configuration

### Database Type Selection

Edit `ReadRoverDBStub/ReaderConfiguration.cs`:

```csharp
// For PostgreSQL (default - matches RoverSimulator)
public const string DEFAULT_DATABASE_TYPE = "postgres";

// For GeoPackage (local files)
public const string DEFAULT_DATABASE_TYPE = "geopackage";
```

### Connection Settings

```csharp
// PostgreSQL configuration (should match RoverSimulator)
public const string POSTGRES_CONNECTION_STRING = 
    "Host=192.168.1.97;Port=5432;Username=anders;Password=tua123;Database=postgres;Timeout=10;Command Timeout=30";

// GeoPackage configuration
public const string GEOPACKAGE_FOLDER_PATH = @"C:\temp\Rover1\";
```

## New Classes Added

### PostgresRoverDataReader
- **Purpose**: Reads rover measurement data from PostgreSQL
- **Features**:
  - Connection validation with timeout/retry
  - Proper PostGIS geometry handling
  - Error handling with detailed diagnostics
  - Read operations: count, all measurements, new measurements, latest measurement

### Enhanced ReaderConfiguration
- **Purpose**: Database configuration and connection validation
- **Features**:
  - Connection testing with timeout handling
  - Retry logic (3 attempts, 10-second timeout each)
  - Clear error messages and troubleshooting guidance
  - No automatic fallback (user controls database choice)

## Usage Examples

### Connection Validation Method (Recommended)

```csharp
// Test connection and create appropriate reader
var reader = await ReaderConfiguration.CreateReaderWithValidationAsync("postgres", cancellationToken);

// Use the reader
await reader.InitializeAsync();
var count = await reader.GetMeasurementCountAsync();
var latest = await reader.GetLatestMeasurementAsync();
```

### Direct Creation Method (Legacy)

```csharp
// Direct creation without validation
var reader = ReaderConfiguration.CreateReader("postgres"); // or "geopackage"
```

## Connection Validation Features

### PostgreSQL Connection Testing
- **Timeout**: 10 seconds per attempt
- **Retries**: 3 attempts with 2-second delays
- **Validation**: Tests both connection and data access
- **Error Detection**: Identifies specific issues (auth, missing table, etc.)

### Expected Behavior

#### When PostgreSQL is Available
```
============================================================
DATABASE CONNECTION SETUP (READER)
============================================================
Database type: POSTGRES
Testing PostgreSQL connection for reading...
Target: 192.168.1.97:5432, Database: postgres, User: anders
Timeout: 10 seconds
  Attempt 1/3: Connecting...
  SUCCESS: PostgreSQL connection established and data accessible!
? PostgreSQL connection successful - using PostgreSQL database for reading
============================================================
```

#### When PostgreSQL is Unreachable
```
============================================================
DATABASE CONNECTION SETUP (READER)
============================================================
Database type: POSTGRES
Testing PostgreSQL connection for reading...
Target: 192.168.1.97:5432, Database: postgres, User: anders
Timeout: 10 seconds
  Attempt 1/3: Connecting...
  TIMEOUT: Connection attempt 1 timed out after 10 seconds
  Waiting 2000ms before retry...
  [... retry attempts ...]
? PostgreSQL connection failed: Connection failed: All 3 attempts timed out

NETWORK TROUBLESHOOTING:
- Check if PostgreSQL server is running on 192.168.1.97:5432
- Verify network connectivity to the database server
- Make sure RoverSimulator has created the database schema

DATABASE CONFIGURATION OPTIONS:
1. Fix the PostgreSQL connection issue above
2. Change DEFAULT_DATABASE_TYPE to "geopackage" in ReaderConfiguration.cs
3. Run RoverSimulator first to create the database schema
============================================================

Database connection failed: PostgreSQL connection failed: Connection failed: All 3 attempts timed out after 10 seconds each. Please fix the connection issue or change to a different database type.

Reader cannot proceed without a valid database connection.
Please resolve the database connection issue and try again.
```

## Error Handling

### Common Scenarios

1. **PostgreSQL Server Unreachable**
   - Clear timeout messages
   - Network troubleshooting steps
   - Instructions to switch to GeoPackage

2. **Missing Database Schema**
   - Detects missing tables
   - Suggests running RoverSimulator first
   - Provides clear error messages

3. **Authentication Issues**
   - Identifies credential problems
   - Doesn't retry on auth failures
   - Suggests credential verification

4. **GeoPackage File Missing**
   - Detects missing .gpkg files
   - Suggests running RoverSimulator
   - Clear file path information

## Troubleshooting

### Quick Database Type Change

To switch from PostgreSQL to GeoPackage:

1. Open `ReadRoverDBStub/ReaderConfiguration.cs`
2. Change line: `DEFAULT_DATABASE_TYPE = "geopackage"`
3. Save and run

### Common Issues

#### "PostgreSQL connection timed out"
- **Cause**: Network issues or server unavailable
- **Solution**: 
  1. Check network connectivity
  2. Verify PostgreSQL server is running
  3. Or switch to GeoPackage mode

#### "Rover data table not found"
- **Cause**: RoverSimulator hasn't created the schema yet
- **Solution**: 
  1. Run RoverSimulator first
  2. Or switch to GeoPackage mode

#### "GeoPackage file not found"
- **Cause**: RoverSimulator hasn't created the .gpkg file
- **Solution**: 
  1. Run RoverSimulator with GeoPackage mode
  2. Check the file path in configuration

## Integration with RoverSimulator

### Consistent Configuration
Both applications now use the same configuration pattern:

| Setting | RoverSimulator | ReadRoverDBStub |
|---------|----------------|-----------------|
| Default DB Type | `postgres` | `postgres` |
| PostgreSQL Connection | `Host=192.168.1.97...` | `Host=192.168.1.97...` |
| GeoPackage Path | `C:\temp\Rover1\` | `C:\temp\Rover1\` |
| Connection Timeout | 10 seconds | 10 seconds |
| Retry Attempts | 3 | 3 |

### Data Flow
```
RoverSimulator ? Database ? ReadRoverDBStub
     ?              ?           ?
   Writes      PostgreSQL    Reads
   Data         or           Data
              GeoPackage
```

### Recommended Setup
1. Choose same database type in both applications
2. Use same connection settings
3. Run RoverSimulator first to create schema/data
4. Run ReadRoverDBStub to monitor the data

## Testing

Run the test script to see the new functionality:

```powershell
.\test_readroverdbstub_database_support.ps1
```

This will demonstrate:
- Connection validation process
- Error handling for unreachable PostgreSQL
- Clear user guidance and troubleshooting

## Benefits

1. **Consistency**: Same database support as RoverSimulator
2. **Flexibility**: Works with both PostgreSQL and GeoPackage
3. **Reliability**: Robust connection handling and error recovery
4. **User-Friendly**: Clear error messages and guidance
5. **No Surprises**: User controls database choice, no automatic decisions

The ReadRoverDBStub now provides enterprise-grade database connectivity while maintaining the simplicity needed for development and testing scenarios.