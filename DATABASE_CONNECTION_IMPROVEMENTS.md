# Database Connection Improvements

This document describes the improvements made to handle database connection issues, particularly PostgreSQL timeout problems.

## Problem Solved

The original issue was:
```
Error during simulation: Failed to connect to 192.168.1.97:5432
Inner exception: The operation has timed out.
```

## New Features

### 1. Connection Validation and Retry Logic

- **Timeout Handling**: Connections now have a 10-second timeout with 3 retry attempts
- **Smart Retry**: Waits 2 seconds between retry attempts
- **Early Failure Detection**: Stops retrying for authentication or permission errors

### 2. Clear Failure Handling (No Automatic Fallback)

When PostgreSQL connection fails, the system:
- Provides detailed error information and troubleshooting steps
- **Stops the simulation** with a clear error message
- Gives the user explicit instructions on how to fix the issue
- **No automatic fallback** - the user maintains control over database choice

### 3. Enhanced Error Messages and Troubleshooting

The system now provides:
- Detailed connection attempt information
- Clear troubleshooting steps for common issues
- Network-specific error handling
- Explicit instructions for switching database types

## Configuration

### Connection Settings

```csharp
// Connection timeout and retry settings
public const int CONNECTION_TIMEOUT_SECONDS = 10;
public const int MAX_RETRY_ATTEMPTS = 3;
public const int RETRY_DELAY_MS = 2000;

// PostgreSQL connection string with timeout
public const string POSTGRES_CONNECTION_STRING = 
    "Host=192.168.1.97;Port=5432;Username=anders;Password=tua123;Database=postgres;Timeout=10;Command Timeout=30";
```

### How to Use

#### Method 1: Connection Validation (Recommended)
```csharp
// This method tests the specified database type and fails clearly if connection issues occur
var repository = await SimulatorConfiguration.CreateRepositoryWithValidationAsync("postgres", cancellationToken);
```

#### Method 2: Direct Creation (Legacy)
```csharp
// Direct creation (legacy method)
var repository = SimulatorConfiguration.CreateRepository("postgres"); // or "geopackage"
```

### Changing Database Type

To switch from PostgreSQL to GeoPackage, change this line in `SimulatorConfiguration.cs`:

```csharp
// Change from:
public const string DEFAULT_DATABASE_TYPE = "postgres";

// To:
public const string DEFAULT_DATABASE_TYPE = "geopackage";
```

## Testing the Connection Features

### Test Scripts

1. **`test_connection_fallback.ps1`** - Tests connection handling (now shows failure behavior)
2. **`test_connection_validation.ps1`** - Connection validation only

### Running Tests

```powershell
# Test the connection validation and failure handling
.\test_connection_fallback.ps1

# Test just the connection validation
.\test_connection_validation.ps1
```

## Expected Behavior

### When PostgreSQL is Available
```
============================================================
DATABASE CONNECTION SETUP
============================================================
Database type: POSTGRES
Testing PostgreSQL connection...
Target: 192.168.1.97:5432, Database: postgres, User: anders
Timeout: 10 seconds
  Attempt 1/3: Connecting...
  SUCCESS: PostgreSQL connection established!
? PostgreSQL connection successful - using PostgreSQL database
============================================================
```

### When PostgreSQL is Unreachable (Failure with Instructions)
```
============================================================
DATABASE CONNECTION SETUP
============================================================
Database type: POSTGRES
Testing PostgreSQL connection...
Target: 192.168.1.97:5432, Database: postgres, User: anders
Timeout: 10 seconds
  Attempt 1/3: Connecting...
  TIMEOUT: Connection attempt 1 timed out after 10 seconds
  Waiting 2000ms before retry...
  Attempt 2/3: Connecting...
  TIMEOUT: Connection attempt 2 timed out after 10 seconds
  Waiting 2000ms before retry...
  Attempt 3/3: Connecting...
  TIMEOUT: Connection attempt 3 timed out after 10 seconds
? PostgreSQL connection failed: Connection failed: All 3 attempts timed out after 10 seconds each

NETWORK TROUBLESHOOTING:
- Check if PostgreSQL server is running on 192.168.1.97:5432, Database: postgres, User: anders
- Verify network connectivity to the database server
- Check firewall settings on both client and server
- Ensure PostgreSQL is configured to accept connections
- Verify credentials and database permissions

DATABASE CONFIGURATION OPTIONS:
1. Fix the PostgreSQL connection issue above
2. Change DEFAULT_DATABASE_TYPE to "geopackage" in SimulatorConfiguration.cs
3. Use a local PostgreSQL instance (change connection string)
============================================================

Database connection failed: PostgreSQL connection failed: Connection failed: All 3 attempts timed out after 10 seconds each. Please fix the connection issue or change to a different database type.

Simulation cannot proceed without a valid database connection.
Please resolve the database connection issue and try again.
```

## Troubleshooting

### Common Connection Issues

1. **Connection Timeout**
   - Server unreachable (network/firewall)
   - Server overloaded
   - Incorrect IP address or port

2. **Authentication Failed**
   - Incorrect username/password
   - User doesn't have database permissions

3. **Database Not Found**
   - Database name misspelled
   - Database doesn't exist on server

### Quick Fixes

1. **Check Network Connectivity**
   ```powershell
   Test-NetConnection -ComputerName 192.168.1.97 -Port 5432
   ```

2. **Switch to Local Database**
   - Change `DEFAULT_DATABASE_TYPE` to `"geopackage"` in `SimulatorConfiguration.cs`

3. **Update Connection String**
   - Modify `POSTGRES_CONNECTION_STRING` with correct server details

4. **Use Local PostgreSQL**
   - Install PostgreSQL locally and change host to `localhost`

## Benefits

1. **User Control**: No automatic decisions - user chooses the database type
2. **Clear Feedback**: Detailed error messages and troubleshooting steps
3. **Flexible**: Works with both PostgreSQL and GeoPackage
4. **Robust**: Proper timeout and retry logic
5. **Informative**: Detailed logging for diagnosis
6. **Predictable**: Fails fast and clearly when connections don't work

## Design Philosophy

The system now follows a **"fail fast and clear"** approach:

- **No surprises**: The system does what you configure it to do
- **Clear feedback**: When things fail, you know exactly why and how to fix it
- **User control**: You decide which database to use, not the system
- **Explicit configuration**: Database type is explicitly set in configuration

## Files Modified

- `RoverSimulator/SimulatorConfiguration.cs` - Removed automatic fallback, added clear failure handling
- `RoverSimulator/ProgramRoverSim.cs` - Updated to handle connection failures explicitly
- `RoverSimulator/PostgresRoverDataRepository.cs` - Enhanced timeout handling and logging

The system now provides excellent diagnostics and user guidance while maintaining full user control over database selection.