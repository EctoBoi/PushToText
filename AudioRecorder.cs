using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PushToText;

public sealed class AudioRecorder : IDisposable
{
    private readonly object _sync = new();
    private WaveInEvent? _waveIn;
    private System.IO.MemoryStream? _recordingStream;
    private WaveFileWriter? _waveWriter;
    private TaskCompletionSource<byte[]>? _stopTcs;

    public bool IsRecording { get; private set; }
    public int SelectedDeviceNumber { get; set; } = -1;

    public static IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var devices = new List<AudioInputDevice>();
        var endpointNames = GetCaptureEndpointNames();

        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var capabilities = WaveInEvent.GetCapabilities(i);
            var fallbackName = capabilities.ProductName;
            var displayName = i < endpointNames.Count && !string.IsNullOrWhiteSpace(endpointNames[i])
                ? endpointNames[i]
                : fallbackName;

            devices.Add(new AudioInputDevice(i, displayName));
        }

        return devices;
    }

    public void StartRecording()
    {
        lock (_sync)
        {
            if (IsRecording)
            {
                return;
            }

            if (WaveInEvent.DeviceCount < 1)
            {
                throw new InvalidOperationException("No microphone is available.");
            }

            var deviceNumber = SelectedDeviceNumber;
            if (deviceNumber < 0 || deviceNumber >= WaveInEvent.DeviceCount)
            {
                deviceNumber = 0;
            }

            _recordingStream = new System.IO.MemoryStream();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(16000, 16, 1)
            };

            _waveWriter = new WaveFileWriter(_recordingStream, _waveIn.WaveFormat);
            _stopTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _waveIn.StartRecording();
            IsRecording = true;
        }
    }

    public async Task<byte[]> StopRecordingAsync()
    {
        Task<byte[]> stopTask;

        lock (_sync)
        {
            if (!IsRecording)
            {
                return Array.Empty<byte>();
            }

            stopTask = _stopTcs?.Task ?? Task.FromResult(Array.Empty<byte>());
            _waveIn?.StopRecording();
        }

        return await stopTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _waveIn?.Dispose();
            _waveWriter?.Dispose();
            _recordingStream?.Dispose();

            _waveIn = null;
            _waveWriter = null;
            _recordingStream = null;
            IsRecording = false;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_sync)
        {
            _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
            _waveWriter?.Flush();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            IsRecording = false;

            _waveIn!.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;

            _waveIn.Dispose();
            _waveIn = null;

            _waveWriter?.Dispose();

            var bytes = _recordingStream?.ToArray() ?? Array.Empty<byte>();
            _recordingStream?.Dispose();

            _waveWriter = null;
            _recordingStream = null;

            if (e.Exception is not null)
            {
                _stopTcs?.TrySetException(e.Exception);
            }
            else
            {
                _stopTcs?.TrySetResult(bytes);
            }

            _stopTcs = null;
        }
    }

    private static List<string> GetCaptureEndpointNames()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(device => device.FriendlyName)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}

public sealed record AudioInputDevice(int DeviceNumber, string Name)
{
    public override string ToString()
    {
        return Name;
    }
}
