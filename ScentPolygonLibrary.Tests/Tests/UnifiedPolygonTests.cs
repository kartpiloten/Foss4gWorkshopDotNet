/*
 The functionallity in this file is:
 - Validate unified polygon creation from individual polygons and per-rover unified polygons.
 - Ensure NTS union results are valid, SRID is set, and areas behave as expected.
*/

using NetTopologySuite.Geometries;
using ScentPolygonLibrary;
using Xunit;

namespace ScentPolygonLibrary.Tests;

public class UnifiedPolygonTests
{
 [Fact]
 public void CreateUnifiedPolygon_FromOverlappingPolygons_ShouldBeValid_AndNotEmpty()
 {
 var lat = -36.85;
 var lon =174.76;

 var p1 = ScentPolygonCalculator.CreateScentPolygon(
 lat, lon,45,3.0,
 new ScentPolygonConfiguration { OmnidirectionalRadiusMeters =30, FanPolygonPoints =12, MinimumDistanceMultiplier =0.4 });

 var p2 = ScentPolygonCalculator.CreateScentPolygon(
 lat +0.0002, lon +0.0002,60,2.5,
 new ScentPolygonConfiguration { OmnidirectionalRadiusMeters =30, FanPolygonPoints =12, MinimumDistanceMultiplier =0.4 });

 var results = new List<ScentPolygonResult>
 {
 BuildResult(p1, lat, lon,45,3.0),
 BuildResult(p2, lat +0.0002, lon +0.0002,60,2.5)
 };

 var unified = ScentPolygonCalculator.CreateUnifiedPolygon(results);

 Assert.NotNull(unified);
 Assert.True(unified.Polygon.IsValid);
 Assert.Equal(4326, unified.Polygon.SRID);
 Assert.True(unified.TotalAreaM2 >0);
 Assert.Equal(2, unified.PolygonCount);
 }

 [Fact]
 public void CreateUnifiedFromRoverPolygons_ShouldUnionPerRoverUnified()
 {
 var lat = -36.85;
 var lon =174.76;

 var roverAUnified = ScentPolygonCalculator.CreateScentPolygon(
 lat, lon,90,4.0,
 new ScentPolygonConfiguration { OmnidirectionalRadiusMeters =30, FanPolygonPoints =14, MinimumDistanceMultiplier =0.4 });

 var roverBUnified = ScentPolygonCalculator.CreateScentPolygon(
 lat +0.0004, lon +0.0003,270,2.0,
 new ScentPolygonConfiguration { OmnidirectionalRadiusMeters =30, FanPolygonPoints =14, MinimumDistanceMultiplier =0.4 });

 var roverPolys = new List<RoverUnifiedPolygon>
 {
 new RoverUnifiedPolygon
 {
 RoverId = Guid.NewGuid(),
 RoverName = "Alpha",
 UnifiedPolygon = roverAUnified,
 PolygonCount =3,
 TotalAreaM2 = ScentPolygonCalculator.CalculateScentAreaM2(roverAUnified, lat),
 LatestSequence =100,
 EarliestMeasurement = DateTimeOffset.UtcNow.AddMinutes(-30),
 LatestMeasurement = DateTimeOffset.UtcNow,
 AverageLatitude = lat,
 Version =2
 },
 new RoverUnifiedPolygon
 {
 RoverId = Guid.NewGuid(),
 RoverName = "Bravo",
 UnifiedPolygon = roverBUnified,
 PolygonCount =2,
 TotalAreaM2 = ScentPolygonCalculator.CalculateScentAreaM2(roverBUnified, lat),
 LatestSequence =80,
 EarliestMeasurement = DateTimeOffset.UtcNow.AddMinutes(-25),
 LatestMeasurement = DateTimeOffset.UtcNow.AddMinutes(-5),
 AverageLatitude = lat,
 Version =5
 }
 };

 var unified = ScentPolygonCalculator.CreateUnifiedFromRoverPolygons(roverPolys);

 Assert.NotNull(unified);
 Assert.True(unified.Polygon.IsValid);
 Assert.Equal(4326, unified.Polygon.SRID);
 Assert.Equal(5, unified.PolygonCount);
 Assert.True(unified.TotalAreaM2 >0);
 Assert.NotEmpty(unified.RoverNames);
 }

 private static ScentPolygonResult BuildResult(
 Polygon poly, double lat, double lon, double windDir, double windSpeed)
 {
 return new ScentPolygonResult
 {
 Polygon = poly,
 RoverId = Guid.NewGuid(),
 RoverName = "TestRover",
 SessionId = Guid.NewGuid(),
 Sequence =1,
 RecordedAt = DateTimeOffset.UtcNow,
 Latitude = lat,
 Longitude = lon,
 WindDirectionDeg = windDir,
 WindSpeedMps = windSpeed,
 ScentAreaM2 = ScentPolygonCalculator.CalculateScentAreaM2(poly, lat)
 };
 }
}
