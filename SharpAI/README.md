# SharpAI

A high-performance OpenAI-compatible API server built with ASP.NET Core, featuring dynamic model selection for both chat completions and audio transcription.

## 🚀 Features

- **OpenAI Compatible API**: Drop-in replacement for OpenAI's chat completions and audio transcription endpoints
- **Dynamic Model Selection**: Automatically download and switch between different models based on request parameters
- **Multi-Architecture Support**: CPU, CUDA 11, and CUDA 12 Docker images available
- **Audio Format Conversion**: Universal audio format support with automatic WAV 16kHz conversion
- **Function Calling**: Support for OpenAI-style function calling and tool usage
- **Streaming Support**: Real-time streaming responses for chat completions
- **Auto Model Download**: Automatic model downloading from Hugging Face Hub
- **High Performance**: Built on .NET 9 with optimized concurrent model management

## 📋 Supported Models

### Chat Models (LLamaSharp)
- **llama-3.2-3b-instruct**: Meta's Llama 3.2 3B model optimized for chat
- **llama-3.2-1b-instruct**: Smaller, faster Llama 3.2 1B model
- **llama-3.2-3b-instruct-turbo**: Performance-optimized 3B model
- **phi-3-mini-instruct**: Microsoft's Phi-3 Mini for instruction following
- **gemma-2-2b-instruct**: Google's Gemma 2 2B model

### Audio Models (Whisper.net)
- **whisper-tiny**: Fastest, smallest model (~39 MB)
- **whisper-base**: Good balance of speed and accuracy (~74 MB)
- **whisper-small**: Better accuracy (~244 MB)
- **whisper-medium**: High accuracy (~769 MB)
- **whisper-large-v1**: Best accuracy (~1550 MB)
- **whisper-large-v2**: Improved large model (~1550 MB)
- **whisper-large-v3**: Latest large model (~1550 MB)

## 🛠 Installation

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [FFmpeg](https://ffmpeg.org/download.html) (for audio conversion)
- [Docker](https://docker.com) (optional, for containerized deployment)
- NVIDIA GPU with CUDA support (optional, for GPU acceleration)

### Local Development

1. **Clone the repository**
   ```bash
   git clone https://github.com/your-username/SharpAI.git
   cd SharpAI
   ```

2. **Restore dependencies**
   ```bash
   dotnet restore
   ```

3. **Build the project**
   ```bash
   dotnet build
   ```

4. **Run the application**
   ```bash
   dotnet run
   ```

The API will be available at `http://localhost:5000` (or the port specified in your configuration).

### Docker Deployment

#### CPU Version
```bash
docker build -t sharpai:cpu -f Dockerfile .
docker run -d -p 5000:80 -v $(pwd)/Data:/app/Data sharpai:cpu
```

#### CUDA 12 Version (Recommended for GPU acceleration)
```bash
docker build -t sharpai:cuda12 -f Dockerfile.cuda12 .
docker run -d -p 5000:80 -v $(pwd)/Data:/app/Data --gpus all sharpai:cuda12
```

#### CUDA 11 Version
```bash
docker build -t sharpai:cuda11 -f Dockerfile.cuda11 .
docker run -d -p 5000:80 -v $(pwd)/Data:/app/Data --gpus all sharpai:cuda11
```

## 🔧 Configuration

### Model Configuration

Models are automatically configured but can be customized by editing `Data/models-config.json` and `Data/whisper-models-config.json`.

### Environment Variables

- `ASPNETCORE_ENVIRONMENT`: Set to `Development` for development mode
- `ASPNETCORE_URLS`: Configure listening URLs (default: `http://+:80`)

## 📖 API Usage

### Chat Completions

```bash
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama-3.2-3b-instruct",
    "messages": [
      {"role": "user", "content": "Hello, how are you?"}
    ],
    "max_tokens": 150,
    "temperature": 0.7
  }'
```

### Audio Transcription

```bash
curl -X POST http://localhost:5000/v1/audio/transcriptions \
  -H "Content-Type: multipart/form-data" \
  -F "file=@audio.mp3" \
  -F "model=whisper-base"
```

### List Available Models

```bash
# List all models
curl http://localhost:5000/v1/models

# List chat models only
curl http://localhost:5000/v1/models/chat

# List audio models only
curl http://localhost:5000/v1/models/audio
```

### Function Calling

```bash
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama-3.2-3b-instruct",
    "messages": [
      {"role": "user", "content": "What is the weather like in Paris?"}
    ],
    "tools": [
      {
        "type": "function",
        "function": {
          "name": "get_weather",
          "description": "Get weather information for a city",
          "parameters": {
            "type": "object",
            "properties": {
              "city": {"type": "string", "description": "The city name"}
            },
            "required": ["city"]
          }
        }
      }
    ]
  }'
```

## 🏗 Architecture

SharpAI is built with a modular architecture:

- **Controllers**: Handle HTTP requests and responses
  - `ChatController`: Chat completions endpoint
  - `AudioController`: Audio transcription endpoint
  - `ModelsController`: Model listing and information
  
- **Services**: Core business logic
  - `LLamaModelManager`: Manages chat models and downloads
  - `WhisperModelManager`: Manages audio models and downloads
  - `LLamaSharpService`: Chat completion processing
  - `WhisperService`: Audio transcription processing
  
- **Models**: Data transfer objects and configurations
  - OpenAI-compatible request/response models
  - Model configuration classes
  - Enum converters for JSON serialization

## 🔍 Monitoring & Logging

SharpAI includes comprehensive logging:

- Model download progress
- Request processing metrics
- Error tracking and debugging
- Performance monitoring

Logs are output to the console and can be configured through standard .NET logging configuration.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🆘 Support

- 📖 Check the [Documentation](Documentation/) folder for detailed guides
- 🐛 Report issues on [GitHub Issues](https://github.com/your-username/SharpAI/issues)
- 💬 Join our community discussions

## 🙏 Acknowledgments

- [LLamaSharp](https://github.com/SciSharp/LLamaSharp) - .NET bindings for llama.cpp
- [Whisper.net](https://github.com/sandrohanea/whisper.net) - .NET bindings for OpenAI Whisper
- [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore) - .NET wrapper for FFmpeg
- [OpenAI](https://openai.com) - API specification and inspiration

---

**Made with ❤️ and .NET 9**