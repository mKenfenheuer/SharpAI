# API Documentation

SharpAI provides OpenAI-compatible REST APIs for chat completions, audio transcription, and model management. All endpoints follow OpenAI's API specifications for seamless integration.

## Base URL

```
http://localhost:5000/v1
```

## Authentication

Currently, SharpAI does not require authentication. In production environments, consider implementing proper authentication and authorization mechanisms.

## API Endpoints

### Chat Completions

#### Create Chat Completion

Creates a chat completion, either as a single response or a stream.

**Endpoint:** `POST /v1/chat/completions`

**Request Body:**

```json
{
  "model": "string",
  "messages": [
    {
      "role": "system|user|assistant",
      "content": "string"
    }
  ],
  "stream": false,
  "temperature": 0.7,
  "max_tokens": 1000,
  "top_p": 1.0,
  "frequency_penalty": 0.0,
  "presence_penalty": 0.0,
  "response_format": {
    "type": "text|json_object"
  }
}
```

**Parameters:**

- `model` (required): The model to use for completion
- `messages` (required): Array of messages in the conversation
- `stream` (optional): Whether to stream the response. Default: `false`
- `temperature` (optional): Sampling temperature (0-2). Default: `0.7`
- `max_tokens` (optional): Maximum tokens to generate
- `top_p` (optional): Nucleus sampling parameter. Default: `1.0`
- `frequency_penalty` (optional): Frequency penalty (-2 to 2). Default: `0.0`
- `presence_penalty` (optional): Presence penalty (-2 to 2). Default: `0.0`
- `response_format` (optional): Format of the response

**Non-Streaming Response:**

```json
{
  "id": "chatcmpl-123",
  "object": "chat.completion",
  "created": 1677652288,
  "model": "default",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "Hello! How can I help you today?"
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 10,
    "completion_tokens": 12,
    "total_tokens": 22
  }
}
```

**Streaming Response:**

When `stream: true`, the response is sent as Server-Sent Events:

```
data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1677652288,"model":"default","choices":[{"index":0,"delta":{"role":"assistant","content":"Hello"},"finish_reason":null}]}

data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1677652288,"model":"default","choices":[{"index":0,"delta":{"content":"!"},"finish_reason":null}]}

data: {"id":"chatcmpl-123","object":"chat.completion.chunk","created":1677652288,"model":"default","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

data: [DONE]
```

**Example cURL:**

```bash
# Non-streaming request
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "default",
    "messages": [
      {"role": "user", "content": "Hello, how are you?"}
    ],
    "stream": false,
    "temperature": 0.7
  }'

# Streaming request
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "default",
    "messages": [
      {"role": "user", "content": "Tell me a story"}
    ],
    "stream": true
  }'
```

#### Test Request (Development)

Test endpoint for debugging request parsing.

**Endpoint:** `POST /v1/chat/test-request`

Returns the parsed request structure without generating a completion.

### Audio

#### Create Transcription

Transcribes audio to text using Whisper models.

**Endpoint:** `POST /v1/audio/transcriptions`

**Request:** Multipart form data

**Parameters:**

- `file` (required): Audio file to transcribe
- `model` (required): Model to use (e.g., "whisper-1")
- `language` (optional): Language of the audio (ISO-639-1 code)
- `prompt` (optional): Optional text to guide the model's style
- `response_format` (optional): Format of the response (`json`, `text`, `srt`, `verbose_json`, `vtt`)
- `temperature` (optional): Sampling temperature (0-1)
- `timestamp_granularities` (optional): Timestamp granularities (`word`, `segment`)
- `stream` (optional): Whether to stream the transcription (custom extension)

**Supported Audio Formats:**
- MP3 (`audio/mpeg`)
- MP4 (`audio/mp4`)
- WAV (`audio/wav`)
- WebM (`audio/webm`)
- M4A (`audio/m4a`)
- OGG (`audio/ogg`)
- FLAC (`audio/flac`)

**Response (JSON format):**

