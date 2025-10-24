
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;

namespace SharpAI;

public class Program
{
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
        
        // Register harmony services
        builder.Services.AddSingleton<SharpAI.Services.Harmony.HarmonyFormatter>();
        builder.Services.AddSingleton<SharpAI.Services.Harmony.HarmonyParser>();
        builder.Services.AddSingleton<SharpAI.Services.Harmony.StreamingInferenceEngine>();
        
        builder.Services.AddSingleton<SharpAI.Services.LLamaSharpService>();
        builder.Services.AddSingleton<SharpAI.Services.WhisperService>();

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
