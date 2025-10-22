# Configuration Guide

This guide covers all configuration options for SharpAI, including model management, server settings, and performance tuning.

## 📁 Configuration Files

### Directory Structure

```
Data/
├── models-config.json          # Chat model configurations
├── whisper-models-config.json  # Audio model configurations
└── Models/                     # Downloaded model files
    ├── llama-3.2-3b-instruct-q4_k_m.gguf
    ├── whisper-base.bin
    └── ...
```

### Application Settings

SharpAI uses standard .NET configuration with `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "SharpAI.Services": "Information"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "Limits": {
      "MaxRequestBodySize": 104857600,
      "KeepAliveTimeout": "00:02:00",
      "RequestHeadersTimeout": "00:01:00"
    }
  }
}
```

---

## 🤖 Chat Model Configuration

### Default Configuration

Chat models are configured in `Data/models-config.json`. If this file doesn't exist, SharpAI uses built-in defaults:

```json
{
  "models": [
    {
      "name": "llama-3.2-3b-instruct",
      "huggingface_id": "bartowski/Llama-3.2-3B-Instruct-GGUF",
      "filename": "Llama-3.2-3B-Instruct-Q4_K_M.gguf",
      "context_size": 4096,
      "gpu_layers": 5,
      "description": "Llama 3.2 3B Instruct model optimized for chat",
      "size_gb": 2.02,
      "type": "instruct"
    },
    {
      "name": "llama-3.2-1b-instruct",
      "huggingface_id": "huggingface-hub/Llama-3.2-1B-Instruct-Q4_K_M-GGUF",
      "filename": "llama-3.2-1b-instruct-q4_k_m.gguf",
      "context_size": 4096,
      "gpu_layers": 5,
      "description": "Llama 3.2 1B Instruct model - smaller and faster",
      "size_gb": 0.7,
      "type": "instruct"
    }
  ]
}
```

### Model Parameters

| Parameter | Type | Description | Default |
|-----------|------|-------------|---------|
| `name` | string | Model identifier used in API requests | Required |
| `huggingface_id` | string | Hugging Face repository ID | Required |
| `filename` | string | Model file name in the repository | Required |
| `context_size` | integer | Maximum context length in tokens | 4096 |
| `gpu_layers` | integer | Number of layers to run on GPU | 5 |
| `description` | string | Human-readable description | "" |
| `size_gb` | number | Approximate model size in GB | 0 |
| `type` | string | Model type identifier | "instruct" |

### GPU Layer Optimization

Adjust `gpu_layers` based on your GPU memory:

| GPU VRAM | Recommended GPU Layers |
|----------|----------------------|
| 4GB | 10-15 |
| 8GB | 20-25 |
| 12GB+ | 30+ (full model) |
| CPU Only | 0 |

### Custom Model Addition

Add custom models to `Data/models-config.json`:

```json
{
  "models": [
    {
      "name": "my-custom-model",
      "huggingface_id": "organization/model-repo",
      "filename": "model-file.gguf",
      "context_size": 8192,
      "gpu_layers": 20,
      "description": "Custom fine-tuned model",
      "size_gb": 3.5,
      "type": "custom"
    }
  ]
}
```

---

## 🎤 Audio Model Configuration

### Default Configuration

Audio models are configured in `Data/whisper-models-config.json`:

```json
{
  "models": [
    {
      "name": "whisper-tiny",
      "filename": "ggml-tiny.bin",
      "description": "Tiny Whisper model - fastest, lowest accuracy",
      "size_mb": 39,
      "speed_multiplier": 32,
      "accuracy": "Low",
      "ggml_type": "Tiny"
    },
    {
      "name": "whisper-base",
      "filename": "ggml-base.bin",
      "description": "Base Whisper model - good balance of speed and accuracy",
      "size_mb": 74,
      "speed_multiplier": 16,
      "accuracy": "Good",
      "ggml_type": "Base"
    }
  ]
}
```

### Audio Model Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `name` | string | Model identifier for API requests |
| `filename` | string | Model file name |
| `description` | string | Human-readable description |
| `size_mb` | integer | Model size in megabytes |
| `speed_multiplier` | integer | Approximate speed vs real-time |
| `accuracy` | string | Relative accuracy level |
| `ggml_type` | string | Whisper.net GgmlType enum value |

