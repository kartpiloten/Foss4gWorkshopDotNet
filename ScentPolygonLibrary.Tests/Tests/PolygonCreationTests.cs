/*
 The functionallity in this file is:
 - Validate properties of polygons created by ScentPolygonCalculator.CreateScentPolygon.
 - Check validity, SRID, non-zero area, and orientation (upwind cone).
 - Verify presence of local omnidirectional buffer around the dog (expected by design).
 - Provide fast, deterministic unit tests for workshop learning.
*/

using NetTopologySuite.Geometries;
using ScentPolygonLibrary;
using Xunit;

namespace ScentPolygonLibrary.Tests;

public class PolygonCreationTests
{
 private static readonly GeometryFactory Gf = new(new PrecisionModel(),4326);

 [Fact]
 public void CreateScentPolygon_ShouldProduceValidPolygon_WithCorrectSrid_AndPositiveArea()
 {
 var lat = -36.85;
 var lon =174.76;
 var windDirDeg =45.0;
 var windSpeed =3.0;

 var poly = ScentPolygonCalculator.CreateScentPolygon(
 lat, lon, windDirDeg, windSpeed,
 new ScentPolygonConfiguration
 {
 OmnidirectionalRadiusMeters =30,
 FanPolygonPoints =15,
 MinimumDistanceMultiplier =0.4
 });

 Assert.NotNull(poly);
 Assert.True(poly.IsValid);
 Assert.Equal(4326, poly.SRID);

 var areaM2 = ScentPolygonCalculator.CalculateScentAreaM2(poly, lat);
 Assert.True(areaM2 >0, "Expected positive area in m²");
 }

 [Theory]
 [InlineData(0.5)]
 [InlineData(2.0)]
 [InlineData(5.0)]
 [InlineData(8.0)]
 public void CreateScentPolygon_Orientation_ShouldPointUpwind(double windSpeed)
 {
 var lat = -36.85;
 var lon =174.76;
 var windDirDeg =120.0; // degrees-from

 var poly = ScentPolygonCalculator.CreateScentPolygon(
 lat, lon, windDirDeg, windSpeed,
 new ScentPolygonConfiguration
 {
 OmnidirectionalRadiusMeters =30,
 FanPolygonPoints =20,
 MinimumDistanceMultiplier =0.4
 });

 var dog = new Coordinate(lon, lat);
 var far = GetFarthestVertex(poly, dog);
 var bearingDeg = BearingDegrees(dog, far);
 var fanHalfAngleDeg = RadToDeg(ScentPolygonCalculator.CalculateFanAngle(windSpeed));
 var diff = AngleDiffDeg(bearingDeg, windDirDeg);

 Assert.True(diff <= fanHalfAngleDeg +10.0,
 $"Farthest vertex bearing {bearingDeg:F1}° differs from upwind {windDirDeg:F1}° by {diff:F1}° (tolerance {fanHalfAngleDeg +10.0:F1}°)");
 }

 [Fact]
 public void CreateScentPolygon_ShouldIncludeLocalOmnidirectionalBuffer_AroundDog()
 {
 var lat = -36.85;
 var lon =174.76;
 var windDirDeg =200.0;
 var windSpeed =2.5;
 var cfg = new ScentPolygonConfiguration
 {
 OmnidirectionalRadiusMeters =30,
 FanPolygonPoints =15,
 MinimumDistanceMultiplier =0.4
 };

 var poly = ScentPolygonCalculator.CreateScentPolygon(
 lat, lon, windDirDeg, windSpeed, cfg);

 var dogPoint = Gf.CreatePoint(new Coordinate(lon, lat));
 var metersPerDegLat =111_320.0;
 var bufferDeg =10.0 / metersPerDegLat;

 var smallCircle = dogPoint.Buffer(bufferDeg);
 Assert.True(poly.Contains(smallCircle),
 "Expected omnidirectional buffer around the dog to be included in the scent polygon (fan ? small circle).");
 }

 [Fact]
 public void CreateScentPolygon_WithManyPoints_ShouldRemainValid_AndBeLarge()
 {
 // Arrange: use many fan points to create a high-vertex polygon
 var lat = -36.85;
 var lon =174.76;
 var windDirDeg =120.0;
 var windSpeed =7.9; // close to the max-distance regime in our model
 var manyPointsConfig = new ScentPolygonConfiguration
 {
 OmnidirectionalRadiusMeters =30,
 FanPolygonPoints =2000, // a lot of segment points for the fan
 MinimumDistanceMultiplier =0.4
 };

 // Act
 var poly = ScentPolygonCalculator.CreateScentPolygon(
 lat, lon, windDirDeg, windSpeed, manyPointsConfig);

 // Assert - validity and SRID
 Assert.NotNull(poly);
 Assert.True(poly.IsValid);
 Assert.Equal(4326, poly.SRID);

 // Assert - area is large (rough expectation >8,000 m^2)
 var areaM2 = ScentPolygonCalculator.CalculateScentAreaM2(poly, lat);
 Assert.True(areaM2 >8_000, $"Expected a large polygon area, got {areaM2:F0} m²");

 // Assert - polygon has many vertices (union may alter count, but should still be high)
 // Exterior ring points include closing vertex; ensure it stays well above a few hundred
 Assert.True(poly.ExteriorRing.NumPoints >500, $"Expected many vertices, got {poly.ExteriorRing.NumPoints}");
 }

 private static Coordinate GetFarthestVertex(Polygon poly, Coordinate origin)
 {
 var maxD2 = double.NegativeInfinity;
 Coordinate? best = null;
 foreach (var c in poly.ExteriorRing.Coordinates)
 {
 var dx = c.X - origin.X;
 var dy = c.Y - origin.Y;
 var d2 = dx * dx + dy * dy;
 if (d2 > maxD2)
 {
 maxD2 = d2;
 best = c;
 }
 }
 return best!;
 }

 private static double BearingDegrees(Coordinate from, Coordinate to)
 {
 var dx = to.X - from.X;
 var dy = to.Y - from.Y;
 var rad = Math.Atan2(dx, dy);
 var deg = RadToDeg(rad);
 return (deg +360.0) %360.0;
 }

 private static double RadToDeg(double r) => r *180.0 / Math.PI;

 private static double AngleDiffDeg(double a, double b)
 {
 var d = (a - b +540.0) %360.0 -180.0;
 return Math.Abs(d);
 }
}
