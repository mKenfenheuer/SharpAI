# Browser Tool Configuration

The browser tool in SharpAI uses the Brave Search API to provide real web search capabilities. To enable this functionality, you need to configure your Brave Search API key.

## Getting a Brave Search API Key

1. Visit [Brave Search API](https://brave.com/search/api/)
2. Sign up for an account
3. Subscribe to a plan (Free plan is available and includes 2,000 queries per month)
4. Get your API key from the API Keys section

## Configuration

Add your API key to the configuration:

### For Development
Edit `appsettings.Development.json`:
```json
{
  "BraveSearch": {
    "ApiKey": "YOUR_BRAVE_SEARCH_API_KEY_HERE",
    "BaseUrl": "https://api.search.brave.com/res/v1"
  }
}
```

### For Production
Set the environment variable:
```bash
export BraveSearch__ApiKey="your_api_key_here"
```

Or edit `appsettings.json` (not recommended for production):
```json
{
  "BraveSearch": {
    "ApiKey": "your_api_key_here",
    "BaseUrl": "https://api.search.brave.com/res/v1"
  }
}
```

## Fallback Behavior

If no API key is configured, the browser tool will fall back to simulated search results for demonstration purposes. For production use, it's recommended to configure a real API key.

## Browser Tool Usage

The browser tool provides three main functions:

1. **search** - Search the web for information
2. **open** - Open web pages and navigate to specific content
3. **find** - Search for patterns within opened pages

The LLM can use these tools naturally through the chat interface. When browser functions are called, they are handled internally and the results are incorporated into the response automatically.

### Example Usage

User: "Search for the latest news about artificial intelligence"

The LLM will automatically call the browser.search function and incorporate the results into its response, providing up-to-date information from the web.