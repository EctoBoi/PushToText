using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PushToText;

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;

    private static readonly string[] SelectableKeys =
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
    ];

    private readonly SettingsService _settingsService;
    private readonly AudioRecorder _audioRecorder;
    private readonly TranscriptionService _transcriptionService;

    private AppSettings _settings;
    private HotkeyManager? _hotkeyManager;
    private bool _isTranscribing;
    private bool _isInitializingUi;
    private Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _audioRecorder = new AudioRecorder { SelectedDeviceNumber = _settings.InputDeviceNumber };
        _transcriptionService = new TranscriptionService();

        InitializeComponent();
        InitializeTrayIcon();

        SourceInitialized += MainWindowOnSourceInitialized;
        Loaded += MainWindowOnLoaded;
        Closing += MainWindowOnClosing;
        Deactivated += MainWindowOnDeactivated;

        InitializeSettingsUi();
        UpdateHotkeyText();
        SetRecordingLight(false);
    }

    private async void MainWindowOnLoaded(object sender, RoutedEventArgs e)
    {
        SetStatus("Loading speech model...", Brushes.Goldenrod);
        try
        {
            await _transcriptionService.InitializeAsync();
            SetStatus("Ready", Brushes.ForestGreen);
            ErrorTextBlock.Text = string.Empty;
        }
        catch (Exception ex)
        {
            SetStatus("Speech model unavailable", Brushes.OrangeRed);
            ErrorTextBlock.Text = ex.Message;
        }
    }

    private void MainWindowOnSourceInitialized(object? sender, EventArgs e)
    {
        SetNoActivateStyle();

        _hotkeyManager = new HotkeyManager(this, _settings);
        _hotkeyManager.StartRecordingRequested += OnStartRecordingRequested;
        _hotkeyManager.StopRecordingRequested += OnStopRecordingRequested;
        _hotkeyManager.CopyRequested += OnCopyRequested;
        _hotkeyManager.Initialize();
    }

    private void MainWindowOnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _hotkeyManager?.Dispose();
        _audioRecorder.Dispose();
        _ = _transcriptionService.DisposeAsync();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "PushToText",
            Visible = false,
            Icon = LoadTrayIcon()
        };

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Restore", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => Close());
        _trayIcon.ContextMenuStrip = menu;
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var streamResource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tape.ico"));
        if (streamResource is null)
        {
            return Drawing.SystemIcons.Application;
        }

        using var stream = streamResource.Stream;
        return new Drawing.Icon(stream);
    }

    private void MinimizeToTrayButton_OnClick(object sender, RoutedEventArgs e)
    {
        MinimizeToTray();
    }

    private void MinimizeToTray()
    {
        if (_trayIcon is null)
        {
            return;
        }

        Hide();
        ShowInTaskbar = false;
        _trayIcon.Visible = true;
        _trayIcon.BalloonTipTitle = "PushToText";
        _trayIcon.BalloonTipText = "Running in the system tray.";
        _trayIcon.ShowBalloonTip(1200);
    }

    private void RestoreFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
            }

            ShowInTaskbar = true;
            Show();
            Activate();
            SetNoActivateStyle();
        });
    }

    private void InitializeSettingsUi()
    {
        _isInitializingUi = true;
        try
        {
            UpdateModeSelection();
            PopulateAudioInputSelection(_settings.InputDeviceNumber);
            AutoCopyCheckBox.IsChecked = _settings.AutoCopyAfterTranscription;
            MicKeyComboBox.ItemsSource = SelectableKeys;
            CopyKeyComboBox.ItemsSource = SelectableKeys;
            ApplyBindingToControls(_settings.MicrophoneHotkey, MicCtrlCheckBox, MicShiftCheckBox, MicAltCheckBox, MicWinCheckBox, MicKeyComboBox);
            ApplyBindingToControls(_settings.CopyHotkey, CopyCtrlCheckBox, CopyShiftCheckBox, CopyAltCheckBox, CopyWinCheckBox, CopyKeyComboBox);
        }
        finally
        {
            _isInitializingUi = false;
        }
    }

    private void PopulateAudioInputSelection(int? preferredDeviceNumber = null)
    {
        var devices = AudioRecorder.GetInputDevices();
        AudioInputComboBox.ItemsSource = devices;

        if (devices.Count == 0)
        {
            AudioInputComboBox.SelectedIndex = -1;
            _settings.InputDeviceNumber = -1;
            _audioRecorder.SelectedDeviceNumber = -1;
            ErrorTextBlock.Text = "No microphone devices detected.";
            return;
        }

        var targetDeviceNumber = preferredDeviceNumber ?? _settings.InputDeviceNumber;
        var selected = devices.FirstOrDefault(d => d.DeviceNumber == targetDeviceNumber) ?? devices[0];

        AudioInputComboBox.SelectedItem = selected;
        _settings.InputDeviceNumber = selected.DeviceNumber;
        _audioRecorder.SelectedDeviceNumber = selected.DeviceNumber;
        ErrorTextBlock.Text = string.Empty;
    }

    private void RefreshAudioDevicesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var currentDeviceNumber = (AudioInputComboBox.SelectedItem as AudioInputDevice)?.DeviceNumber ?? _settings.InputDeviceNumber;

        _isInitializingUi = true;
        try
        {
            PopulateAudioInputSelection(currentDeviceNumber);
        }
        finally
        {
            _isInitializingUi = false;
        }

        _settingsService.Save(_settings);
        SetStatus("Audio devices refreshed", Brushes.SteelBlue);
    }

    private void RecordingModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isInitializingUi)
        {
            return;
        }

        if (RecordingModeComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        _settings.RecordingMode = string.Equals(selectedItem.Tag?.ToString(), "Toggle", StringComparison.OrdinalIgnoreCase)
            ? RecordingMode.Toggle
            : RecordingMode.Push;

        SaveAndApplySettings("Mode updated", Brushes.SteelBlue);
    }

    private void AudioInputComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingUi)
        {
            return;
        }

        if (AudioInputComboBox.SelectedItem is not AudioInputDevice device)
        {
            return;
        }

        _settings.InputDeviceNumber = device.DeviceNumber;
        _audioRecorder.SelectedDeviceNumber = device.DeviceNumber;
        _settingsService.Save(_settings);
        SetStatus($"Audio input: {device.Name}", Brushes.SteelBlue);
    }

    private void AutoCopyCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializingUi)
        {
            return;
        }

        _settings.AutoCopyAfterTranscription = AutoCopyCheckBox.IsChecked == true;
        _settingsService.Save(_settings);
        SetStatus(_settings.AutoCopyAfterTranscription ? "Auto-copy enabled" : "Auto-copy disabled", Brushes.SteelBlue);
    }

    private void ApplyHotkeysButton_OnClick(object sender, RoutedEventArgs e)
    {
        var micHotkey = BuildBindingFromControls(MicCtrlCheckBox, MicShiftCheckBox, MicAltCheckBox, MicWinCheckBox, MicKeyComboBox);
        var copyHotkey = BuildBindingFromControls(CopyCtrlCheckBox, CopyShiftCheckBox, CopyAltCheckBox, CopyWinCheckBox, CopyKeyComboBox);

        if (micHotkey is null || copyHotkey is null)
        {
            SetStatus("Select valid hotkey keys.", Brushes.OrangeRed);
            return;
        }

        if (micHotkey.VirtualKey == copyHotkey.VirtualKey && micHotkey.Modifiers == copyHotkey.Modifiers)
        {
            SetStatus("Record and Copy hotkeys cannot be identical.", Brushes.OrangeRed);
            return;
        }

        _settings.MicrophoneHotkey = micHotkey;
        _settings.CopyHotkey = copyHotkey;
        SaveAndApplySettings("Hotkeys updated", Brushes.SteelBlue);
    }

    private void SaveAndApplySettings(string statusText, Brush statusColor)
    {
        _settingsService.Save(_settings);
        _hotkeyManager?.UpdateSettings(_settings);
        UpdateHotkeyText();
        SetStatus(statusText, statusColor);
    }

    private void OnStartRecordingRequested(object? sender, EventArgs e)
    {
        if (_isTranscribing)
        {
            SetStatus("Transcription in progress. New recording ignored.", Brushes.Goldenrod);
            return;
        }

        if (_audioRecorder.IsRecording)
        {
            return;
        }

        try
        {
            _audioRecorder.StartRecording();
            SetRecordingLight(true);
            SetStatus("Recording...", Brushes.IndianRed);
            ErrorTextBlock.Text = string.Empty;
        }
        catch (Exception ex)
        {
            SetRecordingLight(false);
            SetStatus("Recording failed", Brushes.OrangeRed);
            ErrorTextBlock.Text = ex.Message;
        }
    }

    private async void OnStopRecordingRequested(object? sender, EventArgs e)
    {
        if (!_audioRecorder.IsRecording)
        {
            return;
        }

        _isTranscribing = true;
        SetRecordingLight(false);
        SetStatus("Transcribing...", Brushes.Goldenrod);

        try
        {
            var audioData = await _audioRecorder.StopRecordingAsync();
            var transcription = await _transcriptionService.TranscribeAsync(audioData);
            TranscriptTextBox.Text = transcription;

            if (string.IsNullOrWhiteSpace(transcription))
            {
                SetStatus("No speech detected", Brushes.ForestGreen);
                return;
            }

            if (_settings.AutoCopyAfterTranscription && TryCopyTextToClipboard(transcription))
            {
                SetStatus("Transcription ready and copied", Brushes.ForestGreen);
            }
            else
            {
                SetStatus("Transcription ready", Brushes.ForestGreen);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Transcription failed", Brushes.OrangeRed);
            ErrorTextBlock.Text = ex.Message;
        }
        finally
        {
            _isTranscribing = false;
        }
    }

    private void OnCopyRequested(object? sender, EventArgs e)
    {
        var text = TranscriptTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Nothing to copy", Brushes.Gray);
            return;
        }

        if (TryCopyTextToClipboard(text))
        {
            SetStatus("Copied to clipboard", Brushes.SteelBlue);
        }
    }

    private bool TryCopyTextToClipboard(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("Copy failed", Brushes.OrangeRed);
            ErrorTextBlock.Text = ex.Message;
            return false;
        }
    }

    private void SetRecordingLight(bool isRecording)
    {
        RecordingLight.Opacity = isRecording ? 1.0 : 0.2;
    }

    private void SetStatus(string text, Brush color)
    {
        StatusTextBlock.Text = text;
        StatusIndicator.Fill = color;
    }

    private void UpdateModeSelection()
    {
        var modeTag = _settings.RecordingMode == RecordingMode.Toggle ? "Toggle" : "Push";

        foreach (var item in RecordingModeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), modeTag, StringComparison.OrdinalIgnoreCase))
            {
                RecordingModeComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void UpdateHotkeyText()
    {
        HotkeyTextBlock.Text =
            $"Record ({_settings.RecordingMode}): {_settings.MicrophoneHotkey}    |    Copy: {_settings.CopyHotkey}";
    }

    private void SetNoActivateStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, exStyle | WsExNoActivate);
    }

    private void ClearNoActivateStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, exStyle & ~WsExNoActivate);
    }

    private static HotkeyBinding? BuildBindingFromControls(System.Windows.Controls.CheckBox ctrl, System.Windows.Controls.CheckBox shift, System.Windows.Controls.CheckBox alt, System.Windows.Controls.CheckBox win, System.Windows.Controls.ComboBox keyCombo)
    {
        if (keyCombo.SelectedItem is not string keyName || !Enum.TryParse<System.Windows.Input.Key>(keyName, out var key))
        {
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        if (ctrl.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (shift.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (alt.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (win.IsChecked == true)
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return new HotkeyBinding
        {
            Modifiers = modifiers,
            VirtualKey = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key)
        };
    }

    private static void ApplyBindingToControls(HotkeyBinding binding, System.Windows.Controls.CheckBox ctrl, System.Windows.Controls.CheckBox shift, System.Windows.Controls.CheckBox alt, System.Windows.Controls.CheckBox win, System.Windows.Controls.ComboBox keyCombo)
    {
        ctrl.IsChecked = binding.Modifiers.HasFlag(HotkeyModifiers.Control);
        shift.IsChecked = binding.Modifiers.HasFlag(HotkeyModifiers.Shift);
        alt.IsChecked = binding.Modifiers.HasFlag(HotkeyModifiers.Alt);
        win.IsChecked = binding.Modifiers.HasFlag(HotkeyModifiers.Win);

        var keyText = System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)binding.VirtualKey).ToString();
        keyCombo.SelectedItem = SelectableKeys.Contains(keyText) ? keyText : "F9";
    }

    private void MainWindowOnDeactivated(object? sender, EventArgs e)
    {
        SetNoActivateStyle();
    }

    private void TranscriptTextBox_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ClearNoActivateStyle();
        Activate();
        TranscriptTextBox.Focus();
    }

    private void TranscriptTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!IsActive)
        {
            SetNoActivateStyle();
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}