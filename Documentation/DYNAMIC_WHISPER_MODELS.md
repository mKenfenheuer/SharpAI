# Dynamic Whisper Model Selection

SharpAI now supports dynamic Whisper model selection for audio transcription, allowing you to choose the optimal model based on your speed vs accuracy requirements.

## Available Whisper Models

### Model Comparison

| Model | Size | Speed | Accuracy | Use Case |
|-------|------|-------|----------|----------|
| **tiny** | 39 MB | Very Fast | Low | Real-time, draft transcriptions |
| **base** | 142 MB | Fast | Good | General purpose (default) |
| **small** | 466 MB | Medium | Better | Higher quality transcriptions |
| **medium** | 1.5 GB | Slow | High | Professional transcriptions |
| **large-v1** | 2.9 GB | Very Slow | Highest | Maximum accuracy needed |
| **large-v2** | 2.9 GB | Very Slow | Highest | Improved version of large-v1 |
| **large-v3** | 2.9 GB | Very Slow | Best | Latest and most accurate |

### Model Aliases

- `whisper-1` → `base` (OpenAI API compatibility)
- `whisper-tiny` → `tiny`
- `whisper-base` → `base`
- `whisper-small` → `small`
- `whisper-medium` → `medium`
- `whisper-large` → `large-v3`

## API Usage

### Basic Transcription with Model Selection

```http
POST /v1/audio/transcriptions
Content-Type: multipart/form-data

file: your-audio.mp3
model: small
response_format: json
```

### List Available Audio Models

```http
GET /v1/models/audio
```

### Streaming Transcription with Specific Model

```http
POST /v1/audio/transcriptions
Content-Type: multipart/form-data

file: your-audio.wav
model: tiny
stream: true
```

## Model Selection Guidelines

### For Speed (Real-time Applications)
```json
{
  "model": "tiny",
  "file": "audio.wav"
}
```
- **Use case**: Live transcription, voice commands, quick draft transcripts
- **Performance**: ~10x faster than base model
- **Trade-off**: Lower accuracy, may miss nuanced speech

### For Balanced Performance (Recommended)
```json
{
  "model": "base",
  "file": "audio.mp3"
}
```
- **Use case**: General transcriptions, meetings, podcasts
- **Performance**: Good balance of speed and accuracy
- **Trade-off**: Default choice for most applications

### For High Quality
```json
{
  "model": "small",
  "file": "audio.flac"
}
```
- **Use case**: Professional transcriptions, subtitles, important documents
- **Performance**: Better accuracy with reasonable speed
- **Trade-off**: Longer processing time, larger memory usage

### For Maximum Accuracy
```json
{
  "model": "large-v3",
  "file": "audio.wav"
}
```
- **Use case**: Legal transcriptions, medical records, research
- **Performance**: Best possible accuracy
- **Trade-off**: Significantly slower, requires more resources

## Language Support

All models support the same languages, but larger models typically perform better on:
- Accented speech
- Technical terminology
- Multiple speakers
- Poor audio quality
- Background noise

## Performance Considerations

### Memory Usage
- **tiny**: ~100 MB RAM
- **base**: ~200 MB RAM
- **small**: ~600 MB RAM
- **medium**: ~2 GB RAM
- **large**: ~4 GB RAM

### Processing Speed (approximate, varies by hardware)
- **tiny**: 5-10x real-time
- **base**: 2-5x real-time
- **small**: 1-2x real-time
- **medium**: 0.5-1x real-time
- **large**: 0.2-0.5x real-time

### Storage Requirements
Models are automatically downloaded and cached in `Data/Models/`:
```
Data/Models/
├── ggml-tiny.bin     (39 MB)
├── ggml-base.bin     (142 MB)
├── ggml-small.bin    (466 MB)
├── ggml-medium.bin   (1.5 GB)
└── ggml-large-v3.bin (2.9 GB)
```

## Configuration

Whisper models are configured in `Data/whisper-models-config.json` (auto-generated):

```json
{
  "models": [
    {
      "name": "tiny",
      "ggml_type": "Tiny",
      "filename": "ggml-tiny.bin",
      "description": "Fastest, lowest accuracy",
      "size_mb": 39,
      "speed": "Very Fast",
      "accuracy": "Low"
    }
  ]
}
```

## Error Handling

### Invalid Model
```json
{
  "error": {
    "message": "Whisper model 'invalid-model' is not available. Available models: tiny, base, small, medium, large-v1, large-v2, large-v3",
    "type": "invalid_request_error"
  }
}
```

### Model Download Progress
During first use, models are automatically downloaded:

```
INFO: Whisper model tiny not found locally. Downloading Tiny
INFO: Downloading Whisper model tiny (Tiny) - Size: ~39 MB
INFO: Successfully downloaded Whisper model tiny
INFO: Successfully loaded Whisper model tiny
INFO: Using Whisper model: tiny
```

## Best Practices

### Model Selection Strategy
1. **Start with `base`** for general use cases
2. **Use `tiny`** for real-time or high-volume processing
3. **Use `small`** when accuracy is more important than speed
4. **Use `large-v3`** only when maximum accuracy is critical

### Performance Optimization
- Keep frequently used models loaded by making periodic requests
- Use smaller models for initial processing, larger models for final quality
- Consider audio preprocessing (noise reduction) with smaller models
- Monitor memory usage when using multiple large models

### Audio Format Considerations
- All models work with any audio format (auto-converted to WAV 16kHz)
- Higher quality models benefit more from better audio quality
- Larger models are more robust to audio compression artifacts

## API Endpoints Summary

| Endpoint | Description |
|----------|-------------|
| `GET /v1/models` | List all models (chat + audio) |
| `GET /v1/models/audio` | List only Whisper models |
| `GET /v1/models/{model-id}` | Get specific model info |
| `POST /v1/audio/transcriptions` | Transcribe with model selection |
| `POST /v1/audio/translations` | Translate with model selection |