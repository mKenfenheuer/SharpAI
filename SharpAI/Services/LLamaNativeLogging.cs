using LLama;
using LLama.Common;
using LLama.Native;
using SharpAI.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SharpAI.Services
{
    public class LLamaNativeLogging : IDisposable
    {
        private readonly NativeLogConfig.LLamaLogCallback _logCallback;
        private readonly ILogger<LLamaNativeLogging> _logger;

        public LLamaNativeLogging(ILogger<LLamaNativeLogging> logger)
        {
            _logger = logger;
            _logCallback = new NativeLogConfig.LLamaLogCallback(OnLog);
            NativeLogConfig.llama_log_set(_logCallback);
        }

        public void Dispose()
        {
            NativeLogConfig.llama_log_set(logCallback: null);
        }

        private void OnLog(LLamaLogLevel level, string message)
        {
            switch (level)
            {
                case LLamaLogLevel.Debug:
                    _logger.LogDebug(message);
                    break;
                case LLamaLogLevel.Info:
                    _logger.LogInformation(message);
                    break;
                case LLamaLogLevel.Warning:
                    _logger.LogWarning(message);
                    break;
                case LLamaLogLevel.Error:
                    _logger.LogError(message);
                    break;
                default:
                    _logger.LogInformation(message);
                    break;
            }
        }
    }
}
