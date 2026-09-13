# PushToText

`PushToText` is a lightweight Windows desktop app (`WPF`, `.NET 8`) that turns short microphone recordings into text using local speech-to-text transcription.

## What it does

- Listens for global hotkeys even when the app is not focused.
- Records audio from your selected microphone.
- Transcribes recorded speech to text with `Whisper.net`.
- Displays the result in the app text box.
- Copies the latest transcript to the clipboard with a dedicated hotkey.
- Saves preferences (mode, hotkeys, input device) between launches.

## Core functionality

### 1. Recording control modes

You can choose one of two microphone behaviors:

- `Push` mode: hold the mic hotkey to record, release to transcribe.
- `Toggle` mode: press mic hotkey once to start, press again to stop and transcribe.

### 2. Global hotkeys

The app supports customizable hotkeys for:

- `Mic hotkey` (start/stop recording)
- `Copy hotkey` (copy transcript to clipboard)

Default hotkeys:

- Mic: `Ctrl+Shift+F9`
- Copy: `Ctrl+Shift+F10`

### 3. Audio input selection

- Select a microphone from available input devices.
- Refresh the device list from the UI.
- Selected input is remembered in settings.

### 4. Local Whisper model

On first launch, the app downloads the `ggml-base.en` Whisper model to local app data and reuses it afterward.

- Model path: `%LOCALAPPDATA%\PushToText\model\ggml-base.en.bin`

### 5. Persistent settings

Settings are stored as JSON:

- `%LOCALAPPDATA%\PushToText\settings.json`

Stored values include:

- Recording mode
- Selected microphone device number
- Mic hotkey
- Copy hotkey

## UI overview

- Status indicator and recording light
- Options panel for mode, audio input, and hotkeys
- Transcript text area
- Error/status text for failures (device/model/copy/transcription)

## Requirements

- Windows
- `.NET 8` runtime
- Microphone input device
- Internet connection on first run (for initial model download)

## Run the app

Run PushToText.exe

## Dependencies

- `NAudio` (microphone capture)
- `Whisper.net` + `Whisper.net.Runtime` (speech transcription)
