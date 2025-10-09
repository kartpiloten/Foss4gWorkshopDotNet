namespace ScentPolygonLibrary;

public static class ScentGeo
{
    public const int Wgs84Srid = 4326;

    public static (double MPerDegLat, double MPerDegLon) MetersPerDegree(double latitudeDeg)
    {
        var mPerDegLat = 111_320.0;
        var mPerDegLon = 111_320.0 * Math.Cos(latitudeDeg * Math.PI / 180.0);
        return (mPerDegLat, mPerDegLon);
    }
}   