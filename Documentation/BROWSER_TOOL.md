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

The browser tool requires a valid Brave Search API key to function. If no API key is configured, the browser tool will return an error message informing the user that the search functionality is not available. The LLM will then work with this error message and can inform the user about the configuration requirement.

For production use, it's essential to configure a real API key to enable web browsing capabilities.

## Browser Tool Usage

The browser tool provides five main functions:

1. **search** - Search the web for information and return formatted results
2. **searchjson** - Search the web for information and return raw JSON data
3. **open** - Open web pages and navigate to specific content
4. **openjson** - Open web pages and return structured JSON data with metadata
5. **find** - Search for patterns within opened pages

The LLM can use these tools naturally through the chat interface. When browser functions are called, they are handled internally and the results are incorporated into the response automatically.

### Example Usage

User: "Search for the latest news about artificial intelligence"

The LLM will automatically call the browser.search function and incorporate the results into its response, providing up-to-date information from the web.

User: "Search for AI news and give me the raw data"

The LLM can use browser.searchjson to get structured JSON data from the search results, which is useful for programmatic processing or when you need the exact search metadata, URLs, and structured content.

User: "Open that first link and give me the structured data"

The LLM can use browser.openjson to open a web page and return structured JSON data that includes the page content, metadata like cursor, URL, title, line information, and content statistics. This is particularly useful when you need programmatic access to the page data.