```json
{
  "text": "Hello, this is a test transcription.",
  "segments": [
    {
      "id": 0,
      "start": 0,
      "end": 2000,
      "text": "Hello, this is a test transcription.",
      "avg_logprob": -0.45,
      "compression_ratio": 1.2,
      "no_speech_prob": 0.01,
      "temperature": 0.0
    }
  ],
  "language": "en"
}
```

**Response (Text format):**

```
Hello, this is a test transcription.
```

**Response (SRT format):**

```
1
00:00:00,000 --> 00:00:02,000
Hello, this is a test transcription.
```

**Response (VTT format):**

```
WEBVTT

00:00:00.000 --> 00:00:02.000
Hello, this is a test transcription.
```

**Example cURL:**

```bash
# Basic transcription
curl -X POST http://localhost:5000/v1/audio/transcriptions \
  -F "file=@audio.mp3" \
  -F "model=whisper-1"

# With language and response format
curl -X POST http://localhost:5000/v1/audio/transcriptions \
  -F "file=@audio.wav" \
  -F "model=whisper-1" \
  -F "language=en" \
  -F "response_format=verbose_json"

# SRT subtitle format
curl -X POST http://localhost:5000/v1/audio/transcriptions \
  -F "file=@speech.m4a" \
  -F "model=whisper-1" \
  -F "response_format=srt"
```

### Models

#### List Models

Lists all available models.

**Endpoint:** `GET /v1/models`

**Response:**

```json
{
  "object": "list",
  "data": [
    {
      "id": "default",
      "object": "model",
      "created": 1677652288,
      "owned_by": "huggingface"
    },
    {
      "id": "whisper-1",
      "object": "model",
      "created": 1677652288,
      "owned_by": "openai"
    }
  ]
}
```

**Example cURL:**

```bash
curl http://localhost:5000/v1/models
```

## Advanced Features

### Tool Calling and Browser Integration

SharpAI supports advanced tool calling capabilities through the Harmony system, including:

- **Web Search**: Automatically search the web using Brave Search API
- **Browser Automation**: Navigate and extract content from web pages
- **Recursive Tool Calling**: Tools can call other tools to complete complex tasks

Tool calling is automatically triggered based on the conversation context and user requests.

### Streaming with Tool Integration

When using streaming mode with tool calling:

1. The model generates a response
2. If tools are needed, they are executed automatically
3. Tool results are fed back to the model
4. The model continues generating with the new context
5. The final response is streamed to the client

### Response Formats

#### JSON Mode

For structured responses, use `response_format.type = "json_object"`:

```json
{
  "model": "default",
  "messages": [
    {"role": "user", "content": "Generate a JSON object with a person's name and age"}
  ],
  "response_format": {
    "type": "json_object"
  }
}
```

## Error Handling

All endpoints return standard HTTP status codes and error responses:

**400 Bad Request:**
```json
{
  "error": {
    "message": "Invalid request format",
    "type": "invalid_request_error"
  }
}
```

**500 Internal Server Error:**
```json
{
  "error": {
    "message": "Internal server error",
    "type": "internal_error"
  }
}
```

## Rate Limiting

Currently, SharpAI does not implement rate limiting. In production environments, consider implementing rate limiting based on your requirements.

## OpenAI SDK Compatibility

SharpAI is designed to be compatible with OpenAI's official SDKs. Simply change the base URL to point to your SharpAI instance:

**Python:**
```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:5000/v1",
    api_key="not-needed"  # API key not required but SDK expects it
)

# Use as normal
response = client.chat.completions.create(
    model="default",
    messages=[{"role": "user", "content": "Hello!"}]
)
```

**Node.js:**
```javascript
import OpenAI from 'openai';

const openai = new OpenAI({
  baseURL: 'http://localhost:5000/v1',
  apiKey: 'not-needed'
});

const response = await openai.chat.completions.create({
  model: 'default',
  messages: [{ role: 'user', content: 'Hello!' }]
});
```

## Custom Extensions

SharpAI includes some custom extensions beyond the OpenAI API:

- **Streaming Audio Transcription**: Set `stream: true` in audio transcription requests
- **Advanced Tool Integration**: Automatic web search and browser automation
- **Harmony Format**: Advanced prompt formatting for better model performance

These extensions are designed to be backwards compatible and can be safely ignored by standard OpenAI clients.