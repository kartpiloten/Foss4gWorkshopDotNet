/*
 The functionality in this file is:
 - Per-rover polygon processor that runs in its own thread
 - Incrementally updates a single unified polygon for each rover
 - Thread-safe operations with minimal locking
 - Efficient: adds new polygons to existing unified polygon without reprocessing all polygons
 - Performance: bounded queue, batched unions, and geometry simplification to avoid CPU spikes
*/

using NetTopologySuite.Geometries; // NTS geometry types and operations
using System.Collections.Concurrent; // Thread-safe collections
using System.Threading.Channels; // Channel-based async producer-consumer pattern
using NetTopologySuite.Operation.Union; // UnaryUnionOp for efficient batch union
using NetTopologySuite.Simplify; // TopologyPreservingSimplifier for vertex reduction

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

    // Tunables
    private const int QueueCapacity = 512; // prevent unbounded memory growth
    private const int BatchSize = 10; // union N polygons at a time
    private const int MaxVerticesBeforeSimplify = 6000; // simplify if geometry gets too detailed
    private const double SimplifyToleranceMeters = 1.5; // ~1.5 m tolerance

    // Event raised when the rover's unified polygon is updated
    public event EventHandler<RoverUnifiedPolygon>? UnifiedPolygonUpdated;

    public RoverPolygonProcessor(Guid roverId, string roverName)
    {
        _roverId = roverId;
        _roverName = roverName;
        _polygonChannel = Channel.CreateBounded<ScentPolygonResult>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest // coalesce if producer outruns consumer
        });
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
        var batch = new List<ScentPolygonResult>(BatchSize);
        try
        {
            await foreach (var polygon in _polygonChannel.Reader.ReadAllAsync(_cts.Token))
            {
                if (!polygon.IsValid) continue;
                batch.Add(polygon);

                if (batch.Count >= BatchSize)
                {
                    ApplyBatch(batch);
                    batch.Clear();
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
        finally
        {
            // flush any remaining work
            if (batch.Count > 0)
            {
                try { ApplyBatch(batch); } catch { }
                batch.Clear();
            }
        }
    }

    private void ApplyBatch(List<ScentPolygonResult> batch)
    {
        if (batch.Count == 0) return;

        // Snapshot base geometry to avoid holding the lock during expensive union
        Polygon? basePolygon = null;
        double baseAvgLat = 0;
        int existingCount = 0;
        DateTimeOffset earliest = batch.Min(b => b.RecordedAt);
        DateTimeOffset latest = batch.Max(b => b.RecordedAt);
        int latestSeq = batch.Max(b => b.Sequence);

        var current = _currentUnified;
        if (current != null)
        {
            lock (current.Lock)
            {
                basePolygon = current.UnifiedPolygon;
                baseAvgLat = current.AverageLatitude;
                existingCount = current.PolygonCount;
                // keep earliest from existing
                earliest = earliest < current.EarliestMeasurement ? earliest : current.EarliestMeasurement;
            }
        }

        // Pre-simplify incoming polygons slightly to reduce union cost
        var geometries = new List<Geometry>();
        if (basePolygon != null) geometries.Add(basePolygon);
        foreach (var r in batch)
        {
            var g = r.Polygon;
            // tiny simplification to limit vertex growth (topology-preserving)
            var tolDeg = MetersToDegrees(r.Latitude, SimplifyToleranceMeters);
            var simplified = TopologyPreservingSimplifier.Simplify(g, tolDeg) as Geometry ?? g;
            simplified.SRID = 4326;
            geometries.Add(simplified);
        }

        // Efficient union over collection
        var unionGeom = UnaryUnionOp.Union(geometries);

        // Normalize to single polygon (choose largest if Multi)
        Polygon finalPolygon = unionGeom switch
        {
            Polygon p => p,
            MultiPolygon mp => mp.Geometries.OfType<Polygon>().OrderByDescending(p => p.Area).First(),
            _ => basePolygon ?? batch.First().Polygon
        };
        finalPolygon.SRID = 4326;

        // Simplify final polygon if overly complex
        var avgLatForTol = batch.Average(b => b.Latitude);
        var tol = MetersToDegrees(avgLatForTol, SimplifyToleranceMeters);
        if (finalPolygon.NumPoints > MaxVerticesBeforeSimplify)
        {
            var simplified = TopologyPreservingSimplifier.Simplify(finalPolygon, tol) as Polygon;
            if (simplified != null && simplified.IsValid)
            {
                finalPolygon = simplified;
                finalPolygon.SRID = 4326;
            }
        }

        // Compute updated stats
        int newCountTotal = existingCount + batch.Count;
        double newAvgLat = (baseAvgLat * existingCount + batch.Sum(b => b.Latitude)) / Math.Max(1, newCountTotal);
        var newAreaM2 = ScentPolygonCalculator.CalculateScentAreaM2(finalPolygon, newAvgLat);

        // Commit update
        if (_currentUnified == null)
        {
            _currentUnified = new RoverUnifiedPolygon
            {
                RoverId = _roverId,
                RoverName = _roverName,
                UnifiedPolygon = finalPolygon,
                PolygonCount = newCountTotal,
                TotalAreaM2 = newAreaM2,
                LatestSequence = latestSeq,
                EarliestMeasurement = earliest,
                LatestMeasurement = latest,
                AverageLatitude = newAvgLat,
                Version = 1
            };
        }
        else
        {
            lock (_currentUnified.Lock)
            {
                _currentUnified.UnifiedPolygon = finalPolygon;
                _currentUnified.PolygonCount = newCountTotal;
                _currentUnified.TotalAreaM2 = newAreaM2;
                _currentUnified.LatestSequence = Math.Max(_currentUnified.LatestSequence, latestSeq);
                _currentUnified.LatestMeasurement = latest > _currentUnified.LatestMeasurement ? latest : _currentUnified.LatestMeasurement;
                _currentUnified.EarliestMeasurement = earliest;
                _currentUnified.AverageLatitude = newAvgLat;
                _currentUnified.Version++;
            }
        }

        // Notify listeners
        try { UnifiedPolygonUpdated?.Invoke(this, GetCurrentUnified()!); } catch { }
    }

    private static double MetersToDegrees(double latitude, double meters)
    {
        const double metersPerDegLat = 111_320.0;
        // longitudes shrink with cos(lat)
        double metersPerDegLon = 111_320.0 * Math.Cos(latitude * Math.PI / 180.0);
        // use latitude scale for a conservative tolerance
        return meters / metersPerDegLat;
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
