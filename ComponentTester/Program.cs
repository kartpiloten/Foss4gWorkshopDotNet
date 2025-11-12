using ComponentTester;
using ComponentTester.Components;
using ScentPolygonLibrary;
using RoverData.Repository;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add memory cache for polygon caching
builder.Services.AddMemoryCache();

// Configure mock/stub repository for testing
builder.Services.AddScoped<ISessionContext>(provider =>
{
    return new WebSessionContext(Guid.NewGuid(), "test-session");
});

// Use stub repository for testing (no real data needed)
builder.Services.AddScoped<IRoverDataRepository, StubRoverDataRepository>();

// Find forest file path
string FindForestFile()
{
var possiblePaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "..", "Solutionresources", "RiverHeadForest.gpkg"),
        Path.Combine(Directory.GetCurrentDirectory(), "Solutionresources", "RiverHeadForest.gpkg"),
 };
    
    return possiblePaths.FirstOrDefault(File.Exists) ?? possiblePaths[0];
}

var forestPath = FindForestFile();

// Register ScentPolygonGenerator as scoped
builder.Services.AddScoped<ScentPolygonGenerator>(provider =>
{
    var repo = provider.GetRequiredService<IRoverDataRepository>();
    var cache = provider.GetRequiredService<IMemoryCache>();
  var config = new ScentPolygonConfiguration();
    return new ScentPolygonGenerator(repo, cache, config, forestPath);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
 app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
