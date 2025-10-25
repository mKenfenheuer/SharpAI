# Development Guide

This guide provides instructions for setting up a development environment, contributing to SharpAI, and understanding the development workflow.

## Development Environment Setup

### Prerequisites

- **.NET 9.0 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Git** - Version control
- **Visual Studio 2022** or **VS Code** - IDE
- **Docker Desktop** (optional) - For containerized development
- **NVIDIA CUDA Toolkit** (optional) - For GPU development

### Quick Start

1. **Clone the repository:**
   ```bash
   git clone <repository-url>
   cd sharpai
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

3. **Build the solution:**
   ```bash
   dotnet build
   ```

4. **Run the application:**
   ```bash
   cd SharpAI
   dotnet run
   ```

5. **Verify installation:**
   ```bash
   curl http://localhost:5000/v1/models
   ```

### IDE Setup

#### Visual Studio 2022

1. Open `SharpAI.sln`
2. Set `SharpAI` as the startup project
3. Configure launch settings in `launchSettings.json`
4. Install recommended extensions:
   - C# Dev Kit
   - Docker

#### VS Code

1. Open the root directory
2. Install recommended extensions:
   - C# Dev Kit
   - Docker
   - REST Client (for API testing)
3. Use the integrated terminal for dotnet commands

### Environment Configuration

Create a `appsettings.Development.json` file in the `SharpAI` directory:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Warning",
      "System.Net.Http.HttpClient": "Information",
      "SharpAI": "Debug"
    }
  },
  "AllowedHosts": "*",
  "BraveSearch": {
    "ApiKey": "your-development-api-key",
    "BaseUrl": "https://api.search.brave.com/res/v1"
  }
}
```

## Project Structure

```
SharpAI/
├── SharpAI/                    # Main web application
│   ├── Api/                    # API controllers
│   │   ├── Audio/             # Audio transcription endpoints
│   │   ├── Chat/              # Chat completion endpoints
│   │   └── Models/            # Model management endpoints
│   ├── Services/              # Business logic services
│   │   ├── Harmony/           # Advanced AI orchestration
│   │   ├── Browser/           # Web browsing capabilities
│   │   └── *.cs              # Core AI services
│   ├── Models/                # Data transfer objects
│   ├── Data/                  # Data access layer
│   └── Program.cs             # Application entry point
├── LLamaSharp/                # LLamaSharp submodule
├── Documentation/             # Project documentation
├── bin/                       # Build outputs
├── obj/                       # Build intermediates
└── *.sln                      # Solution files
```

## Development Workflow

### Git Workflow

We follow a modified GitFlow workflow:

1. **Main branches:**
   - `main` - Production-ready code
   - `develop` - Integration branch for features

2. **Feature branches:**
   - Create from `develop`
   - Naming: `feature/description-of-feature`
   - Merge back to `develop` via PR

3. **Bug fix branches:**
   - Create from `main` or `develop`
   - Naming: `bugfix/description-of-fix`
   - Merge back to originating branch via PR

4. **Release branches:**
   - Create from `develop`
   - Naming: `release/version-number`
   - Merge to both `main` and `develop`

### Commit Message Format

Follow conventional commit format:

```
type(scope): description

[optional body]

[optional footer]
```

**Types:**
- `feat`: New features
- `fix`: Bug fixes
- `docs`: Documentation changes
- `style`: Code style changes
- `refactor`: Code refactoring
- `test`: Test additions/modifications
- `chore`: Maintenance tasks

**Example:**
```
feat(api): add streaming support for audio transcription

Implement streaming audio transcription to reduce latency
for real-time applications.

Closes #123
```

### Pull Request Process

1. **Create a feature branch:**
   ```bash
   git checkout develop
   git pull origin develop
   git checkout -b feature/new-feature
   ```

2. **Make changes and commit:**
   ```bash
   git add .
   git commit -m "feat(scope): description"
   ```

3. **Push branch and create PR:**
   ```bash
   git push origin feature/new-feature
   ```

