# Database Architecture Refactoring Plan

## Current Issues

The current database handling implementation has several architectural problems:

### 1. Split Read/Write Responsibilities
- **Read operations**: Located in `ReadRoverDBStubLibrary`
- **Write operations**: Located in `RoverSimulator`
- **Problem**: No single source of truth; violates Single Responsibility Principle

### 2. Static Factory Pattern Anti-Pattern
- `RoverDataReaderFactory` uses static methods
- Configuration-driven factory creates instances manually
- **Problem**: Bypasses dependency injection container; difficult to test and maintain

### 3. Resource Management Issues
- `GeoPackageRoverDataRepository` keeps file handles open for entire instance lifetime
- Can cause file locking issues (QGIS, ArcGIS cannot open the file)
- **Problem**: Not suitable for long-running applications; prevents concurrent access

### 4. Configuration String-Based Selection
```csharp
// Current approach - fragile and not testable
var reader = config.DatabaseType == "postgres" 
    ? new PostgresReader(...) 
    : new GeoPackageReader(...);
```
- **Problem**: No compile-time safety; difficult to mock for testing

### 5. Singleton Abuse
- Services registered as `AddSingleton` that should be scoped
- Holds connections/resources for application lifetime
- **Problem**: Resource leaks, connection pool exhaustion

## Proposed Architecture

### 1. Unified Repository Library
**New structure**: `RoverData.Repository` (single library)

```
RoverData.Repository/
├── IRoverDataRepository.cs           // Unified interface (read + write)
├── Postgres/
│   ├── PostgresRoverDataRepository.cs
│   └── PostgresRepositoryOptions.cs
├── GeoPackage/
│   ├── GeoPackageRoverDataRepository.cs
│   └── GeoPackageRepositoryOptions.cs
└── Models/
    └── RoverMeasurement.cs
```

### 2. Proper Dependency Injection
**In Program.cs** (Blazor/Console apps):

```csharp
// Configuration determines implementation
var dbType = configuration["Database:DatabaseType"];

if (dbType == "postgres")
{
    services.AddScoped<IRoverDataRepository, PostgresRoverDataRepository>();
    services.Configure<PostgresRepositoryOptions>(
        configuration.GetSection("Database:Postgres"));
}
else // geopackage
{
    services.AddScoped<IRoverDataRepository, GeoPackageRoverDataRepository>();
    services.Configure<GeoPackageRepositoryOptions>(
        configuration.GetSection("Database:GeoPackage"));
}
```

### 3. Scoped Lifecycle Management
- **Web applications**: `AddScoped` - one instance per HTTP request
- **Console applications**: Manual scope creation for operations
- **GeoPackage**: Open connection at scope start, close at scope end

**Benefits**:
- Automatic resource cleanup via `IDisposable`
- No file locking issues
- Connection pooling for Postgres
- Testable with standard DI patterns

### 4. Repository Implementation Pattern

```csharp
public class PostgresRoverDataRepository : IRoverDataRepository
{
    private readonly PostgresRepositoryOptions _options;
    private readonly NpgsqlDataSource _dataSource;
    
    // Constructor injection - no static dependencies
    public PostgresRoverDataRepository(IOptions<PostgresRepositoryOptions> options)
    {
        _options = options.Value;
        _dataSource = CreateDataSource();
    }
    
    // Implement both read and write operations
    public async Task<RoverMeasurement> GetByIdAsync(Guid id) { }
    public async Task SaveAsync(RoverMeasurement measurement) { }
    
    public void Dispose() => _dataSource?.Dispose();
}
```

### 5. Service Layer (Optional but Recommended)

```csharp
public class RoverDataService
{
    private readonly IRoverDataRepository _repository;
    
    public RoverDataService(IRoverDataRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<ScentPolygon> CalculateScentArea(Guid sessionId)
    {
        var measurements = await _repository.GetBySessionAsync(sessionId);
        // Business logic here
    }
}
```

## Why This Matters

### Maintainability
- **Single location** for all database operations
- Changes to database schema affect one library
- Easier to understand and navigate codebase

### Testability
- Mock `IRoverDataRepository` in unit tests
- No static dependencies to work around
- Standard .NET testing patterns apply

### Resource Management
- Automatic cleanup via scoped services
- No file locking issues with GeoPackage
- Proper connection pooling for Postgres
- Better suited for production environments

### SOLID Principles Compliance
- **S**ingle Responsibility: Each repository handles one data source
- **O**pen/Closed: Add new implementations without modifying existing code
- **L**iskov Substitution: All implementations interchangeable via interface
- **I**nterface Segregation: Clean, focused interface
- **D**ependency Inversion: Depend on abstractions, not concrete types

## Conclusion

This refactoring transforms the codebase from an anti-pattern implementation to a modern, maintainable .NET architecture. While it requires short-term effort, the long-term benefits in maintainability, testability, and scalability are substantial.
