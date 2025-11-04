/*
 The functionallity in this file is:
 - Provide a minimal background service that reads rover measurements and builds scent polygons.
 - Per-rover processing: each rover has its own thread that incrementally updates its unified polygon.
 - Unified combiner thread: combines all rover polygons into one total polygon.
 - Expose latest polygon, a cached unified polygon, and simple events for UI/consumers.
 - Keep implementation simple for learning (.NET HostedService, async/await, basic locking),
   and small FOSS4G notes (NTS for geometry, GeoPackage helper for file-based vector data).
*/

using Microsoft.Extensions.Hosting; // .NET HostedService pattern
using ReadRoverDBStubLibrary; // Data reader abstraction (async APIs)
using System.Collections.Concurrent; // Thread-safe dictionary for storing polygons
using MapPiloteGeopackageHelper; // GeoPackage helper (async APIs) - used only for forest intersection
using NetTopologySuite.Geometries; // NTS geometry types and operations

namespace ScentPolygonLibrary;

/// <summary>
/// Service that continuously monitors rover measurements and generates scent polygons on-demand
/// </summary>
public class ScentPolygonService : IHostedService, IDisposable
{
    private readonly IRoverDataReader _dataReader;
    private readonly ScentPolygonConfiguration _configuration;
    private readonly Timer _pollTimer; // simple periodic polling (no external scheduler)
    private readonly ConcurrentDictionary<int, ScentPolygonResult> _polygons;
    private readonly ConcurrentDictionary<Guid, RoverPolygonProcessor> _roverProcessors;
    private bool _disposed;
    private int _lastKnownSequence = -1; // kept for backward compatibility
    private DateTimeOffset _lastKnownTimestampUtc = DateTimeOffset.MinValue; // robust multi-rover polling
    private ScentPolygonResult? _latestPolygon;
    private readonly string? _forestGeoPackagePath;

    // Unified cache & versioning (small lock to protect updates)
    private UnifiedScentPolygon? _unifiedCache;
    private bool _unifiedDirty = true;
    private int _unifiedVersion = 0;
    private readonly object _unifiedLock = new();
    private Task? _unifiedCombinerTask;
    private CancellationTokenSource? _combinerCts;

    // Events for notifications (used by console/Blazor front-ends)
    public event EventHandler<ScentPolygonUpdateEventArgs>? PolygonsUpdated;
    public event EventHandler<ScentPolygonStatusEventArgs>? StatusUpdate;
    public event EventHandler<ForestCoverageEventArgs>? ForestCoverageUpdated;

    public ScentPolygonService(
        IRoverDataReader dataReader,
        ScentPolygonConfiguration? configuration = null,
      int pollIntervalMs = 1000,
      string? forestGeoPackagePath = null)
    {
      _dataReader = dataReader ?? throw new ArgumentNullException(nameof(dataReader));
  _configuration = configuration ?? new ScentPolygonConfiguration();
        _polygons = new ConcurrentDictionary<int, ScentPolygonResult>();
        _roverProcessors = new ConcurrentDictionary<Guid, RoverPolygonProcessor>();
      _pollTimer = new Timer(PollForNewMeasurements, null, Timeout.Infinite, pollIntervalMs);
    _forestGeoPackagePath = forestGeoPackagePath;
    }

    // --- Public state ---
    public int Count => _polygons.Count;
    public ScentPolygonResult? LatestPolygon => _latestPolygon;
    public int UnifiedVersion => _unifiedVersion;
    public int ActiveRoverCount => _roverProcessors.Count;

    public List<ScentPolygonResult> GetAllPolygons() =>
        _polygons.Values.OrderBy(p => p.Sequence).ToList(); // LINQ: order for deterministic output

    /// <summary>
    /// Gets the unified polygon for a specific rover
    /// </summary>
    public RoverUnifiedPolygon? GetRoverUnifiedPolygon(Guid roverId)
    {
 return _roverProcessors.TryGetValue(roverId, out var processor) 
            ? processor.GetCurrentUnified() 
        : null;
}

    /// <summary>
    /// Gets all rover-specific unified polygons
    /// </summary>
    public List<RoverUnifiedPolygon> GetAllRoverUnifiedPolygons()
{
        return _roverProcessors.Values
            .Select(p => p.GetCurrentUnified())
            .Where(p => p != null)
            .ToList()!;
    }

    /// <summary>
    /// Non-cached unified polygon (calls calculator each time)
    /// </summary>
    public UnifiedScentPolygon? GetUnifiedScentPolygon()
 {
        var all = GetAllPolygons();
        if (!all.Any()) return null;
    return ScentPolygonCalculator.CreateUnifiedPolygon(all);
    }

