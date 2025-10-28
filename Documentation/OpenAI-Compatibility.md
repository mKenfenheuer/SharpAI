# OpenAI API Compatibility

SharpAI provides an **OpenAI-compatible API with extensions** for enhanced transparency and insight into the model's reasoning process.

## 🔄 OpenAI Compatibility

### ✅ Fully Compatible Endpoints

- **POST /v1/chat/completions** - Complete compatibility with OpenAI's Chat Completions API
- All standard OpenAI parameters supported: `model`, `messages`, `temperature`, `max_tokens`, `stream`, etc.
- Standard response format with `id`, `object`, `created`, `model`, `choices`, `usage`
- OpenAI-compatible streaming with Server-Sent Events

### ✅ Standard Fields Supported

#### Request Fields
- `model` - Model identifier
- `messages` - Array of chat messages with standard roles (system, user, assistant, tool)
- `temperature` - Sampling temperature (0-2)
- `max_tokens` - Maximum tokens to generate
- `stream` - Enable streaming responses
- `tools` - Function calling support
- `tool_choice` - Tool selection control
- All other standard OpenAI parameters

#### Response Fields
- `id` - Unique completion identifier
- `object` - Response object type
- `created` - Unix timestamp
- `model` - Model used
- `choices` - Array of completion choices
- `usage` - Token usage statistics
- `finish_reason` - Completion reason

#### Message Fields
- `role` - Message role (system, user, assistant, tool)
- `content` - Message content
- `name` - Optional participant name
- `tool_call_id` - Tool response identifier
- `tool_calls` - Function calls made by model

## 🚀 SharpAI Extensions

### Enhanced Message Fields

#### `reasoning` Field
```json
{
  "role": "assistant",
  "content": "The answer is 42.",
  "reasoning": "I need to think about this step by step. First, I consider the context of the question..."
}
```

- **Purpose**: Contains chain-of-thought reasoning content
- **Source**: Harmony library's analysis channel
- **Streaming**: Available in delta chunks for real-time reasoning visibility
- **Compatibility**: Ignored by standard OpenAI clients

#### `commentary` Field
```json
{
  "role": "assistant", 
  "content": "The answer is 42.",
  "commentary": "The user seems to be asking about the meaning of life. I should provide a thoughtful response that acknowledges both the reference and the deeper question."
}
```

- **Purpose**: Meta-commentary about the response process
- **Source**: Harmony library's commentary channel  
- **Streaming**: Available in delta chunks for process transparency
- **Compatibility**: Ignored by standard OpenAI clients

### Multi-Channel Support

SharpAI processes responses through multiple channels:

1. **Final Channel** → `content` field (OpenAI standard)
2. **Analysis Channel** → `reasoning` field (SharpAI extension)
3. **Commentary Channel** → `commentary` field (SharpAI extension)

## 📡 Streaming Extensions

### Standard OpenAI Streaming
```json
{"choices":[{"delta":{"content":"Hello"}}]}
{"choices":[{"delta":{"content":" world"}}]}
```

### Extended SharpAI Streaming
```json
{"choices":[{"delta":{"content":"Hello","reasoning":"I should greet the user","commentary":"Starting with a friendly greeting"}}]}
{"choices":[{"delta":{"content":" world","reasoning":" in a warm manner","commentary":" to establish rapport"}}]}
```

## 🔧 Usage Examples

### With Standard OpenAI Client
```python
import openai

# Works exactly like OpenAI
client = openai.OpenAI(base_url="http://localhost:5000/v1")
response = client.chat.completions.create(
    model="llama-3.1-8b",
    messages=[{"role": "user", "content": "Hello"}]
)
print(response.choices[0].message.content)
# Extended fields are silently ignored
```

### With Extended SharpAI Client
```python
import requests

response = requests.post("http://localhost:5000/v1/chat/completions", json={
    "model": "llama-3.1-8b",
    "messages": [{"role": "user", "content": "Explain quantum computing"}]
})

data = response.json()
message = data["choices"][0]["message"]

print("Content:", message["content"])      # Main response
print("Reasoning:", message["reasoning"])  # Chain of thought
print("Commentary:", message["commentary"]) # Meta-commentary
```

### Streaming with Extensions
```python
import requests

response = requests.post("http://localhost:5000/v1/chat/completions", json={
    "model": "llama-3.1-8b", 
    "messages": [{"role": "user", "content": "Think through this problem"}],
    "stream": True
}, stream=True)

for line in response.iter_lines():
    if line.startswith(b"data: "):
        data = json.loads(line[6:])
        delta = data["choices"][0]["delta"]
        
        if "content" in delta:
            print("Content:", delta["content"], end="")
        if "reasoning" in delta:
            print("\nReasoning:", delta["reasoning"], end="")
        if "commentary" in delta:
            print("\nCommentary:", delta["commentary"], end="")
```

## 🎯 Migration from OpenAI

### Zero-Code Migration
Replace the base URL in your existing OpenAI client:

```python
# Before
client = openai.OpenAI()

# After  
client = openai.OpenAI(base_url="http://localhost:5000/v1")
```

All existing code continues to work unchanged. Extended fields are optional additions.

### Gradual Enhancement
Add support for extended fields when needed:

```python
def get_completion_with_reasoning(prompt):
    response = client.chat.completions.create(
        model="llama-3.1-8b",
        messages=[{"role": "user", "content": prompt}]
    )
    
    message = response.choices[0].message
    return {
        "content": message.content,
        "reasoning": getattr(message, "reasoning", None),
        "commentary": getattr(message, "commentary", None)
    }
```

## 📋 Compatibility Matrix

| Feature | OpenAI Standard | SharpAI Support | Notes |
|---------|----------------|-----------------|--------|
| Basic chat completions | ✅ | ✅ | Full compatibility |
| Streaming responses | ✅ | ✅ | SSE with delta structure |
| Function calling | ✅ | ✅ | Complete tool support |
| System messages | ✅ | ✅ | Standard behavior |
| Temperature control | ✅ | ✅ | 0-2 range supported |
| Token limits | ✅ | ✅ | Configurable limits |
| Multiple choices | ✅ | ✅ | N parameter supported |
| Reasoning field | ❌ | ✅ | SharpAI extension |
| Commentary field | ❌ | ✅ | SharpAI extension |
| Multi-channel processing | ❌ | ✅ | Harmony integration |

## 🔍 Benefits

### For OpenAI Users
- **Zero migration effort** - Drop-in replacement
- **Same familiar API** - No learning curve
- **Optional enhancements** - Use extensions when needed

### For Advanced Users  
- **Reasoning transparency** - See how the model thinks
- **Process insights** - Understand decision-making
- **Real-time streaming** - Watch reasoning unfold
- **Multi-channel processing** - Separate content types

## 🚨 Important Notes

1. **Backward Compatibility**: All OpenAI clients work without modification
2. **Forward Compatibility**: Extended fields are optional and safely ignored
3. **Standard Compliance**: Core API matches OpenAI specification exactly
4. **Token Counting**: Extended fields included in usage statistics
5. **Performance**: Extensions add minimal overhead

## 📚 Related Documentation

- [OpenAI Chat Completions API](https://platform.openai.com/docs/api-reference/chat)
- [Harmony Library Documentation](../Architecture.md#harmony-integration)
- [Streaming Implementation](../Development.md#streaming-support)
- [Function Calling Guide](../API.md#function-calling)