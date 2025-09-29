using System.Collections.Concurrent;

namespace ReadRoverDBStubLibrary;

/// <summary>
/// Event arguments for rover data updates
/// </summary>
public class RoverDataUpdateEventArgs : EventArgs
{
    public int NewMeasurementsCount { get; init; }
    public int TotalMeasurementsCount { get; init; }
    public RoverMeasurement? LatestMeasurement { get; init; }
}

/// <summary>
/// Event arguments for rover data monitor status
/// </summary>
public class RoverDataStatusEventArgs : EventArgs
{
    public int TotalMeasurementsCount { get; init; }
    public RoverMeasurement? LatestMeasurement { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Manages a collection of rover measurements and monitors for updates (silent library version)
/// </summary>
public class RoverDataMonitor : IDisposable
{
    private readonly IRoverDataReader _dataReader;
    private readonly ConcurrentDictionary<int, RoverMeasurement> _measurements;
    private readonly Timer _pollTimer;
    private readonly Timer _statusTimer;
    private bool _disposed;
    private int _lastKnownSequence = -1;
    private RoverMeasurement? _latestMeasurement;

    /// <summary>
    /// Event fired when new data is received
    /// </summary>
    public event EventHandler<RoverDataUpdateEventArgs>? DataUpdated;
    
    /// <summary>
    /// Event fired for periodic status updates
    /// </summary>
    public event EventHandler<RoverDataStatusEventArgs>? StatusUpdate;

    public RoverDataMonitor(IRoverDataReader dataReader, int pollIntervalMs = 1000, int statusIntervalMs = 2000)
    {
        _dataReader = dataReader ?? throw new ArgumentNullException(nameof(dataReader));
        _measurements = new ConcurrentDictionary<int, RoverMeasurement>();
        
        // Create timers but don't start them yet
        _pollTimer = new Timer(PollForNewData, null, Timeout.Infinite, pollIntervalMs);
        _statusTimer = new Timer(FireStatusUpdate, null, Timeout.Infinite, statusIntervalMs);
    }

    /// <summary>
    /// Gets the current count of measurements in the collection
    /// </summary>
    public int Count => _measurements.Count;

    /// <summary>
    /// Gets the latest measurement
    /// </summary>
    public RoverMeasurement? LatestMeasurement => _latestMeasurement;

    /// <summary>
    /// Gets all measurements as a list
    /// </summary>
    public List<RoverMeasurement> GetAllMeasurements()
    {
        return _measurements.Values.OrderBy(m => m.Sequence).ToList();
    }

    /// <summary>
    /// Initializes the monitor and starts polling
    /// </summary>
    public async Task StartAsync()
    {
        await _dataReader.InitializeAsync();
        
        // Load initial data
        await LoadInitialData();
        
        // Start polling for new data
        _pollTimer.Change(1000, 1000); // Poll every second
        _statusTimer.Change(2000, 2000); // Status every 2 seconds
    }

    /// <summary>
    /// Stops the monitor
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
                    // Fire event for new data
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