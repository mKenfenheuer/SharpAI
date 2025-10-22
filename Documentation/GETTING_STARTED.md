# Getting Started with SharpAI

This quick start guide will get you up and running with SharpAI in minutes.

## 🚀 Quick Start

### Option 1: Docker (Recommended)

The fastest way to get started is with Docker:

```bash
# Pull and run the CPU version
docker run -d \
  --name sharpai \
  -p 5000:80 \
  -v $(pwd)/sharpai-data:/app/Data \
  ghcr.io/your-username/sharpai:cpu

# Or with GPU support (CUDA 12)
docker run -d \
  --name sharpai \
  -p 5000:80 \
  -v $(pwd)/sharpai-data:/app/Data \
  --gpus all \
  ghcr.io/your-username/sharpai:cuda12
```

### Option 2: Local Development

If you have .NET 9 SDK installed:

```bash
git clone https://github.com/your-username/SharpAI.git
cd SharpAI
dotnet run
```

## 🧪 Test Your Installation

### 1. Check Available Models

```bash
curl http://localhost:5000/v1/models
```

Expected response:
```json
{
  "object": "list",
  "data": [
    {
      "id": "llama-3.2-3b-instruct",
      "object": "model",
      "type": "chat"
    },
    {
      "id": "whisper-base",
      "object": "model", 
      "type": "audio"
    }
  ]
}
```

### 2. Try Chat Completion

```bash
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama-3.2-3b-instruct",
    "messages": [
      {"role": "user", "content": "Say hello in 3 different languages"}
    ],
    "max_tokens": 100
  }'
```

### 3. Test Audio Transcription

First, create a test audio file or use any audio file you have:

```bash
curl -X POST http://localhost:5000/v1/audio/transcriptions \
  -H "Content-Type: multipart/form-data" \
  -F "file=@your-audio-file.mp3" \
  -F "model=whisper-base"
```

## 📝 Your First Application

### Python Example

```python
import openai

# Configure the client to use SharpAI
client = openai.OpenAI(
    base_url="http://localhost:5000/v1",
    api_key="not-required"  # SharpAI doesn't require auth yet
)

# Chat completion
response = client.chat.completions.create(
    model="llama-3.2-3b-instruct",
    messages=[
        {"role": "system", "content": "You are a helpful coding assistant."},
        {"role": "user", "content": "Write a Python function to calculate fibonacci numbers."}
    ],
    max_tokens=200
)

print(response.choices[0].message.content)

# Audio transcription
with open("meeting.mp3", "rb") as audio_file:
    transcript = client.audio.transcriptions.create(
        model="whisper-base",
        file=audio_file
    )
    
print(transcript.text)
```

### JavaScript/Node.js Example

```javascript
import OpenAI from 'openai';
import fs from 'fs';

const openai = new OpenAI({
    baseURL: 'http://localhost:5000/v1',
    apiKey: 'not-required'
});

// Chat completion
async function chatExample() {
    const completion = await openai.chat.completions.create({
        model: 'llama-3.2-3b-instruct',
        messages: [
            { role: 'user', content: 'Explain async/await in JavaScript' }
        ],
        max_tokens: 150
    });
    
    console.log(completion.choices[0].message.content);
}

// Audio transcription
async function transcriptionExample() {
    const transcription = await openai.audio.transcriptions.create({
        file: fs.createReadStream('audio.mp3'),
        model: 'whisper-base'
    });
    
    console.log(transcription.text);
}

chatExample();
transcriptionExample();
```

### cURL Examples

#### Streaming Chat
```bash
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama-3.2-3b-instruct",
    "messages": [{"role": "user", "content": "Count from 1 to 10"}],
    "stream": true
  }' \
  --no-buffer
```

#### Function Calling
```bash
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama-3.2-3b-instruct",
    "messages": [
      {"role": "user", "content": "What is the weather like in Tokyo?"}
    ],
    "tools": [
      {
        "type": "function",
        "function": {
          "name": "get_weather",
          "description": "Get current weather for a city",
          "parameters": {
            "type": "object",
            "properties": {
              "city": {"type": "string"}
            },
            "required": ["city"]
          }
        }
      }
    ]
  }'
```

## 🎛️ Model Selection Guide

### Chat Models - When to Use Which

| Model | Speed | Quality | RAM Usage | Best For |
|-------|-------|---------|-----------|----------|
| **llama-3.2-1b-instruct** | ⚡⚡⚡ | ⭐⭐ | Low | Quick responses, simple tasks |
| **llama-3.2-3b-instruct** | ⚡⚡ | ⭐⭐⭐ | Medium | General purpose, good balance |
| **phi-3-mini-instruct** | ⚡⚡ | ⭐⭐⭐⭐ | Medium | Instruction following, coding |
| **gemma-2-2b-instruct** | ⚡⚡ | ⭐⭐⭐ | Medium | Reasoning, analysis |