### Available Whisper Models

| Model | Size | Speed | Best For |
|-------|------|-------|----------|
| **tiny** | 39MB | 32x realtime | Quick drafts, low resource environments |
| **base** | 74MB | 16x realtime | Most applications, good balance |
| **small** | 244MB | 6x realtime | Better accuracy needed |
| **medium** | 769MB | 2x realtime | High accuracy applications |
| **large-v1** | 1550MB | 1x realtime | Maximum accuracy |
| **large-v2** | 1550MB | 1x realtime | Improved large model |
| **large-v3** | 1550MB | 1x realtime | Latest and most accurate |

---

## 🔧 Server Configuration

### Kestrel Web Server

Configure Kestrel in `appsettings.json`:

```json
{
  "Kestrel": {
    "Limits": {
      "MaxRequestBodySize": 104857600,        // 100MB max upload
      "KeepAliveTimeout": "00:02:00",         // 2 minutes
      "RequestHeadersTimeout": "00:01:00",    // 1 minute
      "MaxConcurrentConnections": 100,
      "MaxConcurrentUpgradedConnections": 100
    },
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:80"
      }
    }
  }
}
```

### Form Options

File upload limits are configured in `Program.cs`:

```csharp
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100_000_000; // 100MB
    options.ValueLengthLimit = int.MaxValue;
    options.ValueCountLimit = int.MaxValue;
    options.KeyLengthLimit = int.MaxValue;
});
```

### Environment Variables

Override configuration with environment variables:

```bash
# Server configuration
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:80
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# Custom paths
SHARPAI_MODELS_PATH=/custom/models/path
SHARPAI_DATA_PATH=/custom/data/path

# Performance tuning
SHARPAI_MAX_CONCURRENT_MODELS=3
SHARPAI_GPU_MEMORY_FRACTION=0.8
```

---

## 🚀 Performance Configuration

### Model Loading

Control model loading behavior:

```json
{
  "ModelLoading": {
    "MaxConcurrentLoads": 1,
    "TimeoutMinutes": 10,
    "RetryAttempts": 3,
    "PreloadModels": ["llama-3.2-3b-instruct", "whisper-base"]
  }
}
```

### Memory Management

```json
{
  "MemoryManagement": {
    "MaxModelsInMemory": 3,
    "ModelIdleTimeoutMinutes": 30,
    "GarbageCollectionMode": "Aggressive"
  }
}
```

### Audio Processing

```json
{
  "AudioProcessing": {
    "TempDirectory": "/tmp/sharpai-audio",
    "MaxFileSizeMB": 100,
    "SupportedFormats": ["mp3", "wav", "m4a", "webm", "mp4"],
    "FFmpegPath": "/usr/bin/ffmpeg"
  }
}
```

---

## 🐳 Docker Configuration

### Build Arguments

Customize Docker builds with build arguments:

```bash
# CPU Build
docker build \
  --build-arg DOTNET_VERSION=9.0 \
  --build-arg BASE_IMAGE=mcr.microsoft.com/dotnet/aspnet:9.0 \
  -f Dockerfile .

# CUDA Build
docker build \
  --build-arg CUDA_VERSION=12.0 \
  --build-arg CUDNN_VERSION=8 \
  -f Dockerfile.cuda12 .
```

### Runtime Configuration

```bash
# Resource limits
docker run -d \
  --name sharpai \
  --memory="8g" \
  --cpus="4" \
  --shm-size="2g" \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -v $(pwd)/Data:/app/Data \
  sharpai:cuda12

# GPU configuration
docker run -d \
  --name sharpai \
  --gpus all \
  -e NVIDIA_VISIBLE_DEVICES=all \
  -e NVIDIA_DRIVER_CAPABILITIES=compute,utility \
  sharpai:cuda12
```

### Docker Compose

```yaml
version: '3.8'

services:
  sharpai:
    image: sharpai:cuda12
    container_name: sharpai
    ports:
      - "5000:80"
    volumes:
      - ./Data:/app/Data
      - ./config:/app/config:ro
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - SHARPAI_MAX_CONCURRENT_MODELS=2
    deploy:
      resources:
        limits:
          memory: 8G
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu]
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:80/v1/models"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s
```

