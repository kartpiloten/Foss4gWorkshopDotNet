using NetTopologySuite.Geometries;

namespace FrontendOpenLayers.Components;

public static class CoordinateExtensions
{
    /// <summary>
    /// Converts NTS Coordinates to a flat float array [lng, lat, lng, lat, ...]
    /// for efficient JS Interop with OpenLayers.
    /// </summary>
    public static float[] ToFloatArray(this Coordinate[] coordinates)
    {
        var result = new float[coordinates.Length * 2];
        for (int i = 0; i < coordinates.Length; i++)
        {
            result[i * 2] = (float)coordinates[i].X;     // Longitude
            result[i * 2 + 1] = (float)coordinates[i].Y; // Latitude
        }
        return result;
    }
}
