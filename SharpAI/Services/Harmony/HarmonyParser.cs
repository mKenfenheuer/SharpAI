using SharpAI.Models;
using System.Text.Json;
using System.Text;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Parses Harmony format responses from language models using only the Openai.Harmony library.
/// This version removes all legacy/fallback parsing and uses the library exclusively.
/// </summary>
public class HarmonyParser : IDisposable
{
    private readonly ILogger<HarmonyParser> _logger;
    public List<HarmonyMessage> _messages { get; private set; } = new List<HarmonyMessage>();
    private HarmonyMessage _currentMessage = new HarmonyMessage();
    private HarmonyParserState _parserState = new HarmonyParserState();
    private StringBuilder _rawContent = new StringBuilder();
    private HarmonyAssistantContent _currentAssistantContent = new HarmonyAssistantContent();

    // Track previous content lengths for delta calculations
    private readonly Dictionary<HarmonyChannel, int> _previousContentLengths = new Dictionary<HarmonyChannel, int>
    {
        { HarmonyChannel.Final, 0 },
        { HarmonyChannel.Analysis, 0 },
        { HarmonyChannel.Commentary, 0 }
    };

    public bool IsComplete { get; internal set; }
    public bool IsFailed { get; private set; }

    public bool IsInFinalContent
    {
        get
        {
            return _currentMessage != null &&
                   _currentMessage.Role == HarmonyRole.Assistant &&
                   (_parserState.CurrentChannel == HarmonyChannel.Final) &&
                   _parserState.ParseState == HarmonyParseState.InMessageContent;
        }
    }
    public string ToolCallRecipient
    {
        get
        {
            return IsInToolCallArguments ? _parserState.CurrentRecipient : null;
        }
    }
    public bool IsInToolCallArguments
    {
        get
        {
            return _currentMessage != null &&
                   _currentMessage.Role == HarmonyRole.Assistant &&
                   !string.IsNullOrEmpty(_parserState.CurrentRecipient) &&
                   _parserState.ParseState == HarmonyParseState.InMessageContent;
        }
    }
    public int ReasoningTokens { get; internal set; } = 0;
    public int CompletionTokens { get; internal set; } = 0;
    public bool HasConstraint
    {
        get
        {
            return _parserState.ConstraintType != null || _parserState.ConstraintTypeText.Length > 0 || _parserState.ParseState == HarmonyParseState.InConstraint;
        }
    }

    public string RawResponse => _rawContent.ToString();

    /// <summary>
    /// Initializes a new instance of the HarmonyParser.
    /// </summary>
    /// <param name="logger">Logger for parser operations</param>
    public HarmonyParser(ILogger<HarmonyParser> logger)
    {
        _logger = logger;
    }

    private void StartNewMessage()
    {
        FinishCurrentMessage();
    }

    public void ResetParserState()
    {
        _parserState = new HarmonyParserState();
        _currentMessage = new HarmonyMessage();
        _currentAssistantContent = new HarmonyAssistantContent();
        _rawContent.Clear();
        _messages.Clear();
        IsComplete = false;
        IsFailed = false;

        // Reset content length tracking
        _previousContentLengths[HarmonyChannel.Final] = 0;
        _previousContentLengths[HarmonyChannel.Analysis] = 0;
        _previousContentLengths[HarmonyChannel.Commentary] = 0;
    }

