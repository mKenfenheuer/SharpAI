# Dynamic Model Selection with Hugging Face Integration

SharpAI now supports dynamic model selection with automatic downloading from Hugging Face. You can use different models for different chat completions without restarting the application.

## Features

- **Automatic Model Downloads**: Models are automatically downloaded from Hugging Face on first use
- **Model Caching**: Downloaded models are cached locally and reused
- **Multiple Models**: Support for various model sizes and types
- **Dynamic Selection**: Choose model per request using the `model` parameter

## Available Models

The following models are pre-configured and ready to use:

### Llama 3.2 Models
- **llama-3.2-3b-instruct** (1.9 GB) - Default model, good balance of quality and speed
- **llama-3.2-1b-instruct** (0.7 GB) - Smaller, faster model for simple tasks

### Microsoft Phi-3
- **phi-3-mini-instruct** (2.4 GB) - Microsoft's efficient instruction-following model

### Google Gemma 2
- **gemma-2-2b-instruct** (1.6 GB) - Google's compact instruction model with 8K context

## API Usage

### List Available Models
```http
GET /v1/models
```

### Chat Completion with Specific Model
```json
{
  "model": "llama-3.2-1b-instruct",
  "messages": [
    {"role": "user", "content": "Hello!"}
  ]
}
```

### Streaming with Different Model
```json
{
  "model": "phi-3-mini-instruct",
  "messages": [
    {"role": "user", "content": "Tell me a story"}
  ],
  "stream": true
}
```

## Model Selection Guidelines

### For General Tasks
- **llama-3.2-3b-instruct**: Best overall quality, good for complex reasoning
- **gemma-2-2b-instruct**: Good quality with longer context (8K tokens)

### For Speed/Resource Efficiency
- **llama-3.2-1b-instruct**: Fastest, lowest memory usage
- **phi-3-mini-instruct**: Good balance of speed and capability

### For Specific Use Cases
- **Long conversations**: gemma-2-2b-instruct (8K context)
- **Quick responses**: llama-3.2-1b-instruct
- **Code/Technical**: phi-3-mini-instruct
- **Creative writing**: llama-3.2-3b-instruct

## Configuration

Models are configured in `Data/models-config.json`. You can add custom models by editing this file:

```json
{
  "models": [
    {
      "name": "custom-model",
      "huggingface_id": "user/model-repo",
      "filename": "model.gguf",
      "context_size": 4096,
      "gpu_layers": 5,
      "description": "Custom model description",
      "size_gb": 2.0,
      "type": "instruct"
    }
  ]
}
```

## Storage Location

All models are downloaded to the `Data/Models/` directory and cached for future use.

## Error Handling

If you request an unavailable model, the API returns an error with available options:
```json
{
  "error": {
    "message": "Model 'invalid-model' is not available. Use /v1/models to see available models.",
    "type": "invalid_request_error"
  }
}
```

## Performance Notes

- First use of a model requires download time (varies by model size and connection speed)
- Models remain in memory once loaded for optimal performance
- GPU acceleration is enabled by default (5 layers) for compatible hardware
- Memory usage scales with the number of loaded models

## Monitoring

Model loading and download progress is logged at INFO level:
```
INFO: Downloading model llama-3.2-1b-instruct from https://huggingface.co/... (Size: ~0.7 GB)
INFO: Downloading llama-3.2-1b-instruct: 45.2% (317.4/702.1 MB)
INFO: Successfully downloaded model llama-3.2-1b-instruct
INFO: Successfully loaded model llama-3.2-1b-instruct
INFO: Using model: llama-3.2-1b-instruct
```