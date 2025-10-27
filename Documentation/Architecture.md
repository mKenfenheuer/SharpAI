# Architecture Documentation

SharpAI is built with a modular, layered architecture designed for scalability, maintainability, and high performance. The system provides OpenAI-compatible APIs while leveraging local AI models for inference. This document outlines the system architecture, key components, and design patterns used throughout the application.

## Key Features

- **OpenAI API Compatibility**: Drop-in replacement for OpenAI's chat completions and audio APIs
- **Local Model Inference**: Uses LLamaSharp and Whisper.NET for on-premises AI inference
- **Advanced Tool Calling**: Sophisticated tool integration with the Harmony format system
- **Browser Integration**: Built-in web search and content retrieval capabilities
- **Streaming Support**: Real-time streaming responses for chat completions
- **High Performance**: Built on .NET 9 with optimized native libraries

## System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                     Client Applications                    │
│              (OpenAI SDKs, HTTP Clients)                   │
└─────────────────────────┬───────────────────────────────────┘
                          │ HTTP/REST API
┌─────────────────────────▼───────────────────────────────────┐
│                      API Layer                             │
│    ┌─────────────┐ ┌─────────────┐ ┌─────────────────┐    │
│    │    Chat     │ │    Audio    │ │     Models      │    │
│    │ Controller  │ │ Controller  │ │   Controller    │    │
│    └─────────────┘ └─────────────┘ └─────────────────┘    │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                   Service Layer                            │
│  ┌──────────────┐ ┌──────────────┐ ┌─────────────────────┐ │
│  │  LLamaSharp  │ │   Whisper    │ │  Model Managers     │ │
│  │   Service    │ │   Service    │ │                     │ │
│  └──────────────┘ └──────────────┘ └─────────────────────┘ │
│                                                             │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │              Harmony System                             │ │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────────┐  │ │
│  │  │ Streaming   │ │   Harmony   │ │    Browser      │  │ │
│  │  │ Inference   │ │ Parser &    │ │ Tool Handler    │  │ │
│  │  │  Engine     │ │ Formatter   │ │                 │  │ │
│  │  └─────────────┘ └─────────────┘ └─────────────────┘  │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                 Native Libraries                           │
│    ┌─────────────┐ ┌─────────────┐ ┌─────────────────┐    │
│    │  llama.cpp  │ │ Whisper.NET │ │   FFmpeg Core   │    │
│    │ (LLamaSharp)│ │             │ │                 │    │
│    └─────────────┘ └─────────────┘ └─────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

## Core Components

### API Layer

The API layer provides OpenAI-compatible REST endpoints using ASP.NET Core controllers.

#### Controllers

- **`ChatController`**: Handles chat completion requests (`/v1/chat/completions`)
- **`AudioController`**: Manages audio transcription (`/v1/audio/transcriptions`)
- **`ModelsController`**: Lists available models (`/v1/models`)

**Key Features:**
- OpenAI API compatibility
- Streaming and non-streaming responses
- Request validation and error handling
- Large file upload support (100MB limit)

### Service Layer

The service layer contains the core business logic and orchestrates AI model operations.

#### Core Services

**`LLamaSharpService`**
- Primary service for LLM operations
- Manages chat completion generation
- Coordinates with Harmony system for advanced features
- Handles both streaming and non-streaming responses

```csharp
public class LLamaSharpService : IDisposable
{
    private readonly LLamaModelManager _modelManager;
    private readonly StreamingInferenceEngine _streamingEngine;
    private readonly HarmonyParser _harmonyParser;
    private readonly HarmonyFormatter _harmonyFormatter;
    
    public async Task<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request)
    public async IAsyncEnumerable<ChatCompletionChunk> GenerateChatCompletionStreamAsync(ChatCompletionRequest request)
}
```

**`WhisperService`**
- Handles audio transcription using Whisper models
- Supports multiple audio formats
- Provides audio format conversion
- Manages different response formats (JSON, text, SRT, VTT)

**Model Managers**
- **`LLamaModelManager`**: Manages LLM lifecycle (loading, unloading, caching)
- **`WhisperModelManager`**: Handles Whisper model management

### Harmony System

The Harmony system is SharpAI's advanced AI orchestration framework that enables sophisticated tool calling and multi-modal interactions. **SharpAI now uses the external Openai.Harmony library** for improved parsing and rendering capabilities.

#### Components

**`StreamingInferenceEngine`**
- Core engine for streaming inference with tool integration
- Handles recursive tool calling
- Manages conversation state during streaming
- Coordinates between LLM and external tools

```csharp
public class StreamingInferenceEngine
{
    public async IAsyncEnumerable<ChatCompletionChunk> ProcessStreamingInferenceAsync(
        ILLamaExecutor executor, 
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
}
```

**`HarmonyFormatter`** (Updated)
- Converts OpenAI-style messages to Harmony conversation format using the Openai.Harmony library
- Leverages the library's `HarmonyEncoding` for tokenization and rendering
- Handles tool definitions and response format specifications
- Supports reasoning effort levels
- Provides both library-based and legacy formatting methods

**`HarmonyParser`** (Updated)
- Parses model responses using the Openai.Harmony library's `StreamableParser`
- Supports both token-based and text-based parsing for compatibility
- Extracts tool calls and function arguments from responses
- Handles streaming response parsing with proper state management
- Falls back to legacy parsing when needed

**`BrowserToolHandler`**
- Implements web browsing capabilities
- Integrates with Brave Search API
- Handles web page content extraction
- Provides web navigation automation

#### Harmony Library Integration

The refactored system now integrates with the **Openai.Harmony library** which provides:

