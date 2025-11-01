# Tool Choice Examples

## String Mode Examples

### Auto (default when tools are present)
```bash
curl -X POST http://localhost:5000/v1/chat/completions \
-H "Content-Type: application/json" \
-d '{
  "model": "default",
  "messages": [{"role": "user", "content": "What is the weather?"}],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_weather",
        "description": "Get the current weather",
        "parameters": {
          "type": "object",
          "properties": {
            "location": {"type": "string"}
          },
          "required": ["location"]
        }
      }
    }
  ],
  "tool_choice": "auto"
}'
```

### Required (force tool call)
```bash
curl -X POST http://localhost:5000/v1/chat/completions \
-H "Content-Type: application/json" \
-d '{
  "model": "default",
  "messages": [{"role": "user", "content": "What is the weather?"}],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_weather",
        "description": "Get the current weather",
        "parameters": {
          "type": "object",
          "properties": {
            "location": {"type": "string"}
          },
          "required": ["location"]
        }
      }
    }
  ],
  "tool_choice": "required"
}'
```

### None (disable tools)
```bash
curl -X POST http://localhost:5000/v1/chat/completions \
-H "Content-Type: application/json" \
-d '{
  "model": "default",
  "messages": [{"role": "user", "content": "What is the weather?"}],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_weather",
        "description": "Get the current weather",
        "parameters": {
          "type": "object",
          "properties": {
            "location": {"type": "string"}
          },
          "required": ["location"]
        }
      }
    }
  ],
  "tool_choice": "none"
}'
```

## Object Mode - Force Specific Function

```bash
curl -X POST http://localhost:5000/v1/chat/completions \
-H "Content-Type: application/json" \
-d '{
  "model": "default",
  "messages": [{"role": "user", "content": "Hello"}],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_weather",
        "description": "Get the current weather",
        "parameters": {
          "type": "object",
          "properties": {
            "location": {"type": "string"}
          },
          "required": ["location"]
        }
      }
    },
    {
      "type": "function",
      "function": {
        "name": "search_web",
        "description": "Search the web",
        "parameters": {
          "type": "object",
          "properties": {
            "query": {"type": "string"}
          },
          "required": ["query"]
        }
      }
    }
  ],
  "tool_choice": {
    "type": "function",
    "function": {
      "name": "get_weather"
    }
  }
}'
```

This forces the model to call the `get_weather` function specifically, even though `search_web` is also available.
