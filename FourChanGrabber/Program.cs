using FourChanGrabber.Data;
using FourChanGrabber.DownloadManager;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddDbContext<MediaDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("MediaDb") ?? "Data Source=./data/4chan.db"));
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddSingleton<DownloadManager>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DownloadManager>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
    await db.Database.MigrateAsync();
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

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
