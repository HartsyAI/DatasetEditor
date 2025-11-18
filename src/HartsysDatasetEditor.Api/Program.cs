using HartsysDatasetEditor.Api.Endpoints;
using HartsysDatasetEditor.Api.Extensions;
using HartsysDatasetEditor.Api.Models;
using HartsysDatasetEditor.Api.Services;
using HartsysDatasetEditor.Api.Services.Dtos;
using HartsysDatasetEditor.Contracts.Common;
using HartsysDatasetEditor.Contracts.Datasets;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to allow large file uploads (5GB)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 5L * 1024 * 1024 * 1024; // 5GB
});

builder.Services.AddDatasetServices(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
string corsPolicyName = "DatasetEditorClient";
string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(allowedOrigins);
        }
        policy.AllowAnyHeader().AllowAnyMethod();
    });
});
WebApplication app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors(corsPolicyName);

// Map all endpoints
app.MapDatasetEndpoints();
app.MapItemEditEndpoints();

app.MapFallbackToFile("index.html");

// Initialize database
DatabaseInitializationService dbInit = app.Services.GetRequiredService<DatabaseInitializationService>();
dbInit.Initialize();

app.Run();