---

## 📊 Logging Configuration

### Structured Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "SharpAI.Services.LLamaModelManager": "Information",
      "SharpAI.Services.WhisperModelManager": "Information",
      "SharpAI.Api": "Information"
    },
    "Console": {
      "IncludeScopes": true,
      "TimestampFormat": "yyyy-MM-dd HH:mm:ss.fff ",
      "LogLevel": {
        "Default": "Information"
      }
    }
  }
}
```

### Custom Log Providers

Add custom logging providers:

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFile("logs/sharpai-{Date}.log");  // Serilog example
```

### Log Categories

SharpAI uses these logging categories:

- `SharpAI.Services.LLamaModelManager`: Model loading and management
- `SharpAI.Services.WhisperModelManager`: Audio model management  
- `SharpAI.Services.LLamaSharpService`: Chat completion processing
- `SharpAI.Services.WhisperService`: Audio transcription processing
- `SharpAI.Api.Chat.ChatController`: Chat API endpoints
- `SharpAI.Api.Audio.AudioController`: Audio API endpoints

---

## 🔒 Security Configuration

### API Authentication (Future)

Prepare for future authentication features:

```json
{
  "Authentication": {
    "ApiKey": {
      "Enabled": false,
      "RequiredHeader": "Authorization",
      "ValidKeys": []
    },
    "JWT": {
      "Enabled": false,
      "SecretKey": "",
      "Issuer": "SharpAI",
      "Audience": "SharpAI-API"
    }
  }
}
```

### Rate Limiting (Future)

```json
{
  "RateLimiting": {
    "Enabled": false,
    "RequestsPerMinute": 60,
    "BurstSize": 10,
    "WindowSizeMinutes": 1
  }
}
```

### CORS Configuration

```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

---

## 🎯 Configuration Validation

### Model Configuration Validation

SharpAI validates model configurations on startup:

- ✅ Required fields present
- ✅ Valid Hugging Face repository format
- ✅ Supported file extensions
- ✅ Reasonable parameter ranges

### Health Checks

Add health checks for monitoring:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<ModelHealthCheck>("models")
    .AddCheck<DiskSpaceHealthCheck>("disk_space")
    .AddCheck<MemoryHealthCheck>("memory");
```

### Configuration Examples

#### High-Performance Setup

```json
{
  "models": [
    {
      "name": "llama-3.2-3b-turbo",
      "context_size": 8192,
      "gpu_layers": 35,
      "description": "High-performance configuration"
    }
  ],
  "ModelLoading": {
    "MaxConcurrentLoads": 2
  },
  "MemoryManagement": {
    "MaxModelsInMemory": 5
  }
}
```

#### Resource-Constrained Setup

```json
{
  "models": [
    {
      "name": "llama-3.2-1b-instruct",
      "context_size": 2048,
      "gpu_layers": 0,
      "description": "CPU-only, low resource usage"
    }
  ],
  "ModelLoading": {
    "MaxConcurrentLoads": 1
  },
  "MemoryManagement": {
    "MaxModelsInMemory": 1,
    "ModelIdleTimeoutMinutes": 10
  }
}
```

---

## 🔧 Troubleshooting Configuration

### Common Issues

**Models not downloading**
- Check `huggingface_id` format: `owner/repo-name`
- Verify internet connectivity
- Check disk space in `Data/Models/`

**High memory usage**
- Reduce `gpu_layers` count
- Lower `context_size`
- Decrease `MaxModelsInMemory`

**Slow model loading**
- Increase `gpu_layers` if GPU available
- Use faster storage (SSD)
- Increase `MaxConcurrentLoads`

### Validation Commands

```bash
# Test configuration
docker run --rm -v $(pwd)/Data:/app/Data sharpai:cpu \
  dotnet SharpAI.dll --validate-config

# Check model availability
curl http://localhost:5000/v1/models

# Test specific model
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"llama-3.2-3b-instruct","messages":[{"role":"user","content":"test"}]}'
```

This configuration guide should help you optimize SharpAI for your specific use case and hardware setup.