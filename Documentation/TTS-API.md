# Text-to-Speech (TTS) API

SharpAI provides OpenAI-compatible Text-to-Speech endpoints with additional extensions for emotion control and voice cloning using the ChatterboxService.

## Supported Languages

ChatterboxMultilingualTTS supports the following languages:

**Arabic (ar)** • **Danish (da)** • **German (de)** • **Greek (el)** • **English (en)** • **Spanish (es)** • **Finnish (fi)** • **French (fr)** • **Hebrew (he)** • **Hindi (hi)** • **Italian (it)** • **Japanese (ja)** • **Korean (ko)** • **Malay (ms)** • **Dutch (nl)** • **Norwegian (no)** • **Polish (pl)** • **Portuguese (pt)** • **Russian (ru)** • **Swedish (sv)** • **Swahili (sw)** • **Turkish (tr)** • **Chinese (zh)**

Languages are automatically detected if not specified, or you can specify the language code explicitly.

## Architecture

The TTS service is implemented as a hosted background service that:
- Initializes the Python TTS model once on startup
- Processes requests sequentially using a single model instance
- Handles multiple concurrent requests by queuing them
- Maintains Python GIL (Global Interpreter Lock) safety
- Provides better resource management and stability

## Endpoints

### POST /v1/audio/speech

Creates audio from text using text-to-speech. This endpoint is fully compatible with OpenAI's TTS API.

#### Request Body (JSON)

```json
{
  "input": "Hello, this is a test of the text-to-speech system.",
  "model": "tts-1",
  "voice": "alloy",
  "response_format": "mp3",
  "speed": 1.0,
  "emotion_intensity": 0.5,
  "reference_audio": null
}
```

#### Parameters

- **input** (string, required): The text to generate audio for. Maximum length is 4096 characters.
- **model** (string, required): One of the available TTS models: `tts-1` or `tts-1-hd`
- **voice** (string, required): The voice to use. Supported voices: `alloy`, `echo`, `fable`, `onyx`, `nova`, `shimmer`
- **response_format** (string, optional): The audio format. Supported: `mp3`, `opus`, `aac`, `flac`, `wav`, `pcm`. Default: `mp3`
- **speed** (number, optional): The speed of the generated audio. Range: 0.25 to 4.0. Default: 1.0
- **emotion_intensity** (number, optional): SharpAI extension. Emotion intensity for voice synthesis. Range: 0.0 to 1.0. Default: 0.5. **Note**: Maps to the `exaggeration` parameter in ChatterboxMultilingualTTS for controlling emotional expressiveness.
- **reference_audio** (string, optional): SharpAI extension. Path to reference audio file for voice cloning.
- **cfg_weight** (number, optional): SharpAI extension. CFG weight for controlling generation quality. Range: 0.0 to 1.0. Default: 0.5. **Note**: Automatically adjusted based on exaggeration level - lower values (~0.3) are used for dramatic speech (high exaggeration) to improve pacing.
- **exaggeration** (number, optional): SharpAI extension. Exaggeration level for expressive speech. Range: 0.0 to 1.0. Default: 0.5. Higher values (~0.7) for more dramatic/expressive speech. **Note**: cfg_weight is automatically optimized based on this value.

#### Response

Returns the audio file in the requested format with appropriate content-type headers.

### POST /v1/audio/speech/form

Creates audio from text with support for uploading reference audio files for voice cloning. This is a SharpAI extension.

#### Request Body (multipart/form-data)

- **input** (string, required): The text to generate audio for
- **model** (string, required): TTS model to use
- **voice** (string, required): Voice to use
- **response_format** (string, optional): Audio format
- **speed** (number, optional): Speech speed
- **emotion_intensity** (number, optional): Emotion intensity (maps to exaggeration parameter)
- **reference_audio** (file, optional): Reference audio file for voice cloning
- **cfg_weight** (number, optional): CFG weight for generation control (0.0-1.0, default: 0.5)
- **exaggeration** (number, optional): Exaggeration level for expressive speech (0.0-1.0, default: 0.5)

