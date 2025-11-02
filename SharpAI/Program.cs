
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
        
        builder.Services.AddSingleton<SharpAI.Services.LLamaModelManager>();
        builder.Services.AddSingleton<SharpAI.Services.LLamaNativeLogging>();
        builder.Services.AddSingleton<SharpAI.Services.WhisperModelManager>();
        builder.Services.AddSingleton<SharpAI.Services.RequestLogger>();
        
        // Register harmony services
        builder.Services.AddScoped<SharpAI.Services.Harmony.HarmonyFormatter>();
        builder.Services.AddScoped<SharpAI.Services.Harmony.StreamingInferenceEngine>();
        builder.Services.AddScoped<SharpAI.Services.Harmony.HarmonyParser>();
    
        // Register llama and whisper hosted services
        builder.Services.AddSingleton<SharpAI.Services.WhisperHostedService>();
        builder.Services.AddHostedService<SharpAI.Services.WhisperHostedService>(provider => 
            provider.GetRequiredService<SharpAI.Services.WhisperHostedService>());
            
        builder.Services.AddSingleton<SharpAI.Services.LLamaSharpHostedService>();
        builder.Services.AddHostedService<SharpAI.Services.LLamaSharpHostedService>(provider => 
            provider.GetRequiredService<SharpAI.Services.LLamaSharpHostedService>());
        
        // Register TTS service as hosted service
        builder.Services.AddSingleton<SharpAI.ChatterboxService>();
        builder.Services.AddHostedService<SharpAI.ChatterboxService>(provider => 
            provider.GetRequiredService<SharpAI.ChatterboxService>());

        // Configure form options for large file uploads
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 100_000_000; // 100MB
            options.ValueLengthLimit = int.MaxValue;
            options.ValueCountLimit = int.MaxValue;
            options.KeyLengthLimit = int.MaxValue;
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