    /// <summary>
    /// Parses new response tokens using the Harmony library's streaming parser.
    /// </summary>
    /// <param name="token">New token to process</param>
    /// <returns>Delta containing changes in this parsing step</returns>
    public HarmonyDelta ParseToken(string token, bool addToRawResponse = true)
    {
        if (addToRawResponse)
            _rawContent.Append(token);

        // Handle special tokens according to Harmony format
        switch (token)
        {
            case var t when t == HarmonyFormattingToken.Start.AsString():
                // Beginning of a message - reset state and prepare for role parsing
                _parserState.ParseState = HarmonyParseState.WaitingForRole;
                _parserState.CurrentRole = null;
                _parserState.CurrentChannel = null;
                _parserState.CurrentRecipient = null;
                _parserState.HasSentToolCallMetadata = false; // Reset tool call metadata flag
                _parserState.RoleHeaderText.Clear(); // Reset role header accumulator
                break;

            case var t when t == HarmonyFormattingToken.Message.AsString():
                // Transition from header to content
                // Parse accumulated role header text if we haven't processed it yet
                if (_parserState.RoleHeaderText.Length > 0 && _parserState.CurrentRole == null)
                {
                    ParseRole(_parserState.RoleHeaderText.ToString());
                }

                // Parse accumulated channel header text before transitioning
                if (_parserState.ChannelHeaderText.Length > 0)
                {
                    ParseChannelAndRecipient(_parserState.ChannelHeaderText.ToString());
                }

                // If we're in constraint state, finalize the constraint type
                if (_parserState.ParseState == HarmonyParseState.InConstraint)
                {
                    FinalizeConstraintTypeIfPending();
                }

                _parserState.ParseState = HarmonyParseState.InMessageContent;

                // Initialize content based on role
                InitializeMessageContent();

                // If we have a current channel for assistant content, set it
                if (_parserState.CurrentChannel != null && _currentMessage.Role == HarmonyRole.Assistant)
                {
                    _currentAssistantContent.Channel = _parserState.CurrentChannel.Value;
                }

                // Return delta with role and channel information
                return new HarmonyDelta
                {
                    Role = _parserState.CurrentRole,
                    Channel = _parserState.CurrentChannel
                };

            case var t when t == HarmonyFormattingToken.Channel.AsString():
                if (_parserState.ParseState == HarmonyParseState.WaitingForChannel)
                {
                    // We have already seen the channel token, this message is not a valid message. Fail with error.
                    _logger.LogError("Received channel token while waiting for channel.");
                    IsComplete = true;
                    IsFailed = true;
                    break;
                }
                // Transition to channel information in header
                // Parse accumulated role header text before transitioning
                if (_parserState.RoleHeaderText.Length > 0)
                {
                    ParseRole(_parserState.RoleHeaderText.ToString());
                }

                _parserState.ParseState = HarmonyParseState.WaitingForChannel;
                _parserState.ChannelHeaderText.Clear(); // Reset channel header accumulator
                break;

            case var t when t == HarmonyFormattingToken.EndMessage.AsString():
                // End of message - mark as complete and start new message
                if ((_parserState.ParseState == HarmonyParseState.InMessageContent && _parserState.CurrentChannel == HarmonyChannel.Final) || IsInToolCallArguments)
                {
                    IsComplete = true;
                }
                if (_parserState.CurrentRecipient != null && _currentMessage.Role == HarmonyRole.Assistant)
                {
                    CreateToolCallFromCurrentState();
                }
                FinishCurrentMessage();
                return new HarmonyDelta { IsComplete = true };

            case var t when t == HarmonyFormattingToken.EndMessageAssistantToTool.AsString():
                // Tool call stop token - finalize any pending constraint type and create tool call
                FinalizeConstraintTypeIfPending();
                CreateToolCallFromCurrentState();
                _currentMessage.IsComplete = true;
                FinishCurrentMessage();
                IsComplete = true;
                return new HarmonyDelta
                {
                    IsComplete = true,
                    ToolCall = _currentAssistantContent.ToolCall
                };

            case var t when t == HarmonyFormattingToken.EndMessageDoneSampling.AsString():
                // Return stop token - finalize any pending constraint type and mark as complete
                FinalizeConstraintTypeIfPending();
                _currentMessage.IsComplete = true;
                FinishCurrentMessage();
                IsComplete = true;
                return new HarmonyDelta { IsComplete = true };

            case var t when t == HarmonyFormattingToken.ConstrainedFormat.AsString():
                // Data type definition in tool call - prepare to collect constraint type and arguments
                _parserState.ParseState = HarmonyParseState.InConstraint;
                _parserState.ToolCallArguments.Clear(); // Reset arguments collection
                _parserState.ConstraintTypeText.Clear(); // Reset constraint type accumulator
                break;

            default:
                // Regular content token - handle based on current state
                return HandleContentToken(token);
        }

        return new HarmonyDelta();
    }

