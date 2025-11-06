/*
 The functionallity in this file is:
 - Centralize polling and in-memory caching of rover measurements for UI consumers (e.g., Blazor components).
 - Raise simple events when new data arrives and for periodic status, so UIs can react without tight coupling.
 - Keep logic minimal: async reads, thread-safe storage, and timers; no extra logging/config noise.
*/

using System.Collections.Concurrent;

namespace ReadRoverDBStubLibrary;

/// <summary>
/// Event arguments for rover data updates (POCO payload).
/// </summary>
public class RoverDataUpdateEventArgs : EventArgs
{
    public int NewMeasurementsCount { get; init; }
    public int TotalMeasurementsCount { get; init; }
    public RoverMeasurement? LatestMeasurement { get; init; }
}

/// <summary>
/// Event arguments for rover data monitor status (POCO payload).
/// </summary>
public class RoverDataStatusEventArgs : EventArgs
{
    public int TotalMeasurementsCount { get; init; }
    public RoverMeasurement? LatestMeasurement { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Manages a collection of rover measurements and monitors for updates (silent library version).
/// Notes:
/// - Uses ConcurrentDictionary for thread-safe updates from timer callbacks.
/// - System.Threading.Timer callbacks run on ThreadPool, not a UI thread (Blazor consumers should marshal to UI).
/// </summary>
public class RoverDataMonitor : IDisposable
{
    private readonly IRoverDataReader _dataReader;
    private readonly ConcurrentDictionary<int, RoverMeasurement> _measurements;
    private readonly Timer _pollTimer;   // Timer-based polling (non-UI thread)
    private readonly Timer _statusTimer; // Periodic status event
    private readonly int _pollIntervalMs;
    private readonly int _statusIntervalMs;
    private bool _disposed;
    private int _lastKnownSequence = -1;
    private RoverMeasurement? _latestMeasurement;

    /// <summary>
    /// Event fired when new data is received.
    /// Blazor note: invoke StateHasChanged via InvokeAsync on the component side.
    /// </summary>
    public event EventHandler<RoverDataUpdateEventArgs>? DataUpdated;

    /// <summary>
    /// Event fired for periodic status updates.
    /// </summary>
    public event EventHandler<RoverDataStatusEventArgs>? StatusUpdate;

    public RoverDataMonitor(IRoverDataReader dataReader, int pollIntervalMs = 1000, int statusIntervalMs = 2000)
    {
        _dataReader = dataReader ?? throw new ArgumentNullException(nameof(dataReader));
        _measurements = new ConcurrentDictionary<int, RoverMeasurement>(); // thread-safe map by Sequence
        _pollIntervalMs = pollIntervalMs;
        _statusIntervalMs = statusIntervalMs;

        // Create timers but don't start them yet; period is configured, dueTime will be set in StartAsync.
        _pollTimer = new Timer(PollForNewData, null, Timeout.Infinite, _pollIntervalMs);
        _statusTimer = new Timer(FireStatusUpdate, null, Timeout.Infinite, _statusIntervalMs);
    }

    /// <summary>
    /// Gets the current count of measurements in the collection.
    /// </summary>
    public int Count => _measurements.Count;

    /// <summary>
    /// Gets the latest measurement.
    /// </summary>
    public RoverMeasurement? LatestMeasurement => _latestMeasurement;

    /// <summary>
    /// Gets all measurements as a list.
    /// LINQ OrderBy for stable, ascending sequence order.
    /// </summary>
    public List<RoverMeasurement> GetAllMeasurements()
    {
        return _measurements.Values.OrderBy(m => m.Sequence).ToList();
    }

    /// <summary>
    /// Initializes the monitor and starts polling.
    /// Async/await avoids blocking; initial load populates cache.
    /// </summary>
    public async Task StartAsync()
    {
        await _dataReader.InitializeAsync();

        // Load initial data
        await LoadInitialData();

        // Start polling for new data and status with configured intervals
        _pollTimer.Change(_pollIntervalMs, _pollIntervalMs);
        _statusTimer.Change(_statusIntervalMs, _statusIntervalMs);
    }

    /// <summary>
    /// Stops the monitor (pauses timers).
    /// </summary>
    public void Stop()
    {
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _statusTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async Task LoadInitialData()
    {
        try
        {
            var allMeasurements = await _dataReader.GetAllMeasurementsAsync();

            foreach (var measurement in allMeasurements)
            {
                _measurements.TryAdd(measurement.Sequence, measurement);

                if (_latestMeasurement == null || measurement.Sequence > _latestMeasurement.Sequence)
                {
                    _latestMeasurement = measurement;
                    _lastKnownSequence = measurement.Sequence;
                }
            }
        }
        catch (Exception)
        {
            // Silent operation - errors are handled by the caller
        }
    }

    // Timer callback uses async void by design in this pattern; exceptions are caught internally.
    private async void PollForNewData(object? state)
    {
        try
        {
            var newMeasurements = await _dataReader.GetNewMeasurementsAsync(_lastKnownSequence);

            if (newMeasurements.Any())
            {
                var addedCount = 0;

                foreach (var measurement in newMeasurements)
                {
                    if (_measurements.TryAdd(measurement.Sequence, measurement))
                    {
                        addedCount++;

                        if (_latestMeasurement == null || measurement.Sequence > _latestMeasurement.Sequence)
                        {
                            _latestMeasurement = measurement;
                            _lastKnownSequence = measurement.Sequence;
                        }
                    }
                }

                if (addedCount > 0)
                {
                    // Fire event for new data (consumer is responsible for UI-thread marshalling)
                    DataUpdated?.Invoke(this, new RoverDataUpdateEventArgs
                    {
                        NewMeasurementsCount = addedCount,
                        TotalMeasurementsCount = Count,
                        LatestMeasurement = _latestMeasurement
                    });
                }
            }
        }
        catch (Exception)
        {
            // Silent operation - polling continues
        }
    }

    private void FireStatusUpdate(object? state)
    {
        try
        {
            StatusUpdate?.Invoke(this, new RoverDataStatusEventArgs
            {
                TotalMeasurementsCount = Count,
                LatestMeasurement = _latestMeasurement,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception)
        {
            // Silent operation
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _pollTimer?.Dispose();
            _statusTimer?.Dispose();
            _dataReader?.Dispose();
            _disposed = true;
        }
    }
}