/*
 The functionality in this file is:
 - Per-rover polygon processor that runs in its own thread
 - Incrementally updates a single unified polygon for each rover
 - Thread-safe operations with minimal locking
 - Efficient: adds new polygons to existing unified polygon without reprocessing all polygons
*/

using NetTopologySuite.Geometries; // NTS geometry types and operations
using System.Collections.Concurrent; // Thread-safe collections
using System.Threading.Channels; // Channel-based async producer-consumer pattern

namespace ScentPolygonLibrary;

/// <summary>
/// Processes scent polygons for a single rover in a dedicated thread
/// </summary>
public class RoverPolygonProcessor : IDisposable
{
    private readonly Guid _roverId;
    private readonly string _roverName;
    private readonly Channel<ScentPolygonResult> _polygonChannel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processingTask;
    private RoverUnifiedPolygon? _currentUnified;
    private bool _disposed;
    private readonly GeometryFactory _geometryFactory;

    // Event raised when the rover's unified polygon is updated
    public event EventHandler<RoverUnifiedPolygon>? UnifiedPolygonUpdated;

    public RoverPolygonProcessor(Guid roverId, string roverName)
    {
        _roverId = roverId;
        _roverName = roverName;
        _polygonChannel = Channel.CreateUnbounded<ScentPolygonResult>();
      _cts = new CancellationTokenSource();
    _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        
    // Start the processing task (runs in thread pool)
_processingTask = Task.Run(ProcessPolygonsAsync, _cts.Token);
    }

    /// <summary>
    /// Gets the current unified polygon for this rover (thread-safe)
    /// </summary>
public RoverUnifiedPolygon? GetCurrentUnified()
    {
        var current = _currentUnified;
 if (current == null) return null;

     lock (current.Lock)
        {
    // Return a snapshot
            return new RoverUnifiedPolygon
     {
   RoverId = current.RoverId,
        RoverName = current.RoverName,
       UnifiedPolygon = current.UnifiedPolygon,
     PolygonCount = current.PolygonCount,
     TotalAreaM2 = current.TotalAreaM2,
              LatestSequence = current.LatestSequence,
EarliestMeasurement = current.EarliestMeasurement,
           LatestMeasurement = current.LatestMeasurement,
  AverageLatitude = current.AverageLatitude,
     Version = current.Version
            };
        }
    }

    /// <summary>
    /// Adds a new polygon to the processing queue (non-blocking)
    /// </summary>
    public bool TryAddPolygon(ScentPolygonResult polygon)
    {
      if (_disposed || polygon.RoverId != _roverId)
            return false;

  return _polygonChannel.Writer.TryWrite(polygon);
}

    /// <summary>
    /// Background processing loop that incrementally updates the unified polygon
    /// </summary>
    private async Task ProcessPolygonsAsync()
    {
        try
        {
    await foreach (var polygon in _polygonChannel.Reader.ReadAllAsync(_cts.Token))
          {
   if (!polygon.IsValid) continue;

          if (_currentUnified == null)
     {
 // First polygon for this rover - initialize
               _currentUnified = new RoverUnifiedPolygon
         {
  RoverId = _roverId,
          RoverName = _roverName,
      UnifiedPolygon = polygon.Polygon,
    PolygonCount = 1,
          TotalAreaM2 = polygon.ScentAreaM2,
    LatestSequence = polygon.Sequence,
    EarliestMeasurement = polygon.RecordedAt,
          LatestMeasurement = polygon.RecordedAt,
    AverageLatitude = polygon.Latitude,
     Version = 1
    };
      }
    else
    {
      // Incremental update: union the new polygon with the existing unified polygon
      lock (_currentUnified.Lock)
          {
          try
         {
 var newUnion = _currentUnified.UnifiedPolygon.Union(polygon.Polygon);
    
             // Handle different result types from union
        Polygon finalPolygon;
    if (newUnion is Polygon singlePolygon)
   {
          finalPolygon = singlePolygon;
}
        else if (newUnion is MultiPolygon multiPolygon)
   {
      // Take the largest polygon
          finalPolygon = multiPolygon.Geometries
             .OfType<Polygon>()
       .OrderByDescending(p => p.Area)
   .First();
     }
        else
       {
         // Fallback: keep existing polygon
     finalPolygon = _currentUnified.UnifiedPolygon;
      }

         finalPolygon.SRID = 4326;

     // Validate and fix if necessary
       if (!finalPolygon.IsValid)
             {
          var buffered = finalPolygon.Buffer(0.0);
          if (buffered is Polygon fixedPolygon && fixedPolygon.IsValid)
       {
       finalPolygon = fixedPolygon;
             finalPolygon.SRID = 4326;
       }
    else
      {
              // Keep existing polygon if fix fails
             finalPolygon = _currentUnified.UnifiedPolygon;
         }
         }

  // Update statistics
                 var newCount = _currentUnified.PolygonCount + 1;
         var newAvgLat = (_currentUnified.AverageLatitude * _currentUnified.PolygonCount + polygon.Latitude) / newCount;
        var newAreaM2 = ScentPolygonCalculator.CalculateScentAreaM2(finalPolygon, newAvgLat);

                  _currentUnified.UnifiedPolygon = finalPolygon;
          _currentUnified.PolygonCount = newCount;
     _currentUnified.TotalAreaM2 = newAreaM2;
                 _currentUnified.LatestSequence = Math.Max(_currentUnified.LatestSequence, polygon.Sequence);
         _currentUnified.LatestMeasurement = polygon.RecordedAt > _currentUnified.LatestMeasurement 
   ? polygon.RecordedAt 
      : _currentUnified.LatestMeasurement;
           _currentUnified.AverageLatitude = newAvgLat;
     _currentUnified.Version++;
  }
         catch (Exception)
               {
  // Keep existing polygon if union fails (silent per workshop guidelines)
continue;
   }
       }
    }

     // Notify listeners (fire-and-forget)
    try
              {
    UnifiedPolygonUpdated?.Invoke(this, GetCurrentUnified()!);
}
 catch
                {
     // Ignore event handler exceptions
     }
   }
        }
        catch (OperationCanceledException)
        {
    // Normal shutdown
        }
        catch
      {
  // Keep silent per workshop guidelines
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

      try
  {
        _cts.Cancel();
            _polygonChannel.Writer.Complete();
       _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Intentionally silent
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
