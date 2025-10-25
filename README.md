# SharpAI

A powerful .NET 9 AI inference server providing OpenAI-compatible APIs for chat completions, audio transcription, and model management. Built with LLamaSharp for local LLM inference and Whisper.NET for speech-to-text capabilities.

## 🚀 Features

- **OpenAI-Compatible API**: Drop-in replacement for OpenAI's chat completions and audio APIs
- **Local LLM Inference**: Run large language models locally using LLamaSharp
- **Audio Transcription**: High-quality speech-to-text using Whisper models
- **Streaming Support**: Real-time streaming responses for chat completions
- **Browser Integration**: Built-in web search capabilities with Brave Search API
- **Tool Calling**: Advanced function calling with recursive tool execution
- **Docker Support**: Easy deployment with Docker containers
- **High Performance**: Built on .NET 9 with unsafe code optimizations

## 📋 Requirements

- .NET 9.0 SDK or later
- Docker (optional, for containerized deployment)
- Compatible GPU (optional, for CUDA acceleration)

## 🛠️ Installation

### From Source

1. **Clone the repository**:
   ```bash
   git clone <repository-url>
   cd sharpai
   ```

2. **Build the project**:
   ```bash
   dotnet build
   ```

3. **Run the application**:
   ```bash
   cd SharpAI
   dotnet run
   ```

### Using Docker

1. **Build the Docker image**:
   ```bash
   docker build -t sharpai .
   ```

2. **Run the container**:
   ```bash
   docker run -p 80:80 sharpai
   ```

For CUDA support, use the CUDA Dockerfile:
```bash
docker build -f Dockerfile.cuda -t sharpai-cuda .
```

## 🚀 Quick Start

### 1. Start the Server

```bash
cd SharpAI
dotnet run
```

The server will start on `http://localhost:5000` (or the configured port).

### 2. Chat Completions

Send a chat completion request:

```bash
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "default",
    "messages": [
      {"role": "user", "content": "Hello, how are you?"}
    ],
    "stream": false
  }'
```

### 3. Audio Transcription

Transcribe an audio file:

```bash
curl -X POST http://localhost:5000/v1/audio/transcriptions \
  -F "file=@audio.mp3" \
  -F "model=whisper-1"
```

### 4. Streaming Chat

For real-time streaming responses:

```bash
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

## 📚 API Documentation

SharpAI provides OpenAI-compatible APIs:

### Chat Completions
- **POST** `/v1/chat/completions` - Create chat completions
- Supports streaming and non-streaming responses
- Compatible with OpenAI's chat completions format

### Audio
- **POST** `/v1/audio/transcriptions` - Transcribe audio to text
- Supports multiple audio formats (MP3, WAV, M4A, etc.)
- Uses Whisper models for high-quality transcription

### Models
- **GET** `/v1/models` - List available models
- Returns information about loaded LLM and Whisper models

For detailed API documentation, see [Documentation/API.md](Documentation/API.md).

## ⚙️ Configuration

Configure the application using `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "BraveSearch": {
    "ApiKey": "your-brave-search-api-key",
    "BaseUrl": "https://api.search.brave.com/res/v1"
  }
}
```

### Environment Variables

- `ASPNETCORE_URLS`: Set the URLs the server listens on
- `ASPNETCORE_ENVIRONMENT`: Set to `Development` for development mode

## 🏗️ Architecture

SharpAI is built with a modular architecture:

- **API Layer**: OpenAI-compatible REST controllers
- **Service Layer**: Core business logic and AI model management
- **Harmony System**: Advanced tool calling and browser integration
- **Model Managers**: LLM and Whisper model lifecycle management

Key components:

- `LLamaSharpService`: Core LLM inference engine
- `WhisperService`: Audio transcription service
- `StreamingInferenceEngine`: Handles streaming responses with tool integration
- `BrowserService`: Web search and browsing capabilities

For detailed architecture documentation, see [Documentation/Architecture.md](Documentation/Architecture.md).

## 🧪 Development

### Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022 or VS Code
- Git

### Building from Source

1. Clone the repository
2. Open the solution in Visual Studio or VS Code
3. Restore NuGet packages: `dotnet restore`
4. Build the solution: `dotnet build`
5. Run tests: `dotnet test`

### Project Structure

```
SharpAI/
├── Api/                    # REST API controllers
│   ├── Audio/             # Audio transcription endpoints
│   ├── Chat/              # Chat completion endpoints
│   └── Models/            # Model management endpoints
├── Services/              # Core business logic
│   ├── Harmony/           # Tool calling and browser integration
│   ├── Browser/           # Web search services
│   └── *.cs              # Model managers and core services
├── Models/                # Data transfer objects
└── Data/                  # Data access and persistence
```

For development guidelines, see [Documentation/Development.md](Documentation/Development.md).

## 🐳 Deployment

### Docker Deployment

1. **Standard deployment**:
   ```bash
   docker build -t sharpai .
   docker run -p 80:80 -v /path/to/models:/app/models sharpai
   ```

2. **CUDA-enabled deployment**:
   ```bash
   docker build -f Dockerfile.cuda -t sharpai-cuda .
   docker run --gpus all -p 80:80 sharpai-cuda
   ```

### Production Considerations

- Mount a volume for model storage to persist downloaded models
- Configure appropriate memory limits for large language models
- Use reverse proxy (nginx) for SSL termination and load balancing
- Monitor GPU memory usage when using CUDA acceleration

For detailed deployment instructions, see [Documentation/Deployment.md](Documentation/Deployment.md).

## 🤝 Contributing

We welcome contributions! Please see our [Contributing Guide](Documentation/Development.md) for details on:

- Code style and conventions
- Setting up the development environment
- Submitting pull requests
- Reporting issues

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [LLamaSharp](https://github.com/SciSharp/LLamaSharp) - .NET bindings for llama.cpp
- [Whisper.NET](https://github.com/sandrohanea/whisper.net) - .NET bindings for OpenAI Whisper
- [llama.cpp](https://github.com/ggerganov/llama.cpp) - Inference of LLaMA model in pure C/C++

## 📞 Support

- Create an issue for bug reports or feature requests
- Check the [FAQ](Documentation/FAQ.md) for common questions
- Review the [API documentation](Documentation/API.md) for usage details

---

Built with ❤️ using .NET 9 and modern AI technologies.