### Audio Models - When to Use Which

| Model | Speed | Accuracy | Size | Best For |
|-------|-------|----------|------|----------|  
| **whisper-tiny** | ⚡⚡⚡ | ⭐ | 39MB | Real-time, low resource |
| **whisper-base** | ⚡⚡ | ⭐⭐⭐ | 74MB | Most applications |
| **whisper-small** | ⚡ | ⭐⭐⭐⭐ | 244MB | Better accuracy needed |
| **whisper-large-v3** | ⚡ | ⭐⭐⭐⭐⭐ | 1.5GB | Maximum accuracy |

## 🔧 Basic Configuration

### Change Default Models

Create `Data/models-config.json`:

```json
{
  "models": [
    {
      "name": "my-favorite-model",
      "huggingface_id": "microsoft/Phi-3-mini-4k-instruct-gguf",
      "filename": "Phi-3-mini-4k-instruct-q4.gguf",
      "context_size": 4096,
      "gpu_layers": 10,
      "description": "My preferred model for coding tasks"
    }
  ]
}
```

### Adjust for Your Hardware

#### For 4GB GPU:
```json
{
  "models": [
    {
      "name": "llama-3.2-1b-instruct",
      "gpu_layers": 15,
      "context_size": 2048
    }
  ]
}
```

#### For 8GB+ GPU:
```json
{
  "models": [
    {
      "name": "llama-3.2-3b-instruct", 
      "gpu_layers": 35,
      "context_size": 8192
    }
  ]
}
```

#### For CPU Only:
```json
{
  "models": [
    {
      "name": "llama-3.2-1b-instruct",
      "gpu_layers": 0,
      "context_size": 2048
    }
  ]
}
```

## ⚡ Performance Tips

### 1. Model Loading
- First request to a new model will be slower (downloading + loading)
- Subsequent requests to the same model are fast
- Models stay loaded in memory for better performance

### 2. GPU Acceleration
- Use CUDA 12 image for best GPU performance
- Adjust `gpu_layers` based on your GPU memory
- More GPU layers = faster inference

### 3. Context Management
- Larger `context_size` = more memory usage
- Choose context size based on your needs:
  - 2048: Short conversations
  - 4096: Standard conversations  
  - 8192+: Long documents, complex tasks

## 🐛 Troubleshooting

### Common Issues

**"Model not found" error**
```bash
# Check available models
curl http://localhost:5000/v1/models

# Use exact model name from the list
curl -X POST http://localhost:5000/v1/chat/completions \
  -d '{"model": "llama-3.2-3b-instruct", ...}'
```

**Slow first response**
- This is normal - the model is downloading/loading
- Check logs: `docker logs sharpai`
- Subsequent requests will be much faster

**Out of memory errors**
- Reduce `gpu_layers` in model config
- Use smaller model (1B instead of 3B)
- Reduce `context_size`

**Audio upload fails**
- Check file size (max 100MB by default)
- Verify audio format is supported
- Try converting to WAV: `ffmpeg -i input.audio output.wav`

### Getting Help

1. **Check the logs**: `docker logs sharpai -f`
2. **Test with curl**: Use the examples above to isolate issues
3. **Verify hardware**: Ensure GPU drivers are installed for CUDA images
4. **Check documentation**: See the `Documentation/` folder for detailed guides

## 🎯 Next Steps

Now that you have SharpAI running:

1. **Read the [API Documentation](Documentation/API.md)** for complete endpoint reference
2. **Check the [Configuration Guide](Documentation/CONFIGURATION.md)** to customize your setup
3. **See [Deployment Guide](Documentation/DEPLOYMENT.md)** for production deployment
4. **Explore the examples** in the `Test/` directory

## 💡 Example Use Cases

### Code Assistant
```python
response = client.chat.completions.create(
    model="phi-3-mini-instruct",
    messages=[
        {"role": "system", "content": "You are an expert programmer."},
        {"role": "user", "content": "Review this Python code and suggest improvements: [code here]"}
    ]
)
```

### Meeting Transcription
```python
transcript = client.audio.transcriptions.create(
    model="whisper-large-v3",  # High accuracy for professional use
    file=open("meeting.mp3", "rb")
)
```

### Content Generation
```python
response = client.chat.completions.create(
    model="llama-3.2-3b-instruct",
    messages=[
        {"role": "user", "content": "Write a blog post about sustainable technology"}
    ],
    max_tokens=1000
)
```

Welcome to SharpAI! 🎉