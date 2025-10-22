# API Documentation

SharpAI provides an OpenAI-compatible REST API for chat completions and audio transcription. All endpoints follow OpenAI's API specifications for seamless integration with existing applications.

## Base URL

```
http://localhost:5000
```

## Authentication

Currently, SharpAI does not require authentication. This may be added in future versions.

## Content Types

- **Request**: `application/json` (for most endpoints)
- **Request**: `multipart/form-data` (for audio uploads)
- **Response**: `application/json`

---

## Chat Completions

### POST /v1/chat/completions

Generate chat completions using various language models.

#### Request Body

```json
{
  "model": "string",           // Required: Model to use
  "messages": [                // Required: Conversation messages
    {
      "role": "string",        // "system", "user", or "assistant"
      "content": "string"      // Message content
    }
  ],
  "max_tokens": 150,           // Optional: Maximum tokens to generate
  "temperature": 0.7,          // Optional: Sampling temperature (0.0 to 2.0)
  "top_p": 1.0,               // Optional: Nucleus sampling parameter
  "n": 1,                     // Optional: Number of completions to generate
  "stream": false,            // Optional: Whether to stream responses
  "stop": ["string"],         // Optional: Stop sequences
  "presence_penalty": 0.0,    // Optional: Presence penalty (-2.0 to 2.0)
  "frequency_penalty": 0.0,   // Optional: Frequency penalty (-2.0 to 2.0)
  "tools": [                  // Optional: Available tools/functions
    {
      "type": "function",
      "function": {
        "name": "string",
        "description": "string",
        "parameters": {
          "type": "object",
          "properties": {
            "param_name": {
              "type": "string",
              "description": "string"
            }
          },
          "required": ["param_name"]
        }
      }
    }
  ],
  "tool_choice": "auto",      // Optional: Tool choice behavior
  "response_format": {        // Optional: Response format specification
    "type": "json_object"
  }
}
```

#### Response

```json
{
  "id": "chatcmpl-abc123",
  "object": "chat.completion",
  "created": 1677858242,
  "model": "llama-3.2-3b-instruct",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "Hello! I'm doing well, thank you for asking."
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

#### Streaming Response

When `stream: true` is set, responses are sent as Server-Sent Events:

```
data: {"id":"chatcmpl-abc123","object":"chat.completion.chunk","created":1677858242,"model":"llama-3.2-3b-instruct","choices":[{"index":0,"delta":{"role":"assistant","content":"Hello"},"finish_reason":null}]}

data: {"id":"chatcmpl-abc123","object":"chat.completion.chunk","created":1677858242,"model":"llama-3.2-3b-instruct","choices":[{"index":0,"delta":{"content":"!"},"finish_reason":null}]}

data: [DONE]
```

#### Example Request

```bash
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama-3.2-3b-instruct",
    "messages": [
      {"role": "system", "content": "You are a helpful assistant."},
      {"role": "user", "content": "Explain quantum computing in simple terms."}
    ],
    "max_tokens": 200,
    "temperature": 0.7,
    "stream": false
  }'
```

---

## Audio Transcriptions

### POST /v1/audio/transcriptions

Transcribe audio files to text using Whisper models.

#### Request

- **Content-Type**: `multipart/form-data`
- **file**: Audio file (required)
- **model**: Whisper model name (required)
- **prompt**: Optional transcription guide
- **response_format**: Response format (optional, default: "json")
- **temperature**: Sampling temperature (optional, 0.0 to 1.0)
- **language**: Source language (optional, ISO-639-1 format)

#### Supported Audio Formats

- MP3
- MP4
- MPEG
- MPGA
- M4A
- WAV
- WEBM
- Any format supported by FFmpeg

#### Response

```json
{
  "text": "This is the transcribed text from the audio file.",
  "segments": [
    {
      "id": 0,
      "start": 0.0,
      "end": 2.5,
      "text": "This is the transcribed text"
    },
    {
      "id": 1,
      "start": 2.5,
      "end": 5.0,
      "text": " from the audio file."
    }
  ]
}
```

#### Example Request

```bash
curl -X POST http://localhost:5000/v1/audio/transcriptions \
  -H "Content-Type: multipart/form-data" \
  -F "file=@meeting_recording.mp3" \
  -F "model=whisper-base" \
  -F "response_format=json" \
  -F "language=en"