    /// <summary>
    /// Cached version - recomputes only when new polygons were added.
    /// Uses the optimized approach: combines per-rover unified polygons instead of all individual polygons.
    /// </summary>
    public UnifiedScentPolygon? GetUnifiedScentPolygonCached()
    {
        if (!_unifiedDirty && _unifiedCache != null)
    return _unifiedCache;

        lock (_unifiedLock) // lock to avoid concurrent regeneration
        {
          if (!_unifiedDirty && _unifiedCache != null)
              return _unifiedCache;

            var roverPolygons = GetAllRoverUnifiedPolygons();
      if (!roverPolygons.Any()) 
            { 
          _unifiedCache = null; 
        _unifiedDirty = false; 
    return null; 
          }

            // Optimized: combine rover-specific unified polygons instead of all individual polygons
            _unifiedCache = ScentPolygonCalculator.CreateUnifiedFromRoverPolygons(roverPolygons);
       _unifiedDirty = false;
            return _unifiedCache;
  }
    }

    /// <summary>
    /// Calculates forest and intersection areas (m²) between the unified scent polygon and a forest polygon.
    /// Minimal checks to keep it easy to follow.
    /// </summary>
    public async Task<(int searchedAreaM2, int forestAreaM2)> GetForestIntersectionAreasAsync(
        string forestGeoPackagePath,
        string layerName = "riverheadforest")
    {
   var unified = GetUnifiedScentPolygonCached();
        if (unified == null || !unified.IsValid) return (0, 0);
        if (!File.Exists(forestGeoPackagePath)) return (0, 0);

        using var gp = await GeoPackage.OpenAsync(forestGeoPackagePath, 4326);
        var layer = await gp.EnsureLayerAsync(layerName, new Dictionary<string, string>(), 4326);

        await foreach (var feature in layer.ReadFeaturesAsync(new ReadOptions(IncludeGeometry: true, Limit: 1)))
  {
      if (feature.Geometry is Polygon forest)
       {
                // Convert degrees² to m² using latitude scale
            var avgLat = forest.Centroid.Y;
    var metersPerDegLat = 111_320.0;
           var metersPerDegLon = 111_320.0 * Math.Cos(avgLat * Math.PI / 180.0);

    var forestArea = forest.Area * metersPerDegLat * metersPerDegLon;
                //UPPGIFT 1 BYT NEDANSTÅENDE RADER
         var searchedArea = unified.Polygon.Area * metersPerDegLat * metersPerDegLon;
       var searchedArea2 = forest.Intersection(unified.Polygon).Area * metersPerDegLat * metersPerDegLon;

       return ((int)Math.Max(0, Math.Round(searchedArea)), (int)Math.Max(0, Math.Round(forestArea)));
          }
        }
        return (0, 0);
    }

    /// <summary>
    /// Generates a scent polygon for a specific measurement.
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

