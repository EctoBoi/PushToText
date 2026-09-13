using System.IO;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

namespace PushToText;

public sealed class TranscriptionService : IAsyncDisposable
{
    private readonly string _modelPath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private WhisperFactory? _factory;

    public TranscriptionService()
    {
        var modelFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PushToText", "model");
        System.IO.Directory.CreateDirectory(modelFolder);
        _modelPath = System.IO.Path.Combine(modelFolder, "ggml-base.en.bin");
    }

    public async Task InitializeAsync()
    {
        if (!System.IO.File.Exists(_modelPath))
        {
            await using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.BaseEn);
            await using var fileStream = System.IO.File.Create(_modelPath);
            await modelStream.CopyToAsync(fileStream);
        }

        _factory = WhisperFactory.FromPath(_modelPath);
    }

    public async Task<string> TranscribeAsync(byte[] wavAudioBytes)
    {
        if (_factory is null)
        {
            throw new InvalidOperationException("Transcription model is not loaded.");
        }

        if (wavAudioBytes.Length == 0)
        {
            return string.Empty;
        }

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var audioStream = new System.IO.MemoryStream(wavAudioBytes);
            using var processor = _factory.CreateBuilder()
                .WithLanguage("en")
                .Build();

            var output = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(audioStream))
            {
                output.Append(segment.Text);
            }

            return output.ToString().Trim();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        _semaphore.Dispose();
        return ValueTask.CompletedTask;
    }
}