- **Native tokenization**: Direct access to the Harmony encoding for precise token-level operations
- **Streaming parser**: Real-time parsing of streaming responses with proper state management
- **Conversation rendering**: Optimized rendering of conversations for completion
- **Robust parsing**: Handles complex message structures and tool calls
- **Performance optimizations**: Efficient token encoding/decoding operations

**Key Benefits:**
- More accurate parsing of Harmony format responses
- Better handling of streaming content
- Improved tool call detection and extraction
- Standardized conversation format handling
- Enhanced performance through native implementation

#### Tool Calling Flow

```
1. User Request → HarmonyFormatter → Formatted Prompt
2. Formatted Prompt → LLM → Harmony Response
3. Harmony Response → HarmonyParser → Tool Calls
4. Tool Calls → BrowserToolHandler → Tool Results
5. Tool Results → HarmonyFormatter → Enhanced Prompt
6. Enhanced Prompt → LLM → Final Response
```

### Browser Integration

SharpAI includes sophisticated web browsing capabilities through the Browser service layer.

**`BrowserService`**
- Core web browsing functionality
- HTTP client management
- Content extraction and parsing
- Search result processing

**Features:**
- Web search via Brave Search API
- Content extraction from web pages
- Automated navigation
- Result summarization and filtering

## Data Flow

### Chat Completion Flow

```
1. HTTP Request → ChatController
2. Request Validation → ChatCompletionRequest
3. Model Selection → LLamaModelManager
4. Prompt Formatting → HarmonyFormatter
5. Inference → StreamingInferenceEngine
6. Tool Detection → HarmonyParser
7. Tool Execution → BrowserToolHandler (if needed)
8. Response Generation → LLM
9. Response Formatting → ChatCompletionResponse
10. HTTP Response → Client
```

### Audio Transcription Flow

```
1. HTTP Request (Multipart) → AudioController
2. File Validation → Audio Format Check
3. Audio Conversion → FFmpeg (WAV 16kHz)
4. Model Loading → WhisperModelManager
5. Transcription → Whisper.NET
6. Response Formatting → AudioTranscriptionResponse
7. HTTP Response → Client
```

## Design Patterns

### Dependency Injection

SharpAI uses .NET's built-in dependency injection container for managing service lifecycles:

```csharp
// Singleton services for expensive resources
builder.Services.AddSingleton<LLamaModelManager>();
builder.Services.AddSingleton<WhisperModelManager>();
builder.Services.AddSingleton<StreamingInferenceEngine>();

// Scoped services for request-specific operations
builder.Services.AddScoped<LLamaSharpService>();
```

### Repository Pattern

Model managers implement a repository-like pattern for AI model management:

- Centralized model loading and caching
- Lifecycle management (load on demand, unload when not needed)
- Thread-safe access to shared resources

### Strategy Pattern

Different response formats and processing strategies are implemented using the strategy pattern:

- Audio response formats (JSON, text, SRT, VTT)
- Streaming vs. non-streaming responses
- Different reasoning effort levels

### Observer Pattern

Streaming responses use an observer-like pattern with async enumerables:

```csharp
await foreach (var chunk in _llamaService.GenerateChatCompletionStreamAsync(request))
{
    await Response.WriteAsync($"data: {json}\n\n");
    await Response.Body.FlushAsync();
}
```

## Performance Considerations

### Memory Management

- **Model Caching**: Models are loaded once and cached in memory
- **Streaming**: Large responses are streamed to minimize memory usage
- **Unsafe Code**: Performance-critical sections use unsafe code for efficiency
- **Object Pooling**: Reuse of expensive objects where possible

### Concurrency

- **Thread Safety**: All services are designed to be thread-safe
- **Async/Await**: Extensive use of async patterns for I/O operations
- **Cancellation Tokens**: Proper cancellation support throughout the pipeline

### Resource Optimization

- **Lazy Loading**: Models are loaded on first use
- **Connection Pooling**: HTTP clients use connection pooling
- **Memory Limits**: Configurable limits for large file uploads

## Configuration and Environment

### Dependency Injection Configuration

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // Core services
    services.AddSingleton<LLamaModelManager>();
    services.AddSingleton<WhisperModelManager>();
    
    // Harmony system
    services.AddSingleton<HarmonyFormatter>();
    services.AddSingleton<HarmonyParser>();
    services.AddSingleton<StreamingInferenceEngine>();
    
    // Browser integration
    services.AddSingleton<BrowserService>();
    services.AddSingleton<BrowserToolHandler>();
    
    // Main services
    services.AddSingleton<LLamaSharpService>();
    services.AddSingleton<WhisperService>();
}
```

### Environment Configuration

The application supports multiple environments with different configurations:

- **Development**: Enhanced logging, development-specific settings
- **Production**: Optimized for performance and security
- **Docker**: Container-specific configurations

## Security Considerations

### Input Validation

- File type validation for audio uploads
- Request size limits
- Content-type verification
- Model name validation

### Resource Protection

- Memory limits for file uploads
- Request timeout configurations
- Rate limiting capabilities (framework-ready)

### Error Handling

- Centralized exception handling
- Sanitized error responses
- Comprehensive logging without sensitive data exposure

## Extensibility

### Adding New Models

1. Implement model-specific loading logic in model managers
2. Add model metadata to the models endpoint
3. Update prompt formatting if needed

### Adding New Tools

1. Create tool handler class implementing the tool interface
2. Register the tool in the dependency injection container
3. Add tool definition to the Harmony formatter
4. Implement tool parsing logic

### Adding New Response Formats

1. Define new response format enum
2. Implement formatting logic in the appropriate service
3. Add parsing logic for the new format
4. Update API documentation

The architecture is designed to be flexible and extensible, allowing for easy addition of new features, models, and integrations while maintaining performance and reliability.