#### Response

Returns the audio file in the requested format.

## OpenAI Compatibility

The `/v1/audio/speech` endpoint is fully compatible with OpenAI's Text-to-Speech API. You can use it as a drop-in replacement by:

1. Changing the base URL to your SharpAI instance
2. Using the same request format and parameters
3. The response will be identical to OpenAI's format

## SharpAI Extensions

SharpAI provides additional features beyond the OpenAI API:

### Voice Cloning
Use the `/v1/audio/speech/form` endpoint with a `reference_audio` file to clone voices.

### Advanced Generation Control
- **cfg_weight**: Controls generation quality and pacing. Automatically optimized based on exaggeration level.
- **exaggeration**: Controls expressiveness. Higher values create more dramatic speech with automatically adjusted pacing.

### Intelligent Parameter Optimization

SharpAI automatically optimizes parameters based on the speech style:

- **Dramatic Speech** (exaggeration > 0.6): cfg_weight is automatically reduced to ~0.3 for better pacing
- **Controlled Speech** (exaggeration < 0.4): cfg_weight is maintained at 0.5 for more control
- **Normal Speech** (exaggeration ~0.5): Uses balanced default settings

### Best Practices

#### General Use (TTS and Voice Agents)
- Ensure that the reference clip matches the specified language tag to avoid accent inheritance
- The system automatically optimizes cfg_weight based on exaggeration level
- Default settings work well for most prompts across all languages
- For reference speakers with fast speaking styles, the system automatically adjusts pacing

#### Expressive or Dramatic Speech
- Higher exaggeration values (~0.7) automatically trigger lower cfg_weight (~0.3) for optimal pacing
- The system compensates for speed increase from high exaggeration with deliberate pacing control
- Manual cfg_weight adjustment is rarely needed due to intelligent optimization

### Emotion Control (Future Feature)
The `emotion_intensity` parameter is included for API compatibility but is currently not supported by the ChatterboxMultilingualTTS model. This may be supported in future model versions.

## Examples

### Basic TTS (OpenAI Compatible)

```bash
curl -X POST "http://localhost:5000/v1/audio/speech" \
  -H "Content-Type: application/json" \
  -d '{
    "input": "Hello world!",
    "model": "tts-1",
    "voice": "alloy"
  }' \
  --output speech.mp3
```

### Emotion Control with TTS

```bash
curl -X POST "http://localhost:5000/v1/audio/speech" \
  -H "Content-Type: application/json" \
  -d '{
    "input": "I am so excited about this amazing breakthrough!",
    "model": "tts-1",
    "voice": "nova",
    "response_format": "wav",
    "emotion_intensity": 0.8
  }' \
  --output excited_speech.wav
```

### Advanced TTS with Custom Parameters

```bash
curl -X POST "http://localhost:5000/v1/audio/speech" \
  -H "Content-Type: application/json" \
  -d '{
    "input": "This is dramatic and expressive speech!",
    "model": "tts-1",
    "voice": "nova",
    "response_format": "wav",
    "speed": 1.1,
    "exaggeration": 0.7
  }' \
  --output dramatic_speech.wav
```

### Voice Cloning with Reference Audio

```bash
curl -X POST "http://localhost:5000/v1/audio/speech/form" \
  -F "input=Hello, this is my cloned voice!" \
  -F "model=tts-1" \
  -F "voice=alloy" \
  -F "reference_audio=@reference_voice.wav" \
  --output cloned_speech.wav
```

## Error Responses

The API returns standard HTTP error codes with JSON error responses:

```json
{
  "error": {
    "message": "Input text cannot exceed 4096 characters",
    "type": "invalid_request_error"
  }
}
```

Common error codes:
- **400**: Invalid request (missing parameters, invalid values)
- **500**: Server error (TTS processing failed)