        return new ScentPolygonResult
 {
  Polygon = polygon,
          RoverId = measurement.RoverId,
            RoverName = measurement.RoverName,
        SessionId = measurement.SessionId,
   Sequence = measurement.Sequence,
            RecordedAt = measurement.RecordedAt,
     Latitude = measurement.Latitude,
            Longitude = measurement.Longitude,
      WindDirectionDeg = measurement.WindDirectionDeg,
 WindSpeedMps = measurement.WindSpeedMps,
    ScentAreaM2 = scentArea
        };
    }

    /// <summary>
    /// Gets or creates a rover processor for the given rover ID
    /// </summary>
    private RoverPolygonProcessor GetOrCreateRoverProcessor(Guid roverId, string roverName)
    {
        return _roverProcessors.GetOrAdd(roverId, _ =>
        {
            var processor = new RoverPolygonProcessor(roverId, roverName);
 
     // Subscribe to updates from this rover's processor
       processor.UnifiedPolygonUpdated += (sender, roverUnified) =>
    {
    // Mark unified cache as dirty when any rover's polygon updates
           lock (_unifiedLock) 
        { 
            _unifiedDirty = true; 
  _unifiedCache = null; 
                    _unifiedVersion++; 
     }
            };
  
            return processor;
        });
    }

    /// <summary>
    /// Notifies subscribers about forest coverage updates (async helper for fire-and-forget)
    /// </summary>
    private async Task NotifyForestCoverageAsync()
    {
var (searchedAreaM2, forestAreaM2) = await GetForestIntersectionAreasAsync(_forestGeoPackagePath!);
        ForestCoverageUpdated?.Invoke(this, new ForestCoverageEventArgs { SearchedAreaM2 = searchedAreaM2, ForestAreaM2 = forestAreaM2 });
    }

 // --- HostedService lifecycle ---
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _dataReader.InitializeAsync(cancellationToken); // async DB/file init
        await LoadInitialPolygons();
        _pollTimer.Change(0, 1000); // start polling every second
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
    
   // Stop all rover processors
        foreach (var processor in _roverProcessors.Values)
        {
        processor.Dispose();
        }
        _roverProcessors.Clear();
        
        return Task.CompletedTask;
    }

    // Initial load is a single pass to keep it simple
    private async Task LoadInitialPolygons()
    {
        var measurements = await _dataReader.GetAllMeasurementsAsync();
        foreach (var m in measurements)
      {
    var polygon = GeneratePolygonForMeasurement(m);
            if (polygon.IsValid && _polygons.TryAdd(polygon.Sequence, polygon))
          {
  // Add to rover-specific processor
         var processor = GetOrCreateRoverProcessor(polygon.RoverId, polygon.RoverName);
        processor.TryAddPolygon(polygon);

           if (_latestPolygon == null || polygon.Sequence > _latestPolygon.Sequence)
 {
          _latestPolygon = polygon;
             _lastKnownSequence = polygon.Sequence;
    }
 // Track newest timestamp seen
 if (polygon.RecordedAt > _lastKnownTimestampUtc)
 {
 _lastKnownTimestampUtc = polygon.RecordedAt;
 }
   }
        }
    lock (_unifiedLock) { _unifiedDirty = true; _unifiedCache = null; _unifiedVersion++; }
    }

    // Simple polling loop (Timer callback). Avoids heavy logging for clarity.
    private async void PollForNewMeasurements(object? state)
    {
        if (_disposed) return;
        try
        {
 // Timestamp-based incremental fetch to support late-joining/restarted rovers
 var newMeasurements = await _dataReader.GetNewMeasurementsSinceAsync(_lastKnownTimestampUtc);
 if (!newMeasurements.Any())
 {
 // periodic lightweight status event (every ~10s)
 if (DateTime.UtcNow.Second %10 ==0)
 {
 StatusUpdate?.Invoke(this, new ScentPolygonStatusEventArgs 
 { 
 TotalPolygonCount = _polygons.Count, 
 LatestPolygon = _latestPolygon, 
 DataSource = _dataReader.GetType().Name,
 ActiveRoverCount = _roverProcessors.Count
 });
 }
 return;
 }

 var newPolygons = new List<ScentPolygonResult>();
 var affectedRoverIds = new HashSet<Guid>();
 
 foreach (var m in newMeasurements)
 {
 var polygon = GeneratePolygonForMeasurement(m);
 if (polygon.IsValid && _polygons.TryAdd(polygon.Sequence, polygon))
 {
 newPolygons.Add(polygon);
 affectedRoverIds.Add(polygon.RoverId);
 
 // Add to rover-specific processor
 var processor = GetOrCreateRoverProcessor(polygon.RoverId, polygon.RoverName);
 processor.TryAddPolygon(polygon);
 
 if (_latestPolygon == null || polygon.RecordedAt > _latestPolygon.RecordedAt)
 {
 _latestPolygon = polygon;
 }
 // Advance watermark by latest timestamp observed
 if (polygon.RecordedAt > _lastKnownTimestampUtc)
 {
 _lastKnownTimestampUtc = polygon.RecordedAt;
 }
 // Keep legacy sequence too (not used for polling anymore)
 if (polygon.Sequence > _lastKnownSequence)
 {
 _lastKnownSequence = polygon.Sequence;
 }
 }
 }

 if (newPolygons.Any())
 {
 lock (_unifiedLock) { _unifiedDirty = true; _unifiedCache = null; _unifiedVersion++; }
 PolygonsUpdated?.Invoke(this, new ScentPolygonUpdateEventArgs 
 { 
 NewPolygons = newPolygons, 
 TotalPolygonCount = _polygons.Count, 
 LatestPolygon = _latestPolygon,
 AffectedRoverIds = affectedRoverIds.ToList()
 });
 
 // Fire forest coverage event if forest path is configured (fire-and-forget)
 if (!string.IsNullOrEmpty(_forestGeoPackagePath))
 {
 _ = NotifyForestCoverageAsync();
 }
 }

 // small periodic status
 if (DateTime.UtcNow.Second %10 ==0)
 {
 StatusUpdate?.Invoke(this, new ScentPolygonStatusEventArgs 
 { 
 TotalPolygonCount = _polygons.Count, 
 LatestPolygon = _latestPolygon, 
 DataSource = _dataReader.GetType().Name,
 ActiveRoverCount = _roverProcessors.Count
 });
 }
 }
 catch
 {
 // Keep silent in learning setup; consumers see state through events
 }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Dispose();
        
        // Dispose all rover processors
        foreach (var processor in _roverProcessors.Values)
   {
        processor.Dispose();
        }
        _roverProcessors.Clear();
   
        _dataReader.Dispose();
    }
}