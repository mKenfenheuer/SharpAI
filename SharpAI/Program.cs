
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;

namespace SharpAI;

/// <summary>
/// The main entry point for the SharpAI application.
/// Configures and starts the web host with all required services for OpenAI-compatible API endpoints.
/// </summary>
public class Program
{
    /// <summary>
    /// The main entry point for the application. Configures services, middleware, and starts the web host.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                // The enum converters are already configured in the individual enum declarations
                // with [JsonConverter(typeof(LowercaseEnumConverter<T>))]
            });
        builder.Services.AddAuthorization();

        // Register services
        builder.Services.AddHttpClient();
        
        builder.Services.AddSingleton<SharpAI.Services.WhisperModelManager>();
    
        // Register llama and whisper hosted services
        builder.Services.AddSingleton<SharpAI.Services.WhisperHostedService>();
        builder.Services.AddHostedService<SharpAI.Services.WhisperHostedService>(provider => 
            provider.GetRequiredService<SharpAI.Services.WhisperHostedService>());

        // Configure form options for large file uploads
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 1024L * 1024L * 1024L * 10; // 10 GB
            options.ValueLengthLimit = int.MaxValue;
            options.ValueCountLimit = int.MaxValue;
            options.KeyLengthLimit = int.MaxValue;
        });

        // Allow large multipart form uploads (e.g., up to 10 GB)
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Limits.MaxRequestBodySize = 1024L * 1024L * 1024L * 10L; // 10 GB
        });

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}
