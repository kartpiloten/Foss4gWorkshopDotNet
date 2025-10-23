/*
 The functionallity in this file is:
 - Centralize minimal configuration access for ScentPolygonTester.
 - Wrap Microsoft.Extensions.Configuration to keep Program simple.
*/

using Microsoft.Extensions.Configuration; // .NET generic configuration
using ReadRoverDBStubLibrary; // DatabaseConfiguration + ReaderDefaults

namespace ScentPolygonTester;

public static class TesterConfiguration
{
    private static IConfiguration? _configuration;

    public static void Initialize(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    private static IConfiguration Configuration =>
        _configuration ?? throw new InvalidOperationException("TesterConfiguration.Initialize must be called before accessing configuration values.");

    public static string DefaultDatabaseType =>
        Configuration.GetValue<string>("DatabaseConfiguration:DatabaseType") ?? "geopackage";

    public static string GeoPackageFolderPath =>
        Configuration.GetValue<string>("DatabaseConfiguration:GeoPackageFolderPath") ?? @"C:\\temp\\Rover1/";

    public static string OutputFolderPath =>
        Configuration.GetValue<string>("Tester:OutputFolderPath") ?? GeoPackageFolderPath;

    public static string OutputGeoPackageFilename =>
        Configuration.GetValue<string>("Tester:OutputGeoPackageFilename") ?? "ScentPolygons.gpkg";

    public static string OutputGeoPackagePath =>
        Configuration.GetValue<string>("Tester:OutputGeoPackagePath") ??
        Path.Combine(OutputFolderPath, OutputGeoPackageFilename);

    // Factory to produce reader config based on appsettings
    public static DatabaseConfiguration CreateDatabaseConfig(string? databaseType = null)
    {
        var section = Configuration.GetSection("DatabaseConfiguration");
        var dbType = databaseType ?? section.GetValue<string>("DatabaseType") ?? "geopackage";

        return new DatabaseConfiguration
        {
            DatabaseType = dbType,
            PostgresConnectionString = section.GetValue<string>("PostgresConnectionString"),
            GeoPackageFolderPath = GeoPackageFolderPath,
            ConnectionTimeoutSeconds = section.GetValue<int?>("ConnectionTimeoutSeconds") ?? ReaderDefaults.DEFAULT_CONNECTION_TIMEOUT_SECONDS,
            MaxRetryAttempts = section.GetValue<int?>("MaxRetryAttempts") ?? ReaderDefaults.DEFAULT_MAX_RETRY_ATTEMPTS,
            RetryDelayMs = section.GetValue<int?>("RetryDelayMs") ?? ReaderDefaults.DEFAULT_RETRY_DELAY_MS
        };
    }
}
