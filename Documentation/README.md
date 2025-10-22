# SharpAI Documentation

Welcome to the SharpAI documentation! This directory contains comprehensive guides for using, configuring, and deploying SharpAI.

## 📚 Documentation Index

### 🚀 Getting Started
- **[Getting Started Guide](GETTING_STARTED.md)** - Quick start guide with examples and basic usage
- **[API Documentation](API.md)** - Complete API reference with all endpoints and examples

### ⚙️ Configuration & Deployment  
- **[Configuration Guide](CONFIGURATION.md)** - Model configuration, server settings, and performance tuning
- **[Deployment Guide](DEPLOYMENT.md)** - Docker deployment, cloud deployment, and production setup

### 🔧 Technical Guides
- **[Dynamic Models](DYNAMIC_MODELS.md)** - Dynamic chat model selection and management
- **[Dynamic Whisper Models](DYNAMIC_WHISPER_MODELS.md)** - Dynamic audio model selection and configuration
- **[Audio Conversion](AUDIO_CONVERSION.md)** - Audio format handling and conversion

## 🗂️ Quick Navigation

### For New Users
1. Start with **[Getting Started](GETTING_STARTED.md)** for a quick overview
2. Try the examples to understand basic functionality
3. Read **[API Documentation](API.md)** for detailed endpoint usage

### For Developers  
1. Check **[API Documentation](API.md)** for integration details
2. Review **[Configuration Guide](CONFIGURATION.md)** for customization options
3. See **[Dynamic Models](DYNAMIC_MODELS.md)** for model management

### For DevOps/Deployment
1. Read **[Deployment Guide](DEPLOYMENT.md)** for production setup
2. Check **[Configuration Guide](CONFIGURATION.md)** for server optimization
3. Review Docker configurations and scaling options

## 🎯 Common Use Cases

### Chat Applications
- **Models**: Use `llama-3.2-3b-instruct` for general chat, `phi-3-mini-instruct` for coding assistance
- **Configuration**: Set appropriate `context_size` and `gpu_layers` for your hardware
- **Integration**: Use OpenAI SDK with custom base URL

### Audio Transcription
- **Models**: `whisper-base` for most use cases, `whisper-large-v3` for maximum accuracy
- **Formats**: Supports MP3, WAV, M4A, WebM, and other common formats
- **Processing**: Automatic conversion to WAV 16kHz for optimal results

### Function Calling
- **Implementation**: OpenAI-compatible tool/function calling
- **Models**: Works best with instruction-tuned models
- **Usage**: Define functions with JSON schema for structured responses

## 📖 Documentation Structure

```
Documentation/
├── README.md                    # This file - documentation index
├── GETTING_STARTED.md          # Quick start guide with examples
├── API.md                      # Complete API reference
├── CONFIGURATION.md            # Configuration and tuning guide
├── DEPLOYMENT.md               # Deployment and production guide
├── DYNAMIC_MODELS.md           # Chat model management
├── DYNAMIC_WHISPER_MODELS.md   # Audio model management
└── AUDIO_CONVERSION.md         # Audio processing details
```

## 🔍 Finding Information

### Search by Topic

| Topic | Primary Document | Secondary Resources |
|-------|------------------|-------------------|
| **Installation** | [Getting Started](GETTING_STARTED.md) | [Deployment](DEPLOYMENT.md) |
| **API Usage** | [API Documentation](API.md) | [Getting Started](GETTING_STARTED.md) |
| **Model Setup** | [Configuration](CONFIGURATION.md) | [Dynamic Models](DYNAMIC_MODELS.md) |
| **Docker** | [Deployment](DEPLOYMENT.md) | [Configuration](CONFIGURATION.md) |
| **Performance** | [Configuration](CONFIGURATION.md) | [Deployment](DEPLOYMENT.md) |
| **Audio Processing** | [Audio Conversion](AUDIO_CONVERSION.md) | [API Documentation](API.md) |

### Search by Experience Level

#### Beginner 🌱
- Start with [Getting Started](GETTING_STARTED.md)
- Focus on basic examples and default configurations
- Use Docker for easiest setup

#### Intermediate 🌿  
- Deep dive into [API Documentation](API.md)
- Customize models with [Configuration Guide](CONFIGURATION.md)
- Explore function calling and streaming

#### Advanced 🌳
- Optimize performance with [Configuration](CONFIGURATION.md)
- Deploy to production with [Deployment Guide](DEPLOYMENT.md)
- Customize model selection and scaling

## 💡 Tips for Using This Documentation

### Code Examples
- All code examples are tested and ready to use
- Replace `localhost:5000` with your actual server address
- API keys are not required (yet) - use any placeholder value

### Configuration Snippets
- JSON configuration examples can be copied directly
- Environment variables are documented with their defaults
- Docker commands include all necessary flags

### Troubleshooting
- Each guide includes a troubleshooting section
- Common issues are documented with solutions
- Log analysis tips are provided for debugging

## 🚀 Quick Links

### Essential Commands

**Start SharpAI (Docker)**
```bash
docker run -d --name sharpai -p 5000:80 -v $(pwd)/data:/app/Data --gpus all sharpai:cuda12
```

**Test Installation**
```bash
curl http://localhost:5000/v1/models
```

**Simple Chat**
```bash
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"llama-3.2-3b-instruct","messages":[{"role":"user","content":"Hello!"}]}'
```

**Audio Transcription**
```bash
curl -X POST http://localhost:5000/v1/audio/transcriptions \
  -F "file=@audio.mp3" -F "model=whisper-base"
```

### Important Files

- `Data/models-config.json` - Chat model configuration
- `Data/whisper-models-config.json` - Audio model configuration  
- `appsettings.json` - Server configuration
- `Dockerfile.cuda12` - GPU-enabled Docker image

## 🆘 Getting Help

### Self-Help Resources
1. **Search this documentation** for your specific use case
2. **Check the troubleshooting sections** in each guide
3. **Review the examples** in the `Test/` directory
4. **Examine the log output** for error details

### Community Support
- **GitHub Issues**: Report bugs and feature requests
- **Discussions**: Ask questions and share experiences
- **Wiki**: Community-contributed guides and tips

### Contributing to Documentation
We welcome improvements to the documentation! Please:
1. Fork the repository
2. Update the relevant markdown files
3. Test any code examples
4. Submit a pull request

## 📝 Documentation Standards

This documentation follows these principles:
- **Practical**: Every example can be copy-pasted and used
- **Progressive**: Builds from basic to advanced concepts
- **Cross-referenced**: Links between related topics
- **Current**: Updated with each release
- **Tested**: All code examples are verified

---

**Need something specific?** Use the search function in your editor to find keywords across all documentation files.

**Found an issue?** Please report documentation bugs alongside code issues - good docs are part of good software!

**Ready to start?** Head to the [Getting Started Guide](GETTING_STARTED.md) to begin your SharpAI journey! 🚀