using Microsoft.Extensions.Hosting;
using ReadRoverDBStubLibrary;
using System.Collections.Concurrent;

namespace ScentPolygonLibrary;

/// <summary>
/// Service that continuously monitors rover measurements and generates scent polygons on-demand
/// </summary>
public class ScentPolygonService : IHostedService, IDisposable
{
    private readonly IRoverDataReader _dataReader;
    private readonly ScentPolygonConfiguration _configuration;
    private readonly Timer _pollTimer;
    private readonly ConcurrentDictionary<int, ScentPolygonResult> _polygons;
    private bool _disposed;
    private int _lastKnownSequence = -1;
    private ScentPolygonResult? _latestPolygon;

    // Unified cache & versioning
    private UnifiedScentPolygon? _unifiedCache;
    private bool _unifiedDirty = true;
    private int _unifiedVersion = 0;
    private readonly object _unifiedLock = new();

    // Events for notifications
    public event EventHandler<ScentPolygonUpdateEventArgs>? PolygonsUpdated;
    public event EventHandler<ScentPolygonStatusEventArgs>? StatusUpdate;

    public ScentPolygonService(
        IRoverDataReader dataReader,
        ScentPolygonConfiguration? configuration = null,
        int pollIntervalMs = 1000)
    {
        _dataReader = dataReader ?? throw new ArgumentNullException(nameof(dataReader));
        _configuration = configuration ?? new ScentPolygonConfiguration();
        _polygons = new ConcurrentDictionary<int, ScentPolygonResult>();
        _pollTimer = new Timer(PollForNewMeasurements, null, Timeout.Infinite, pollIntervalMs);
    }

    /// <summary>
    /// Gets the current count of polygons in the collection
    /// </summary>
    public int Count => _polygons.Count;

    /// <summary>
    /// Gets the latest polygon
    /// </summary>
    public ScentPolygonResult? LatestPolygon => _latestPolygon;

    /// <summary>
    /// Gets the unified polygon version
    /// </summary>
    public int UnifiedVersion => _unifiedVersion;

    /// <summary>
    /// Gets all polygons as a list ordered by sequence
    /// </summary>
    public List<ScentPolygonResult> GetAllPolygons()
    {
        return _polygons.Values.OrderBy(p => p.Sequence).ToList();
    }

