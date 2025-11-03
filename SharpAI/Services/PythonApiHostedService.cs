using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;

namespace SharpAI.Services
{
    public class PythonApiHostedService : BackgroundService
    {
        private readonly ILogger<PythonApiHostedService> _logger;
        private readonly IConfiguration _configuration;
        private Process? _pythonProcess;
        private readonly HttpClient _httpClient;
        private bool _isHealthy = false;
        
        // Configuration settings
        private readonly string _pythonExecutable;
        private readonly string _apiScriptPath;
        private readonly string _host;
        private readonly int _port;
        private readonly TimeSpan _startupTimeout;
        private readonly TimeSpan _healthCheckInterval;

        public PythonApiHostedService(
            ILogger<PythonApiHostedService> logger, 
            IConfiguration configuration,
            HttpClient httpClient)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClient;
            
            // Load configuration
            _pythonExecutable = _configuration["ChatterboxApi:PythonExecutable"] ?? "python3";
            _host = _configuration["ChatterboxApi:Host"] ?? "127.0.0.1";
            _port = int.Parse(_configuration["ChatterboxApi:Port"] ?? "8000");
            _startupTimeout = TimeSpan.FromSeconds(int.Parse(_configuration["ChatterboxApi:StartupTimeoutSeconds"] ?? "60"));
            _healthCheckInterval = TimeSpan.FromSeconds(int.Parse(_configuration["ChatterboxApi:HealthCheckIntervalSeconds"] ?? "30"));
            
            // Set the API script path
            _apiScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "runtimes", "python", "chatterbox_api.py");
        }

        public bool IsHealthy => _isHealthy;
        public string ApiBaseUrl => $"http://{_host}:{_port}";

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await StartPythonApiAsync(stoppingToken);
                await MonitorHealthAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("PythonApiHostedService stopping due to cancellation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in PythonApiHostedService");
            }
            finally
            {
                await StopPythonApiAsync();
            }
        }

        private async Task StartPythonApiAsync(CancellationToken stoppingToken)
        {
            if (!File.Exists(_apiScriptPath))
            {
                throw new FileNotFoundException($"Python API script not found: {_apiScriptPath}");
            }

            _logger.LogInformation("Starting Python Chatterbox API process...");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = _pythonExecutable,
                Arguments = $"\"{_apiScriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_apiScriptPath)
            };

            // Set environment variables for the Python process
            startInfo.Environment["CHATTERBOX_HOST"] = _host;
            startInfo.Environment["CHATTERBOX_PORT"] = _port.ToString();
            startInfo.Environment["CHATTERBOX_DEVICE"] = _configuration["ChatterboxApi:Device"] ?? "cuda";

            try
            {
                _pythonProcess = Process.Start(startInfo);
                
                if (_pythonProcess == null)
                {
                    throw new InvalidOperationException("Failed to start Python process");
                }

                _logger.LogInformation($"Python API process started with PID {_pythonProcess.Id}");

                // Start monitoring output
                _ = Task.Run(() => MonitorProcessOutput(_pythonProcess, stoppingToken), stoppingToken);

                // Wait for the API to become available
                await WaitForApiStartupAsync(stoppingToken);
                
                _logger.LogInformation($"Python Chatterbox API is ready at {ApiBaseUrl}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Python API process");
                throw;
            }
        }

        private async Task WaitForApiStartupAsync(CancellationToken stoppingToken)
        {
            var startTime = DateTime.UtcNow;
            var healthUrl = $"{ApiBaseUrl}/health";
            
            _logger.LogInformation($"Waiting for API to become available at {healthUrl}...");
            
            while (DateTime.UtcNow - startTime < _startupTimeout && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var response = await _httpClient.GetAsync(healthUrl, stoppingToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync(stoppingToken);
                        var healthData = JsonSerializer.Deserialize<JsonElement>(content);
                        
                        if (healthData.TryGetProperty("status", out var status) && 
                            status.GetString() == "healthy")
                        {
                            _isHealthy = true;
                            _logger.LogInformation("Python API health check passed");
                            return;
                        }
                    }
                }
                catch (HttpRequestException)
                {
                    // API not ready yet, continue waiting
                }
                catch (TaskCanceledException)
                {
                    // Timeout or cancellation, continue waiting
                }

                await Task.Delay(1000, stoppingToken); // Wait 1 second before retrying
            }

            if (stoppingToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Startup cancelled");
            }
            
            throw new TimeoutException($"Python API failed to start within {_startupTimeout.TotalSeconds} seconds");
        }

        private async Task MonitorHealthAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting health monitoring for Python API");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_healthCheckInterval, stoppingToken);
                    
                    if (_pythonProcess?.HasExited == true)
                    {
                        _isHealthy = false;
                        _logger.LogError($"Python API process has exited with code {_pythonProcess.ExitCode}");
                        
                        // Try to restart the process
                        await RestartPythonApiAsync(stoppingToken);
                    }
                    else
                    {
                        // Check API health endpoint
                        await CheckApiHealthAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during health monitoring");
                    _isHealthy = false;
                }
            }
        }

        private async Task CheckApiHealthAsync(CancellationToken stoppingToken)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{ApiBaseUrl}/health", stoppingToken);
                _isHealthy = response.IsSuccessStatusCode;
                
                if (!_isHealthy)
                {
                    _logger.LogWarning($"API health check failed with status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _isHealthy = false;
                _logger.LogWarning(ex, "API health check failed");
            }
        }

        private async Task RestartPythonApiAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Attempting to restart Python API process...");
            
            try
            {
                await StopPythonApiAsync();
                await Task.Delay(2000, stoppingToken); // Wait before restart
                await StartPythonApiAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart Python API process");
            }
        }

        private void MonitorProcessOutput(Process process, CancellationToken stoppingToken)
        {
            try
            {
                // Monitor stdout
                _ = Task.Run(async () =>
                {
                    while (!process.HasExited && !stoppingToken.IsCancellationRequested)
                    {
                        var line = await process.StandardOutput.ReadLineAsync();
                        if (line != null)
                        {
                            _logger.LogInformation($"[Python API] {line}");
                        }
                    }
                }, stoppingToken);

                // Monitor stderr
                _ = Task.Run(async () =>
                {
                    while (!process.HasExited && !stoppingToken.IsCancellationRequested)
                    {
                        var line = await process.StandardError.ReadLineAsync();
                        if (line != null)
                        {
                            _logger.LogWarning($"[Python API Error] {line}");
                        }
                    }
                }, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error monitoring Python process output");
            }
        }

        private async Task StopPythonApiAsync()
        {
            if (_pythonProcess != null && !_pythonProcess.HasExited)
            {
                _logger.LogInformation("Stopping Python API process...");
                
                try
                {
                    // Try graceful shutdown first
                    _pythonProcess.CloseMainWindow();
                    
                    // Wait for graceful shutdown
                    if (!_pythonProcess.WaitForExit(5000))
                    {
                        _logger.LogWarning("Python process did not exit gracefully, forcing termination");
                        _pythonProcess.Kill();
                        _pythonProcess.WaitForExit(2000);
                    }
                    
                    _logger.LogInformation("Python API process stopped");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping Python process");
                }
                finally
                {
                    _pythonProcess?.Dispose();
                    _pythonProcess = null;
                    _isHealthy = false;
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PythonApiHostedService stopping...");
            await StopPythonApiAsync();
            await base.StopAsync(cancellationToken);
        }

        public override void Dispose()
        {
            _pythonProcess?.Dispose();
            _httpClient?.Dispose();
            base.Dispose();
        }
    }
}