using System.IO;
using System.Text.Json;

namespace PushToText;

public enum RecordingMode
{
    Push,
    Toggle
}

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}

public sealed class HotkeyBinding
{
    public uint VirtualKey { get; set; }
    public HotkeyModifiers Modifiers { get; set; }

    public string KeyName
    {
        get => System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)VirtualKey).ToString();
        set
        {
            if (Enum.TryParse<System.Windows.Input.Key>(value, true, out var key))
            {
                VirtualKey = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            }
        }
    }

    public override string ToString()
    {
        var parts = new List<string>();

        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)VirtualKey).ToString());
        return string.Join("+", parts);
    }
}

public sealed class AppSettings
{
    public RecordingMode RecordingMode { get; set; } = RecordingMode.Push;
    public int InputDeviceNumber { get; set; } = -1;
    public bool AutoCopyAfterTranscription { get; set; } = false;

    public HotkeyBinding MicrophoneHotkey { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
        VirtualKey = 0x78
    };

    public HotkeyBinding CopyHotkey { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift,
        VirtualKey = 0x79
    };
}

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        var appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PushToText");
        System.IO.Directory.CreateDirectory(appData);
        _settingsPath = System.IO.Path.Combine(appData, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!System.IO.File.Exists(_settingsPath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            var json = System.IO.File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        System.IO.File.WriteAllText(_settingsPath, json);
    }
}
