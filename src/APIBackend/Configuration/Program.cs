using DatasetStudio.APIBackend.Endpoints;
using DatasetStudio.APIBackend.Extensions;
using DatasetStudio.APIBackend.Models;
using DatasetStudio.APIBackend.Services.DatasetManagement;
using DatasetStudio.DTO.Common;
using DatasetStudio.DTO.Datasets;
using Microsoft.AspNetCore.Http.Features;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to allow large file uploads (5GB)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 5L * 1024 * 1024 * 1024; // 5GB
});

// Configure form options to allow large multipart uploads (5GB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 5L * 1024 * 1024 * 1024; // 5GB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.Services.AddDatasetServices(builder.Configuration, builder.Environment);
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

app.Run();
