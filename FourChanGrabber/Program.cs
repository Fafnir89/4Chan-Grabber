using FourChanGrabber.Data;
using FourChanGrabber.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();
builder.Services.AddDbContext<MediaDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("MediaDb") ?? "Data Source=./data/4chan.db"));
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<IQueueService, QueueService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IDownloadService>(sp =>
{
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<DownloadService>>();
    var tempDir = sp.GetRequiredService<IConfiguration>()["downloadManager:tempDirectory"] ?? "./temp/downloads";
    return new DownloadService(httpFactory, logger, tempDir);
});
builder.Services.AddSingleton<DownloadManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DownloadManager>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