4. **PR Requirements:**
   - Clear description of changes
   - Tests for new functionality
   - Updated documentation
   - Code review approval
   - All CI checks passing

## Testing

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test LLama.Unittest/

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test
dotnet test --filter "TestMethodName"
```

### Writing Tests

#### Unit Tests

Create tests in the `LLama.Unittest` project:

```csharp
using Xunit;
using SharpAI.Services;

public class LLamaSharpServiceTests
{
    [Fact]
    public async Task GenerateChatCompletionAsync_ShouldReturnValidResponse()
    {
        // Arrange
        var service = new LLamaSharpService(/* dependencies */);
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages = new[] { new ChatMessage { Role = MessageRole.User, Content = "Hello" } }
        };

        // Act
        var response = await service.GenerateChatCompletionAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Choices);
    }
}
```

#### Integration Tests

Create integration tests for API endpoints:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http;
using Xunit;

public class ChatControllerIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ChatControllerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostChatCompletion_ShouldReturn200()
    {
        // Arrange
        var request = new
        {
            model = "test",
            messages = new[] { new { role = "user", content = "Hello" } }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        response.EnsureSuccessStatusCode();
    }
}
```

### Test Coverage

Maintain high test coverage:

- Unit tests for all services and utilities
- Integration tests for API endpoints
- End-to-end tests for critical workflows

## Code Style and Standards

### C# Coding Standards

- Follow Microsoft's C# Coding Conventions
- Use EditorConfig for consistent formatting
- Enable nullable reference types
- Use explicit access modifiers
- Prefer dependency injection over static dependencies

### Naming Conventions

- **Classes**: PascalCase (`ChatController`)
- **Methods**: PascalCase (`GenerateChatCompletionAsync`)
- **Properties**: PascalCase (`Model`)
- **Fields**: camelCase with underscore (`_logger`)
- **Parameters**: camelCase (`request`)
- **Constants**: PascalCase (`MaxRetries`)

### Documentation Standards

- Use XML documentation for public APIs
- Include parameter descriptions
- Document exceptions that may be thrown
- Provide usage examples for complex methods

```csharp
/// <summary>
/// Generates a chat completion for the provided request.
/// </summary>
/// <param name="request">The chat completion request containing messages and configuration.</param>
/// <returns>A task representing the asynchronous operation, containing the chat completion response.</returns>
/// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
/// <exception cref="InvalidOperationException">Thrown when no model is available.</exception>
public async Task<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request)
{
    // Implementation
}
```

## Debugging

### Local Debugging

1. **Set breakpoints** in Visual Studio or VS Code
2. **Run in debug mode:**
   ```bash
   dotnet run --launch-profile "Development"
   ```
3. **Use logging for tracing:**
   ```csharp
   _logger.LogDebug("Processing request for model: {Model}", request.Model);
   ```

### Docker Debugging

1. **Build debug image:**
   ```bash
   docker build -t sharpai-debug --target build .
   ```

2. **Run with debugging:**
   ```bash
   docker run -p 5000:5000 -e ASPNETCORE_ENVIRONMENT=Development sharpai-debug
   ```

3. **Attach debugger** using VS Code's Remote-Containers extension

### Performance Profiling

Use dotnet diagnostic tools:

```bash
# Install global tools
dotnet tool install --global dotnet-counters
dotnet tool install --global dotnet-trace

# Monitor performance counters
dotnet-counters monitor --process-id <pid>

# Collect traces
dotnet-trace collect --process-id <pid>
```

## API Testing

### Using REST Client (VS Code)

Create `test.http` file:

```http
### Test Models Endpoint
GET http://localhost:5000/v1/models

### Test Chat Completion
POST http://localhost:5000/v1/chat/completions
Content-Type: application/json

{
  "model": "default",
  "messages": [
    {"role": "user", "content": "Hello, how are you?"}
  ],
  "stream": false
}

### Test Audio Transcription
POST http://localhost:5000/v1/audio/transcriptions
Content-Type: multipart/form-data; boundary=boundary

--boundary
Content-Disposition: form-data; name="file"; filename="test.mp3"
Content-Type: audio/mpeg

< ./test-files/audio.mp3
--boundary
Content-Disposition: form-data; name="model"

whisper-1
--boundary--
```