    /// <summary>
    /// Gets polygons for a specific session
    /// </summary>
    public List<ScentPolygonResult> GetPolygonsForSession(Guid sessionId)
    {
        return _polygons.Values
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.Sequence)
            .ToList();
    }

    /// <summary>
    /// Gets the latest N polygons
    /// </summary>
    public List<ScentPolygonResult> GetLatestPolygons(int count)
    {
        return _polygons.Values
            .OrderByDescending(p => p.Sequence)
            .Take(count)
            .OrderBy(p => p.Sequence)
            .ToList();
    }

    /// <summary>
    /// Gets polygons within a time range
    /// </summary>
    public List<ScentPolygonResult> GetPolygonsInTimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        return _polygons.Values
            .Where(p => p.RecordedAt >= start && p.RecordedAt <= end)
            .OrderBy(p => p.Sequence)
            .ToList();
    }

    /// <summary>
    /// Creates a unified scent polygon by combining all individual polygons using Union operation.
    /// This represents the total coverage area of all scent detections.
    /// </summary>
    public UnifiedScentPolygon? GetUnifiedScentPolygon()
    {
        var allPolygons = GetAllPolygons();
        
        if (!allPolygons.Any())
        {
            return null;
        }

        return ScentPolygonCalculator.CreateUnifiedPolygon(allPolygons);
    }

    /// <summary>
    /// Cached version - recomputes only when new polygons added.
    /// </summary>
    public UnifiedScentPolygon? GetUnifiedScentPolygonCached()
    {
        // Check if cache is dirty or null first (outside lock for performance)
        if (!_unifiedDirty && _unifiedCache != null) 
        {
            return _unifiedCache;
        }
        
        lock (_unifiedLock)
        {
            // Double-check pattern
            if (!_unifiedDirty && _unifiedCache != null) 
            {
                return _unifiedCache;
            }
            
            var all = GetAllPolygons();
            if (!all.Any()) 
            {
                _unifiedCache = null;
                _unifiedDirty = false;
                return null;
            }
            
            try
            {
                Console.WriteLine($"[ScentService] Regenerating unified polygon from {all.Count} polygons");
                _unifiedCache = ScentPolygonCalculator.CreateUnifiedPolygon(all);
                _unifiedDirty = false;
                Console.WriteLine($"[ScentService] Unified polygon regenerated successfully. Version={_unifiedVersion}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScentService] Unified polygon creation failed: {ex.Message}");
                _unifiedCache = null;
                _unifiedDirty = false; // Mark as clean to prevent infinite retry
            }
            
            return _unifiedCache;
        }
    }

    /// <summary>
    /// Creates a unified scent polygon for a specific session by combining all polygons from that session.
    /// </summary>
    public UnifiedScentPolygon? GetUnifiedScentPolygonForSession(Guid sessionId)
    {
        var sessionPolygons = GetPolygonsForSession(sessionId);
        
        if (!sessionPolygons.Any())
        {
            return null;
        }

        return ScentPolygonCalculator.CreateUnifiedPolygon(sessionPolygons);
    }

    /// <summary>
    /// Creates a unified scent polygon for the latest N polygons.
    /// Useful for showing recent coverage area.
    /// </summary>
    public UnifiedScentPolygon? GetUnifiedScentPolygonForLatest(int count)
    {
        var recentPolygons = GetLatestPolygons(count);
        
        if (!recentPolygons.Any())
        {
            return null;
        }

        return ScentPolygonCalculator.CreateUnifiedPolygon(recentPolygons);
    }

    /// <summary>
    /// Creates a unified scent polygon for polygons within a time range.
    /// </summary>
    public UnifiedScentPolygon? GetUnifiedScentPolygonForTimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        var timeRangePolygons = GetPolygonsInTimeRange(start, end);
        
        if (!timeRangePolygons.Any())
        {
            return null;
        }

        return ScentPolygonCalculator.CreateUnifiedPolygon(timeRangePolygons);
    }

    /// <summary>
    /// Generates a scent polygon for a specific measurement (on-demand calculation)
    /// </summary>
    public ScentPolygonResult GeneratePolygonForMeasurement(RoverMeasurement measurement)
    {
        var polygon = ScentPolygonCalculator.CreateScentPolygon(
            measurement.Latitude,
            measurement.Longitude,
            measurement.WindDirectionDeg,
            measurement.WindSpeedMps,
            _configuration);

        var scentArea = ScentPolygonCalculator.CalculateScentAreaM2(polygon, measurement.Latitude);
        var maxDistance = ScentPolygonCalculator.CalculateMaxScentDistance(measurement.WindSpeedMps);

        return new ScentPolygonResult
        {
            Polygon = polygon,
            SessionId = measurement.SessionId,
            Sequence = measurement.Sequence,
            RecordedAt = measurement.RecordedAt,
            Latitude = measurement.Latitude,
            Longitude = measurement.Longitude,
            WindDirectionDeg = measurement.WindDirectionDeg,
            WindSpeedMps = measurement.WindSpeedMps,
            ScentAreaM2 = scentArea,
            MaxDistanceM = maxDistance
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _dataReader.InitializeAsync(cancellationToken);
        
        // Load initial data
        await LoadInitialPolygons();
        
        // Start polling for new measurements
        _pollTimer.Change(0, 1000); // Poll every second
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    private async Task LoadInitialPolygons()
    {
        const int maxRetries = 5;
        const int retryDelayMs = 2000;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var measurements = await _dataReader.GetAllMeasurementsAsync();
                
                if (measurements.Any())
                {
                    foreach (var m in measurements)
                    {
                        var polygon = GeneratePolygonForMeasurement(m);
                        
                        if (polygon.IsValid && _polygons.TryAdd(polygon.Sequence, polygon))
                        {
                            if (_latestPolygon == null || polygon.Sequence > _latestPolygon.Sequence)
                            {
                                _latestPolygon = polygon;
                                _lastKnownSequence = polygon.Sequence;
                            }
                        }
                    }
                    _unifiedDirty = true; _unifiedVersion++;
                    Console.WriteLine($"[ScentService] Loaded {measurements.Count} initial polygons. UnifiedVersion={_unifiedVersion}");
                    return; // Exit after successful load
                }

                Console.WriteLine($"[ScentService] No initial measurements (attempt {i + 1}/{maxRetries}). Retrying...");
                await Task.Delay(retryDelayMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScentService] Error loading initial polygons (attempt {i + 1}): {ex.Message}");
                await Task.Delay(retryDelayMs);
            }
        }
        
        Console.WriteLine("[ScentService] Failed to load initial polygons after retries.");
    }

    private async void PollForNewMeasurements(object? state)
    {
        if (_disposed) return;
        try
        {
            Console.WriteLine($"[ScentService] Polling for new measurements. LastKnownSeq={_lastKnownSequence}");
            var newMeasurements = await _dataReader.GetNewMeasurementsAsync(_lastKnownSequence);
            Console.WriteLine($"[ScentService] Poll returned {newMeasurements.Count} new measurements");
            
            if (newMeasurements.Any())
            {
                var newPolygons = new List<ScentPolygonResult>();
                foreach (var m in newMeasurements)
                {
                    Console.WriteLine($"[ScentService] Processing measurement seq={m.Sequence}, time={m.RecordedAt:HH:mm:ss}");
                    var polygon = GeneratePolygonForMeasurement(m);
                    if (polygon.IsValid && _polygons.TryAdd(polygon.Sequence, polygon))
                    {
                        newPolygons.Add(polygon);
                        if (_latestPolygon == null || polygon.Sequence > _latestPolygon.Sequence)
                        {
                            _latestPolygon = polygon;
                            _lastKnownSequence = polygon.Sequence;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[ScentService] Skipped measurement seq={m.Sequence} (invalid or duplicate)");
                    }
                }
                if (newPolygons.Any())
                {
                    var oldVersion = _unifiedVersion;
                    // Force cache invalidation and version increment
                    lock (_unifiedLock)
                    {
                        _unifiedDirty = true;
                        _unifiedCache = null; // Clear cache to force regeneration
                        _unifiedVersion++;
                    }
                    Console.WriteLine($"[ScentService] UNIFIED UPDATED: Added {newPolygons.Count} polygons. Total={_polygons.Count}. Version {oldVersion} -> {_unifiedVersion}");
                    
                    // Fire events
                    PolygonsUpdated?.Invoke(this, new ScentPolygonUpdateEventArgs { NewPolygons = newPolygons, TotalPolygonCount = _polygons.Count, LatestPolygon = _latestPolygon });
                }
            }
            else
            {
                Console.WriteLine($"[ScentService] No new measurements found");
            }
            
            // Status update every 10 seconds
            if (DateTime.UtcNow.Second % 10 == 0)
            {
                Console.WriteLine($"[ScentService] Status: Count={_polygons.Count}, Latest={_latestPolygon?.Sequence ?? -1}, Version={_unifiedVersion}, CacheDirty={_unifiedDirty}");
                StatusUpdate?.Invoke(this, new ScentPolygonStatusEventArgs { TotalPolygonCount = _polygons.Count, LatestPolygon = _latestPolygon, DataSource = _dataReader.GetType().Name });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScentService] Poll error: {ex.Message}");
        }
    }

    public UnifiedScentPolygon? ForceRecomputeUnified()
    {
        lock (_unifiedLock)
        {
            var all = GetAllPolygons();
            if (!all.Any()) 
            {
                _unifiedCache = null;
                return null;
            }
            
            try
            {
                Console.WriteLine($"[ScentService] Force recomputing unified polygon from {all.Count} polygons");
                _unifiedCache = ScentPolygonCalculator.CreateUnifiedPolygon(all);
                _unifiedDirty = false; // Cache is now current
                _unifiedVersion++; // Explicit version bump for forced rebuild
                Console.WriteLine($"[ScentService] Forced unified recompute completed. Version={_unifiedVersion}");
                return _unifiedCache;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ScentService] Forced recompute failed: {ex.Message}");
                _unifiedCache = null;
                _unifiedDirty = false;
                return null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Dispose();
        _dataReader.Dispose();
    }
}