```

---

## Models

### GET /v1/models

List all available models (chat and audio).

#### Response

```json
{
  "object": "list",
  "data": [
    {
      "id": "llama-3.2-3b-instruct",
      "object": "model",
      "created": 1677858242,
      "owned_by": "meta",
      "type": "chat"
    },
    {
      "id": "whisper-base",
      "object": "model",
      "created": 1677858242,
      "owned_by": "openai",
      "type": "audio"
    }
  ]
}
```

### GET /v1/models/chat

List available chat completion models.

### GET /v1/models/audio

List available audio transcription models.

---

## Function Calling

SharpAI supports OpenAI-style function calling for chat completions.

### Function Definition

```json
{
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_current_weather",
        "description": "Get the current weather in a given location",
        "parameters": {
          "type": "object",
          "properties": {
            "location": {
              "type": "string",
              "description": "The city and state, e.g. San Francisco, CA"
            },
            "unit": {
              "type": "string",
              "enum": ["celsius", "fahrenheit"],
              "description": "The temperature unit to use"
            }
          },
          "required": ["location"]
        }
      }
    }
  ],
  "tool_choice": "auto"
}
```

### Tool Choice Options

- `"auto"`: Let the model decide whether to call functions
- `"none"`: Don't call any functions
- `{"type": "function", "function": {"name": "function_name"}}`: Force a specific function call

### Function Call Response

```json
{
  "choices": [
    {
      "message": {
        "role": "assistant",
        "content": null,
        "tool_calls": [
          {
            "id": "call_abc123",
            "type": "function",
            "function": {
              "name": "get_current_weather",
              "arguments": "{\"location\": \"San Francisco, CA\", \"unit\": \"celsius\"}"
            }
          }
        ]
      },
      "finish_reason": "tool_calls"
    }
  ]
}
```

---

## Error Responses

All endpoints return appropriate HTTP status codes and error messages:

### 400 Bad Request

```json
{
  "error": {
    "message": "Invalid model specified. Available models: llama-3.2-3b-instruct, whisper-base",
    "type": "invalid_request_error",
    "code": "invalid_model"
  }
}
```

### 422 Unprocessable Entity

```json
{
  "error": {
    "message": "File upload required",
    "type": "invalid_request_error",
    "code": "missing_file"
  }
}
```

### 500 Internal Server Error

```json
{
  "error": {
    "message": "Model loading failed",
    "type": "server_error",
    "code": "model_error"
  }
}
```

---

## Rate Limits

Currently, SharpAI does not implement rate limiting. This may be added in future versions based on deployment requirements.

---

## Response Formats

### JSON Response Format

For structured JSON output, use the `response_format` parameter:

```json
{
  "response_format": {
    "type": "json_object"
  }
}
```

This instructs the model to return valid JSON in its response.

---

## Model-Specific Notes

### Chat Models

- **llama-3.2-3b-instruct**: Best balance of speed and quality
- **llama-3.2-1b-instruct**: Fastest option, good for simple tasks
- **llama-3.2-3b-instruct-turbo**: Optimized for performance
- **phi-3-mini-instruct**: Excellent instruction following
- **gemma-2-2b-instruct**: Strong reasoning capabilities

### Audio Models

- **whisper-tiny**: 39MB, ~32x realtime, lowest accuracy
- **whisper-base**: 74MB, ~16x realtime, good for most use cases
- **whisper-small**: 244MB, ~6x realtime, better accuracy
- **whisper-medium**: 769MB, ~2x realtime, high accuracy
- **whisper-large-v3**: 1550MB, ~1x realtime, best accuracy

Choose models based on your speed vs. accuracy requirements.

---

## SDK Integration

SharpAI is compatible with existing OpenAI SDKs. Simply change the base URL:

### Python (openai)

```python
import openai

client = openai.OpenAI(
    base_url="http://localhost:5000/v1",
    api_key="not-required"  # Set to any value
)

response = client.chat.completions.create(
    model="llama-3.2-3b-instruct",
    messages=[{"role": "user", "content": "Hello!"}]
)
```

### JavaScript/TypeScript

```javascript
import OpenAI from 'openai';

const openai = new OpenAI({
    baseURL: 'http://localhost:5000/v1',
    apiKey: 'not-required'
});

const response = await openai.chat.completions.create({
    model: 'llama-3.2-3b-instruct',
    messages: [{ role: 'user', content: 'Hello!' }]
});
```

### curl

All examples in this documentation use curl and can be run directly from the command line.