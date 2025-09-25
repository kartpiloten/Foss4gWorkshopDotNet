# Quick Database Configuration Guide

## How to Choose Your Database

The Rover Simulator supports two database types. Here's how to configure each:

## Option 1: PostgreSQL Database (Default)

**Current Configuration:**
```csharp
public const string DEFAULT_DATABASE_TYPE = "postgres";
```

**When to use:**
- You have a PostgreSQL server available
- You want centralized data storage
- Multiple applications need access to the data

**Requirements:**
- PostgreSQL server running and accessible
- Network connectivity to the database server
- Valid credentials and database permissions

## Option 2: GeoPackage (Local Files)

**To switch to GeoPackage, change this line in `RoverSimulator/SimulatorConfiguration.cs`:**

```csharp
// Change from:
public const string DEFAULT_DATABASE_TYPE = "postgres";

// To:
public const string DEFAULT_DATABASE_TYPE = "geopackage";
```

**When to use:**
- No database server available
- You want local file storage
- Working offline or in isolated environments
- Quick testing and development

**Benefits of GeoPackage:**
- No server setup required
- Works completely offline
- Files can be easily shared
- Compatible with QGIS, ArcGIS, and other GIS software

## Making the Change

### Step 1: Open the Configuration File
Open `RoverSimulator/SimulatorConfiguration.cs` in your editor.

### Step 2: Change the Database Type
Find this line (around line 13):
```csharp
public const string DEFAULT_DATABASE_TYPE = "postgres";
```

Change it to:
```csharp
public const string DEFAULT_DATABASE_TYPE = "geopackage";
```

### Step 3: Save and Run
Save the file and run the simulator. It will now use GeoPackage files instead of PostgreSQL.

## Connection String Configuration

### PostgreSQL Connection String
If you need to change the PostgreSQL server details, modify this line:
```csharp
public const string POSTGRES_CONNECTION_STRING = 
    "Host=192.168.1.97;Port=5432;Username=anders;Password=tua123;Database=postgres;Timeout=10;Command Timeout=30";
```

Common changes:
- **Host**: Change `192.168.1.97` to your PostgreSQL server IP/hostname
- **Port**: Change `5432` if using a different port
- **Username/Password**: Update with your credentials
- **Database**: Change `postgres` to your target database name

### GeoPackage File Location
If you need to change where GeoPackage files are stored, modify this line:
```csharp
public const string GEOPACKAGE_FOLDER_PATH = @"C:\temp\Rover1\";
```

## Troubleshooting

### PostgreSQL Connection Issues
If you see connection timeouts or errors:
1. Check if the PostgreSQL server is running
2. Verify network connectivity: `Test-NetConnection -ComputerName 192.168.1.97 -Port 5432`
3. Check firewall settings
4. Verify credentials

**Quick fix**: Switch to GeoPackage as described above.

### GeoPackage File Lock Issues
If you see file lock errors:
1. Close QGIS, ArcGIS, or other GIS applications
2. Check for other RoverSimulator instances in Task Manager
3. Wait a few seconds and try again

## File Locations

### GeoPackage Files
- **Default location**: `C:\temp\Rover1\rover_data.gpkg`
- **Automatically created** when the simulator runs
- **Overwritten** each time the simulator starts

### Output Files
- View with QGIS: Open the `.gpkg` file directly
- View with ArcGIS: Import the GeoPackage layer
- Verify data: Run with `--verify` argument

## Quick Commands

```powershell
# Run the simulator
dotnet run --project RoverSimulator

# Verify existing GeoPackage data
dotnet run --project RoverSimulator -- --verify

# Test connection validation
.\test_connection_fallback.ps1
```

---

**Recommendation**: For development and testing, use GeoPackage. For production with multiple users, use PostgreSQL.