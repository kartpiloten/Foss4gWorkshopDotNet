using MapPiloteGeopackageHelper;
using NetTopologySuite.Geometries;
using System.Runtime.Intrinsics.X86;

namespace RoverSimulator;

/// <summary>
/// Configuration and verification utilities for the rover simulator
/// </summary>
public static class SimulatorConfiguration
{
    // Database configuration
    //public const string DEFAULT_DATABASE_TYPE = "geopackage";
    public const string DEFAULT_DATABASE_TYPE = "postgres";

    //   public const string POSTGRES_CONNECTION_STRING = "Host=localhost;Port=5432;Username=anders;Password=tua123;Database=AucklandRoverData";
    //   public const string POSTGRES_CONNECTION_STRING = "Host=localhost;Port=5432;Username=anders;Password=tua123;Database=AucklandRoverData";
    public const string POSTGRES_CONNECTION_STRING = "Host=192.168.1.254;Port=5432;Username=anders;Password=tua123;SSL Mode = Disable;Database=AucklandRoverData;Timeout=10;Command Timeout=30";
    public const string GEOPACKAGE_FOLDER_PATH = @"C:\temp\Rover1\";

    // Connection validation settings
    public const int CONNECTION_TIMEOUT_SECONDS = 10;
    public const int MAX_RETRY_ATTEMPTS = 3;
    public const int RETRY_DELAY_MS = 2000;

    // Forest boundary - loaded from GeoPackage
    private static Polygon? _forestBoundary;
    private static Envelope? _boundingBox;

    // Default starting position (will be calculated from polygon centroid)
    public static double DEFAULT_START_LATITUDE => GetForestCentroid().Y;
    public static double DEFAULT_START_LONGITUDE => GetForestCentroid().X;

    // Bounding box properties (calculated from polygon)
    public static double MIN_LATITUDE => GetBoundingBox().MinY;
    public static double MAX_LATITUDE => GetBoundingBox().MaxY;
    public static double MIN_LONGITUDE => GetBoundingBox().MinX;
    public static double MAX_LONGITUDE => GetBoundingBox().MaxX;

    /// <summary>
    /// Tests PostgreSQL database connectivity with timeout and retry logic
    /// </summary>
    public static async Task<(bool isConnected, string errorMessage)> TestPostgresConnectionAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("Testing PostgreSQL connection...");
        Console.WriteLine($"Target: {GetPostgresServerInfo()}");
        Console.WriteLine($"Timeout: {CONNECTION_TIMEOUT_SECONDS} seconds");
        