    private void FinishCurrentMessage()
    {
        if (_currentMessage != null)
        {
            _messages.Add(_currentMessage);
            _currentMessage = new HarmonyMessage();
            _currentAssistantContent = new HarmonyAssistantContent();
            _parserState = new HarmonyParserState();
        }
    }

    public void SetConstraintType(string constraintType)
    {
        // we can only set constraint type if we are in final content and no content has been sent yet

        var raw = _rawContent.ToString();
        if (IsInFinalContent && raw.EndsWith(HarmonyFormattingToken.Message.AsString()))
        {
            _parserState.ConstraintType = constraintType;
            raw = raw.Substring(0, raw.Length - HarmonyFormattingToken.Message.AsString().Length) +
                  $"{HarmonyFormattingToken.ConstrainedFormat.AsString()}{constraintType}{HarmonyFormattingToken.Message.AsString()}";
            _rawContent.Clear();
            _rawContent.Append(raw);
        }
    }

    private HarmonyDelta HandleContentToken(string token)
    {
        if (_parserState.CurrentChannel == HarmonyChannel.Final && _currentMessage.Role == HarmonyRole.Assistant)
        {
            CompletionTokens++;
        }
        else if ((_parserState.CurrentChannel == HarmonyChannel.Analysis && _currentMessage.Role == HarmonyRole.Assistant) ||
                 (_parserState.CurrentChannel == HarmonyChannel.Commentary && _currentMessage.Role == HarmonyRole.Assistant))
        {
            ReasoningTokens++;
        }
        else
        {
            // Other tokens are not counted towards reasoning or completion
        }

        var delta = new HarmonyDelta();

        switch (_parserState.ParseState)
        {
            case HarmonyParseState.WaitingForRole:
                // Accumulate role tokens
                _parserState.RoleHeaderText.Append(token);
                break;

            case HarmonyParseState.WaitingForChannel:
                // Parse channel from token, may include recipient syntax
                _parserState.ChannelHeaderText.Append(token);
                break;

            case HarmonyParseState.InMessageContent:
                // Add content to current channel or message and calculate delta
                if (_parserState.CurrentChannel != null && _currentMessage.Role == HarmonyRole.Assistant)
                {
                    if (_currentAssistantContent.Content == null)
                    {
                        _currentAssistantContent.Content = string.Empty;
                    }
                    _currentAssistantContent.Content += token;

                    // Set delta properties based on channel
                    delta.Channel = _parserState.CurrentChannel;
                    delta.Role = _currentMessage.Role;
                    delta.Content = token;

                    // Check if we're in a tool call scenario (recipient is set)
                    if (!string.IsNullOrEmpty(_parserState.CurrentRecipient))
                    {
                        // This content is tool call arguments
                        if (!_parserState.HasSentToolCallMetadata)
                        {
                            // First token for this tool call - include metadata
                            _parserState.ToolCallId = Guid.NewGuid().ToString();
                            delta.ToolCall = new HarmonyToolCall
                            {
                                Id = _parserState.ToolCallId,
                                Recipient = _parserState.CurrentRecipient,
                                ToolName = ExtractToolNameFromRecipient(_parserState.CurrentRecipient),
                                Namespace = ExtractNamespaceFromRecipient(_parserState.CurrentRecipient),
                                Arguments = token, // This is the first argument token
                                ConstraintType = _parserState.ConstraintType
                            };
                            _parserState.HasSentToolCallMetadata = true;
                        }
                        else
                        {
                            // Subsequent tokens - only include arguments
                            delta.ToolCall = new HarmonyToolCall
                            {
                                Recipient = _parserState.CurrentRecipient,
                                Arguments = token // Only the incremental argument token
                            };
                        }
                    }
                    else
                    {
                        // Regular content, map to appropriate channel
                        switch (_parserState.CurrentChannel)
                        {
                            case HarmonyChannel.Final:
                                delta.FinalContent = token;
                                break;
                            case HarmonyChannel.Analysis:
                                delta.ReasoningContent = token;
                                break;
                            case HarmonyChannel.Commentary:
                                delta.CommentaryContent = token;
                                break;
                        }
                    }
                }
                else if (_currentMessage.Role != HarmonyRole.Assistant)
                {
                    // For non-assistant messages, add to text content
                    var textContent = _currentMessage.Content.OfType<HarmonyTextContent>().FirstOrDefault();
                    if (textContent != null)
                    {
                        textContent.Text += token;
                        delta.Content = token;
                        delta.Role = _currentMessage.Role;
                    }
                }
                break;

            case HarmonyParseState.InConstraint:
                // Handle constraint content for tool calls - only collect constraint type
                // Arguments will come as regular message content after <|message|> token
                if (string.IsNullOrEmpty(_parserState.ConstraintType))
                {
                    // Only accumulate constraint type tokens (e.g., "json")
                    // The <|message|> token will end this state and arguments follow as content
                    _parserState.ConstraintTypeText.Append(token);
                }
                // Note: We don't handle arguments here anymore, they come as message content
                break;

            case HarmonyParseState.WaitingForStart:
            default:
                // Ignore tokens when not in a valid state
                break;
        }

        return delta;
    }

