/*
 The functionallity in this file is:
 - Provide a tiny console progress monitor with a Ctrl+P toggle.
 - Keep UI concerns out of the simulator loop; Blazor can consume progress differently.
 - Use a background task to read keys without blocking the simulation.
*/

namespace RoverSimulator;

/// <summary>
/// Shared progress state container for the simulator.
/// </summary>
public static class ProgressState
{
    public static volatile bool Enabled = true;
}

/// <summary>
/// Handles progress reporting and user input monitoring.
/// </summary>
public class ProgressMonitor : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _keyboardMonitorTask;
    private bool _disposed;

    public ProgressMonitor(CancellationTokenSource cancellationTokenSource)
    {
        _cancellationTokenSource = cancellationTokenSource;

        // Start keyboard monitoring task
        _keyboardMonitorTask = Task.Run(MonitorKeyboardInputAsync);
    }

    /// <summary>
    /// Reports progress for rover measurements (prints every 10 rows).
    /// </summary>
    public static void ReportProgress(int sequenceNumber, Guid sessionId, double latitude, double longitude, string windInfo)
    {
        if (ProgressState.Enabled && sequenceNumber % 10 == 0)
        {
            Console.WriteLine($"[progress] {sequenceNumber} rows inserted (session {sessionId}) last=({latitude:F5},{longitude:F5}) wind {windInfo}");
        }
    }

    private async Task MonitorKeyboardInputAsync()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.P && (key.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    ProgressState.Enabled = !ProgressState.Enabled;
                    Console.WriteLine($"[progress] {(ProgressState.Enabled ? "ON" : "OFF")}");
                }
            }
            await Task.Delay(50, CancellationToken.None);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                if (_keyboardMonitorTask != null && !_keyboardMonitorTask.IsCompleted)
                {
                    _keyboardMonitorTask.Wait(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
            finally
            {
                _keyboardMonitorTask?.Dispose();
                _disposed = true;
            }
        }
    }
}