using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
// Enable detailed circuit errors in Development to surface root causes to the browser
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(o =>
    {
        if (builder.Environment.IsDevelopment())
        {
            o.DetailedErrors = true;
        }
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// ====== Hello world Leaflet data endpoint ======
app.MapGet("/api/hello-geojson", () =>
{
    var featureCollection = new
    {
        type = "FeatureCollection",
        features = new object[]
        {
            new
            {
                type = "Feature",
                properties = new { name = "Workshop Point" },
                geometry = new { type = "Point", coordinates = new double[] { 14.6357, 63.1792 } }
            }
        }
    };
    return Results.Json(featureCollection);
});

app.Run();
