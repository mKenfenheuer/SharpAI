# Audio Conversion Feature

SharpAI now supports automatic audio format conversion to ensure optimal compatibility with Whisper models.

## Overview

The audio transcription endpoints now automatically convert any input audio format to WAV 16kHz mono before processing with Whisper. This ensures consistent, high-quality transcription results regardless of the input format.

## Supported Input Formats

- **MP3** - MPEG-1 Audio Layer III
- **MP4/M4A** - MPEG-4 Audio
- **WAV** - Waveform Audio File Format (any sample rate)
- **FLAC** - Free Lossless Audio Codec
- **OGG** - Ogg Vorbis
- **AAC** - Advanced Audio Codec
- **WMA** - Windows Media Audio
- **AIFF** - Audio Interchange File Format
- And many more formats supported by FFmpeg

## Conversion Process

1. **Upload**: Submit audio file in any supported format
2. **Detection**: System automatically detects the input format
3. **Conversion**: Audio is converted to WAV 16kHz mono using FFmpeg
4. **Transcription**: Converted audio is processed by Whisper
5. **Cleanup**: Temporary files are automatically removed

## Technical Details

### Conversion Parameters
- **Sample Rate**: 16kHz (optimal for Whisper models)
- **Channels**: Mono (single channel)
- **Format**: WAV with 16-bit PCM encoding
- **Codec**: pcm_s16le

### Performance Considerations
- Conversion adds minimal latency (typically < 1 second for most files)
- Large files may take longer to convert
- Temporary disk space is used during conversion
- All temporary files are cleaned up automatically

## API Usage

The conversion is completely transparent - no changes needed to your API calls:

```http
POST /v1/audio/transcriptions
Content-Type: multipart/form-data

# Send any audio format - conversion happens automatically
file: your_audio.mp3 (or .m4a, .flac, .ogg, etc.)
model: whisper-1
response_format: json
```

## Error Handling

If audio conversion fails:
- The API returns a 500 error with details
- Original error is logged for debugging
- Temporary files are still cleaned up

## Dependencies

- **FFMpegCore**: .NET wrapper for FFmpeg
- **FFmpeg**: Must be installed on the system for conversion to work

## Installation Notes

Ensure FFmpeg is installed on your system:

### macOS
```bash
brew install ffmpeg
```

### Ubuntu/Debian
```bash
sudo apt update
sudo apt install ffmpeg
```

### Windows
Download from https://ffmpeg.org/download.html or use:
```bash
winget install ffmpeg
```

## Logging

Audio conversion activities are logged at INFO level:
- Successful conversions
- Input format detection
- Performance metrics
- Error details

Example log output:
```
INFO: Successfully converted audio from audio.mp3 to WAV 16kHz mono
```