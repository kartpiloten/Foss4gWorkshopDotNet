/* The functionallity in this file is:
 - Provide extension methods for converting NetTopologySuite geometries to binary float arrays
 - Enable efficient JavaScript Interop by minimizing serialization overhead
 - Convert coordinates to [lng, lat] pairs as expected by Leaflet
*/

using NetTopologySuite.Geometries;

namespace FrontendLeaflet;

/// <summary>
/// Extension methods for converting NTS coordinates to float arrays for efficient JS transfer
/// </summary>
public static class GeometryExtensions
{
    /// <summary>
    /// Converts NTS Coordinate array to flat float array [lng1, lat1, lng2, lat2, ...]
    /// Note: Leaflet uses [lng, lat] order (X, Y in NTS coordinates)
    /// </summary>
    public static float[] ToFloatArray(this Coordinate[] coordinates)
    {
        if (coordinates == null || coordinates.Length == 0)
            return Array.Empty<float>();

        var result = new float[coordinates.Length * 2];
        for (int i = 0; i < coordinates.Length; i++)
        {
            result[i * 2] = (float)coordinates[i].X;     // Longitude
            result[i * 2 + 1] = (float)coordinates[i].Y; // Latitude
        }
        return result;
    }
}