### Using cURL

```bash
# Test chat completion
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "default",
    "messages": [{"role": "user", "content": "Hello"}]
  }'

# Test streaming
curl -X POST http://localhost:5000/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "default",
    "messages": [{"role": "user", "content": "Tell me a story"}],
    "stream": true
  }'
```

## Contributing New Features

### Adding a New API Endpoint

1. **Create controller method:**
   ```csharp
   [HttpPost("new-endpoint")]
   public async Task<IActionResult> NewEndpoint([FromBody] NewRequest request)
   {
       // Implementation
   }
   ```

2. **Add service method:**
   ```csharp
   public async Task<NewResponse> ProcessNewRequestAsync(NewRequest request)
   {
       // Business logic
   }
   ```

3. **Create models:**
   ```csharp
   public class NewRequest
   {
       public string? Property { get; set; }
   }

   public class NewResponse
   {
       public string? Result { get; set; }
   }
   ```

4. **Add tests:**
   ```csharp
   [Fact]
   public async Task NewEndpoint_ShouldReturnValidResponse()
   {
       // Test implementation
   }
   ```

5. **Update documentation:**
   - Add to API.md
   - Update README.md if needed

### Adding a New Service

1. **Create service interface:**
   ```csharp
   public interface INewService
   {
       Task<Result> ProcessAsync(Input input);
   }
   ```

2. **Implement service:**
   ```csharp
   public class NewService : INewService
   {
       private readonly ILogger<NewService> _logger;

       public NewService(ILogger<NewService> logger)
       {
           _logger = logger;
       }

       public async Task<Result> ProcessAsync(Input input)
       {
           // Implementation
       }
   }
   ```

3. **Register in DI container:**
   ```csharp
   builder.Services.AddSingleton<INewService, NewService>();
   ```

4. **Add tests and documentation**

## Performance Guidelines

### Memory Management

- Dispose of unmanaged resources properly
- Use `using` statements for disposables
- Avoid memory leaks in event handlers
- Monitor memory usage during development

### Asynchronous Programming

- Use `async`/`await` for I/O operations
- Don't block on async operations
- Use `ConfigureAwait(false)` in library code
- Prefer `IAsyncEnumerable` for streaming

### Error Handling

- Use structured exception handling
- Log errors with context
- Return appropriate HTTP status codes
- Don't expose sensitive information in error messages

## Release Process

### Version Management

Use semantic versioning (SemVer):
- **Major**: Breaking changes
- **Minor**: New features, backwards compatible
- **Patch**: Bug fixes

### Creating a Release

1. **Update version numbers:**
   ```xml
   <PropertyGroup>
     <Version>1.2.0</Version>
   </PropertyGroup>
   ```

2. **Update CHANGELOG.md**

3. **Create release branch:**
   ```bash
   git checkout develop
   git checkout -b release/1.2.0
   ```

4. **Test thoroughly**

5. **Merge to main and tag:**
   ```bash
   git checkout main
   git merge release/1.2.0
   git tag v1.2.0
   git push origin main --tags
   ```

6. **Merge back to develop:**
   ```bash
   git checkout develop
   git merge main
   git push origin develop
   ```

## Getting Help

### Documentation

- Read the [Architecture](Architecture.md) documentation
- Check the [API](API.md) reference
- Review the [Deployment](Deployment.md) guide

### Community

- Create GitHub issues for bugs or feature requests
- Join discussions in GitHub Discussions
- Check existing issues before creating new ones

### Debugging Issues

1. Check logs for error messages
2. Verify environment configuration
3. Test with minimal reproduction case
4. Include relevant information in bug reports:
   - OS and .NET version
   - Steps to reproduce
   - Expected vs actual behavior
   - Relevant logs or error messages

This development guide should help you get started contributing to SharpAI. Remember to follow the coding standards, write tests, and update documentation for any changes you make.