        for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(CONNECTION_TIMEOUT_SECONDS));
                
                // Create test repository to validate connection
                using var testRepo = new PostgresRoverDataRepository(POSTGRES_CONNECTION_STRING);
                
                Console.WriteLine($"  Attempt {attempt}/{MAX_RETRY_ATTEMPTS}: Connecting...");
                
                // Try to initialize - this will test the full connection including database creation
                await testRepo.InitializeAsync(cts.Token);
                
                Console.WriteLine("  SUCCESS: PostgreSQL connection established!");
                return (true, string.Empty);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return (false, "Connection test cancelled by user");
            }
            catch (OperationCanceledException)
            {
                var message = $"  TIMEOUT: Connection attempt {attempt} timed out after {CONNECTION_TIMEOUT_SECONDS} seconds";
                Console.WriteLine(message);
                
                if (attempt == MAX_RETRY_ATTEMPTS)
                {
                    return (false, $"Connection failed: All {MAX_RETRY_ATTEMPTS} attempts timed out after {CONNECTION_TIMEOUT_SECONDS} seconds each");
                }
            }
            catch (Exception ex)
            {
                var message = $"  ERROR: Connection attempt {attempt} failed: {ex.Message}";
                Console.WriteLine(message);
                
                // For specific error types, don't retry
                if (ex.Message.Contains("authentication failed") || 
                    ex.Message.Contains("database") && ex.Message.Contains("does not exist") ||
                    ex.Message.Contains("permission denied"))
                {
                    return (false, $"Connection failed: {ex.Message}");
                }
                
                if (attempt == MAX_RETRY_ATTEMPTS)
                {
                    return (false, $"Connection failed after {MAX_RETRY_ATTEMPTS} attempts: {ex.Message}");
                }
            }
            
            if (attempt < MAX_RETRY_ATTEMPTS)
            {
                Console.WriteLine($"  Waiting {RETRY_DELAY_MS}ms before retry...");
                await Task.Delay(RETRY_DELAY_MS, cancellationToken);
            }
        }
        
        return (false, "Connection failed: Unknown error");
    }

    /// <summary>
    /// Gets a human-readable description of the PostgreSQL server info
    /// </summary>
    private static string GetPostgresServerInfo()
    {
        try
        {
            var connectionString = POSTGRES_CONNECTION_STRING;
            var parts = new List<string>();
            
            if (connectionString.Contains("Host="))
            {
                var host = ExtractConnectionStringValue(connectionString, "Host");
                var port = ExtractConnectionStringValue(connectionString, "Port") ?? "5432";
                parts.Add($"{host}:{port}");
            }
            
            var database = ExtractConnectionStringValue(connectionString, "Database");
            if (!string.IsNullOrEmpty(database))
            {
                parts.Add($"Database: {database}");
            }
            
            var username = ExtractConnectionStringValue(connectionString, "Username");
            if (!string.IsNullOrEmpty(username))
            {
                parts.Add($"User: {username}");
            }
            
            return parts.Any() ? string.Join(", ", parts) : "PostgreSQL";
        }
        catch
        {
            return "PostgreSQL";
        }
    }

    private static string? ExtractConnectionStringValue(string connectionString, string key)
    {
        try
        {
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var keyValue = parts.FirstOrDefault(p => p.Trim().StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase));
            return keyValue?.Substring(keyValue.IndexOf('=') + 1).Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates the appropriate data repository with connection validation (no automatic fallback)
    /// </summary>
    public static async Task<IRoverDataRepository> CreateRepositoryWithValidationAsync(string databaseType, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"\n{new string('=', 60)}");
        Console.WriteLine("DATABASE CONNECTION SETUP");
        Console.WriteLine($"{new string('=', 60)}");
        Console.WriteLine($"Database type: {databaseType.ToUpper()}");
        
        if (databaseType.ToLower() == "postgres")
        {
            var (isConnected, errorMessage) = await TestPostgresConnectionAsync(cancellationToken);
            
            if (isConnected)
            {
                Console.WriteLine("SUCCESS: PostgreSQL connection successful - using PostgreSQL database");
                Console.WriteLine($"{new string('=', 60)}");
                return new PostgresRoverDataRepository(POSTGRES_CONNECTION_STRING);
            }
            else
            {
                Console.WriteLine($"ERROR: PostgreSQL connection failed: {errorMessage}");
                Console.WriteLine();
                Console.WriteLine("NETWORK TROUBLESHOOTING:");
                Console.WriteLine($"- Check if PostgreSQL server is running on {GetPostgresServerInfo()}");
                Console.WriteLine("- Verify network connectivity to the database server");
                Console.WriteLine("- Check firewall settings on both client and server");
                Console.WriteLine("- Ensure PostgreSQL is configured to accept connections");
                Console.WriteLine("- Verify credentials and database permissions");
                Console.WriteLine();
                Console.WriteLine("DATABASE CONFIGURATION OPTIONS:");
                Console.WriteLine("1. Fix the PostgreSQL connection issue above");
                Console.WriteLine("2. Change DEFAULT_DATABASE_TYPE to \"geopackage\" in SimulatorConfiguration.cs");
                Console.WriteLine("3. Use a local PostgreSQL instance (change connection string)");
                Console.WriteLine($"{new string('=', 60)}");
                
                throw new InvalidOperationException($"PostgreSQL connection failed: {errorMessage}. Please fix the connection issue or change to a different database type.");
            }
        }
        else if (databaseType.ToLower() == "geopackage")
        {
            Console.WriteLine("SUCCESS: Using GeoPackage database (local file storage)");
            Console.WriteLine($"{new string('=', 60)}");
            return new GeoPackageRoverDataRepository(GEOPACKAGE_FOLDER_PATH);
        }
        else
        {
            throw new ArgumentException($"Unsupported database type: {databaseType}. Use 'postgres' or 'geopackage'.");
        }
    }

    /// <summary>
    /// Creates the appropriate data repository based on database type (legacy method)
    /// </summary>
    public static IRoverDataRepository CreateRepository(string databaseType)
    {
        return databaseType.ToLower() switch
        {
            "postgres" => new PostgresRoverDataRepository(POSTGRES_CONNECTION_STRING),
            "geopackage" => new GeoPackageRoverDataRepository(GEOPACKAGE_FOLDER_PATH),
            _ => throw new ArgumentException($"Unsupported database type: {databaseType}")
        };
    }

    /// <summary>
    /// Gets the forest boundary polygon from the GeoPackage file
    /// </summary>
    public static async Task<Polygon> GetForestBoundaryAsync()
    {
        if (_forestBoundary != null)
            return _forestBoundary;

        try
        {
            // Use relative path to the solutionresources folder
            var geoPackagePath = Path.Combine(GetSolutionDirectory(), "Solutionresources", "RiverHeadForest.gpkg");
            
            if (!File.Exists(geoPackagePath))
            {
                throw new FileNotFoundException($"RiverHeadForest.gpkg not found at: {geoPackagePath}");
            }

            Console.WriteLine($"Loading forest boundary from: {geoPackagePath}");

            using var geoPackage = await GeoPackage.OpenAsync(geoPackagePath, 4326);
            var layer = await geoPackage.EnsureLayerAsync("riverheadforest", new Dictionary<string, string>(), 4326);

            // Read the first (and only) feature from the riverheadforest layer
            var readOptions = new ReadOptions(IncludeGeometry: true, Limit: 1);
            
            await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
            {
                if (feature.Geometry is Polygon polygon)
                {
                    _forestBoundary = polygon;
                    _boundingBox = polygon.EnvelopeInternal;
                    
                    Console.WriteLine($"Forest boundary loaded successfully:");
                    Console.WriteLine($"  Bounding box: ({_boundingBox.MinX:F6}, {_boundingBox.MinY:F6}) to ({_boundingBox.MaxX:F6}, {_boundingBox.MaxY:F6})");
                    Console.WriteLine($"  Centroid: ({polygon.Centroid.X:F6}, {polygon.Centroid.Y:F6})");
                    
                    return _forestBoundary;
                }
            }

            throw new InvalidDataException("No polygon geometry found in the riverheadforest layer");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading forest boundary: {ex.Message}");
            Console.WriteLine("Falling back to default coordinates...");
            
            // Fallback to original hardcoded coordinates
            _forestBoundary = CreateFallbackPolygon();
            _boundingBox = _forestBoundary.EnvelopeInternal;
            
            return _forestBoundary;
        }
    }

    /// <summary>
    /// Checks if a point is within the forest boundary
    /// </summary>
    public static async Task<bool> IsPointInForestAsync(double latitude, double longitude)
    {
        var boundary = await GetForestBoundaryAsync();
        var point = new Point(longitude, latitude) { SRID = 4326 };
        return boundary.Contains(point);
    }

    /// <summary>
    /// Gets the bounding box envelope (loads polygon if needed)
    /// </summary>
    private static Envelope GetBoundingBox()
    {
        if (_boundingBox != null)
            return _boundingBox;

        // If not loaded yet, load synchronously (this should ideally be called after async initialization)
        try
        {
            var boundary = GetForestBoundaryAsync().GetAwaiter().GetResult();
            return _boundingBox ?? boundary.EnvelopeInternal;
        }
        catch
        {
            // Fallback coordinates if loading fails
            return new Envelope(174.55, 174.70, -36.78, -36.70);
        }
    }

    /// <summary>
    /// Gets the forest centroid (loads polygon if needed)
    /// </summary>
    private static Point GetForestCentroid()
    {
        try
        {
            var boundary = GetForestBoundaryAsync().GetAwaiter().GetResult();
            return boundary.Centroid;
        }
        catch
        {
            // Fallback to original center coordinates
            return new Point(174.60, -36.75) { SRID = 4326 };
        }
    }

    /// <summary>
    /// Creates a fallback polygon using the original hardcoded coordinates
    /// </summary>
    private static Polygon CreateFallbackPolygon()
    {
        var coordinates = new[]
        {
            new Coordinate(174.55, -36.78), // SW
            new Coordinate(174.70, -36.78), // SE
            new Coordinate(174.70, -36.70), // NE
            new Coordinate(174.55, -36.70), // NW
            new Coordinate(174.55, -36.78)  // Close the ring
        };
        
        var linearRing = new LinearRing(coordinates);
        return new Polygon(linearRing) { SRID = 4326 };
    }

    /// <summary>
    /// Gets the solution directory path
    /// </summary>
    private static string GetSolutionDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var directory = new DirectoryInfo(currentDir);
        
        // Look for solution file or go up to find it
        while (directory != null && !directory.GetFiles("*.sln").Any())
        {
            directory = directory.Parent;
        }
        
        return directory?.FullName ?? currentDir;
    }

    /// <summary>
    /// Verifies existing GeoPackage contents
    /// </summary>
    public static async Task<bool> VerifyGeoPackageAsync(string filePath)
    {
        Console.WriteLine("Verifying existing GeoPackage contents...");
        
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"No GeoPackage file found at: {filePath}");
            Console.WriteLine("Run the simulator first to create data.");
            return false;
        }
        
        try
        {
            using var geoPackage = await GeoPackage.OpenAsync(filePath, 4326);
            var layer = await geoPackage.EnsureLayerAsync("rover_measurements", new Dictionary<string, string>(), 4326);
            
            var totalCount = await layer.CountAsync();
            Console.WriteLine($"Total measurements: {totalCount}");
            
            var fileInfo = new FileInfo(filePath);
            Console.WriteLine($"File size: {fileInfo.Length / 1024.0:F1} KB");
            Console.WriteLine($"File path: {fileInfo.FullName}");
            Console.WriteLine($"Last modified: {fileInfo.LastWriteTime}");
            
            if (totalCount > 0)
            {
                Console.WriteLine("\nSample data (first 3 records):");
                var readOptions = new ReadOptions(IncludeGeometry: true, OrderBy: "sequence ASC", Limit: 3);
                
                int count = 0;
                await foreach (var feature in layer.ReadFeaturesAsync(readOptions))
                {
                    var sequence = feature.Attributes["sequence"];
                    var latitude = feature.Attributes["latitude"];
                    var longitude = feature.Attributes["longitude"];
                    var windSpeed = feature.Attributes["wind_speed_mps"];
                    var windDir = feature.Attributes["wind_direction_deg"];
                    
                    Console.WriteLine($"  {++count}. Seq: {sequence}, Location: ({latitude?[..8]}, {longitude?[..8]}), Wind: {windSpeed}m/s @ {windDir}deg");
                }
                
                Console.WriteLine($"\nGeoPackage verification successful! The file contains {totalCount} rover measurements.");
                Console.WriteLine("You can open this file in QGIS, ArcGIS, or other GIS software to visualize the rover track.");
                return true;
            }
            else
            {
                Console.WriteLine("GeoPackage exists but contains no data.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error verifying GeoPackage: {ex.Message}");
            return false;
        }
    }
}