    private void InitializeMessageContent()
    {
        if (_currentMessage == null)
        {
            _currentMessage = new HarmonyMessage();
        }

        if (!_currentMessage.Content.Any())
        {
            switch (_currentMessage.Role)
            {
                case HarmonyRole.Assistant:
                    _currentMessage.Content.Add(_currentAssistantContent);
                    break;
                case HarmonyRole.System:
                    _currentMessage.Content.Add(new HarmonySystemContent());
                    break;
                case HarmonyRole.Developer:
                    _currentMessage.Content.Add(new HarmonyDeveloperContent());
                    break;
                default:
                    _currentMessage.Content.Add(new HarmonyTextContent());
                    break;
            }
        }
    }

    private void ParseChannelAndRecipient(string token)
    {
        // Parse channel and potential recipient from token like "commentary to=functions.get_current_weather"
        var parts = token.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
        {
            // First part is the channel
            if (TryParseChannel(parts[0], out var channel))
            {
                _parserState.CurrentChannel = channel;
            }

            // Look for "to=recipient" in remaining parts
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("to="))
                {
                    _parserState.CurrentRecipient = parts[i].Substring(3); // Remove "to=" prefix
                    break;
                }
            }
        }
    }

    private void ParseRole(string roleText)
    {
        if (TryParseRole(roleText, out var role))
        {
            _currentMessage.Role = role.Value;
            _parserState.CurrentRole = role;
        }
    }

    private void FinalizeConstraintTypeIfPending()
    {
        // If we have accumulated constraint type text but haven't finalized it yet, do so now
        if (string.IsNullOrEmpty(_parserState.ConstraintType) && _parserState.ConstraintTypeText.Length > 0)
        {
            _parserState.ConstraintType = _parserState.ConstraintTypeText.ToString().Trim();
        }
    }

    private void CreateToolCallFromCurrentState()
    {
        if (_parserState.CurrentRecipient != null && _currentMessage.Role == HarmonyRole.Assistant)
        {
            var toolCall = new HarmonyToolCall
            {
                Id = _parserState.ToolCallId,
                Recipient = _parserState.CurrentRecipient,
                ToolName = ExtractToolNameFromRecipient(_parserState.CurrentRecipient),
                Namespace = ExtractNamespaceFromRecipient(_parserState.CurrentRecipient),
                Arguments = _currentAssistantContent.Content ?? string.Empty, // Use message content as arguments
                ConstraintType = _parserState.ConstraintType
            };

            _currentAssistantContent.Content = null; // Clear content as it's now in tool call

            _currentAssistantContent.ToolCall = toolCall;
        }
    }

    private string ExtractToolNameFromRecipient(string recipient)
    {
        // Extract tool name from recipient like "functions.get_current_weather"
        var lastDotIndex = recipient.IndexOf('.');
        return lastDotIndex >= 0 ? recipient.Substring(lastDotIndex + 1) : recipient;
    }

    private string ExtractNamespaceFromRecipient(string recipient)
    {
        // Extract namespace from recipient like "functions.get_current_weather" => "functions"
        // or "browser.search" => "browser"
        var firstDotIndex = recipient.IndexOf('.');
        return firstDotIndex >= 0 ? recipient.Substring(0, firstDotIndex) : recipient;
    }

    private bool TryParseRole(string roleStr, out HarmonyRole? role)
    {
        role = null;
        try
        {
            role = HarmonyRoleExtensions.FromString(roleStr.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryParseChannel(string channelStr, out HarmonyChannel? channel)
    {
        channel = null;
        try
        {
            channel = HarmonyChannelExtensions.FromString(channelStr.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    public HarmonyResponse GetResponse()
    {
        if (_currentMessage != null && !_currentMessage.IsComplete)
        {
            // Finalize any pending parsing
            if (_parserState.RoleHeaderText.Length > 0 && _parserState.CurrentRole == null)
            {
                ParseRole(_parserState.RoleHeaderText.ToString());
            }

            if (_parserState.ChannelHeaderText.Length > 0 && _parserState.CurrentChannel == null)
            {
                ParseChannelAndRecipient(_parserState.ChannelHeaderText.ToString());
            }

            FinalizeConstraintTypeIfPending();

            _currentMessage.IsComplete = true;
            if (_parserState.CurrentRecipient != null && _currentMessage.Role == HarmonyRole.Assistant)
            {
                CreateToolCallFromCurrentState();
            }
            if (_currentMessage.Content.Count > 0)
            {
                if (_parserState.CurrentChannel == null)
                {
                    _logger.LogWarning("Finalizing message without a defined channel.");
                }
                _messages.Add(_currentMessage);
            }
            _currentMessage = null;
        }
        return new HarmonyResponse { Messages = this._messages };
    }

    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        ResetParserState();
    }

    public HarmonyResponse GetPartialResponse()
    {
        var messages = new List<HarmonyMessage>();
        messages.AddRange(_messages);
        if (_currentMessage != null)
        {
            messages.Add(_currentMessage);
        }
        return new HarmonyResponse { Messages = messages };
    }

    private class HarmonyParserState
    {
        public HarmonyRole? CurrentRole { get; set; }
        public HarmonyChannel? CurrentChannel { get; set; }
        public HarmonyParseState ParseState { get; set; } = HarmonyParseState.WaitingForStart;
        public string? CurrentRecipient { get; set; }
        public string? ConstraintType { get; set; }
        public StringBuilder ToolCallArguments { get; set; } = new StringBuilder();
        public StringBuilder ChannelHeaderText { get; set; } = new StringBuilder();
        public StringBuilder RoleHeaderText { get; set; } = new StringBuilder();
        public StringBuilder ConstraintTypeText { get; set; } = new StringBuilder();
        public bool HasSentToolCallMetadata { get; set; } = false; // Track if we've sent initial tool call info
        public string ToolCallId { get; set; }
    }

    private enum HarmonyParseState
    {
        WaitingForStart,
        WaitingForRole,
        WaitingForChannel,
        InMessageContent,
        InConstraint
    }
}