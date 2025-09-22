using System.Collections.Concurrent;

namespace ReadRoverDBStub;

/// <summary>
/// Manages a collection of rover measurements and monitors for updates
/// </summary>
public class RoverDataMonitor : IDisposable
{
    private readonly IRoverDataReader _dataReader;
    private readonly ConcurrentDictionary<int, RoverMeasurement> _measurements;
    private readonly Timer _pollTimer;
    private readonly Timer _displayTimer;
    private bool _disposed;
    private int _lastKnownSequence = -1;
    private RoverMeasurement? _latestMeasurement;

    public RoverDataMonitor(IRoverDataReader dataReader, int pollIntervalMs = 1000, int displayIntervalMs = 2000)
    {
        _dataReader = dataReader ?? throw new ArgumentNullException(nameof(dataReader));
        _measurements = new ConcurrentDictionary<int, RoverMeasurement>();
        
        // Create timers but don't start them yet
        _pollTimer = new Timer(PollForNewData, null, Timeout.Infinite, pollIntervalMs);
        _displayTimer = new Timer(DisplayStatus, null, Timeout.Infinite, displayIntervalMs);
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
        Console.WriteLine("Initializing rover data monitor...");
        
        try
        {
            await _dataReader.InitializeAsync();
            
            // Load initial data
            await LoadInitialData();
            
            // Start polling for new data
            _pollTimer.Change(1000, 1000); // Poll every second
            _displayTimer.Change(2000, 2000); // Display every 2 seconds
            
            Console.WriteLine("Rover data monitor started successfully!");
            Console.WriteLine($"Initial data loaded: {Count} measurements");
            
            if (_latestMeasurement != null)
            {
                Console.WriteLine($"Latest measurement: Seq={_latestMeasurement.Sequence}, " +
                                $"Location=({_latestMeasurement.Latitude:F6}, {_latestMeasurement.Longitude:F6}), " +
                                $"Time={_latestMeasurement.RecordedAt:HH:mm:ss}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start rover data monitor: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Stops the monitor
    /// </summary>
    public void Stop()
    {
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _displayTimer.Change(Timeout.Infinite, Timeout.Infinite);
        Console.WriteLine("Rover data monitor stopped.");
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
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading initial data: {ex.Message}");
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
                    Console.WriteLine($"[UPDATE] Added {addedCount} new measurements. Total: {Count}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error polling for new data: {ex.Message}");
        }
    }

    private void DisplayStatus(object? state)
    {
        try
        {
            if (_latestMeasurement != null)
            {
                Console.WriteLine($"[STATUS] Total measurements: {Count}, " +
                                $"Latest: Seq={_latestMeasurement.Sequence}, " +
                                $"Location=({_latestMeasurement.Latitude:F6}, {_latestMeasurement.Longitude:F6}), " +
                                $"Wind={_latestMeasurement.WindSpeedMps:F1}m/s @ {_latestMeasurement.WindDirectionDeg}deg, " +
                                $"Time={_latestMeasurement.RecordedAt:HH:mm:ss}");
            }
            else
            {
                Console.WriteLine($"[STATUS] Total measurements: {Count}, No data available yet");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error displaying status: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _pollTimer?.Dispose();
            _displayTimer?.Dispose();
            _dataReader?.Dispose();
            _disposed = true;
        }
    }
}