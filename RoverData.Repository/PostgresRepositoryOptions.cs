namespace RoverData.Repository;

public class PostgresRepositoryOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string SessionName { get; set; } = "default";
}
