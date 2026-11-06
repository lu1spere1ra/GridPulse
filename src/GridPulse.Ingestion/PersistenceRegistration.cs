namespace GridPulse.Ingestion;

internal static class PersistenceRegistration
{
    /// <summary>Wired to PostgreSQL in the persistence phase.</summary>
    public static void AddPersistence(this WebApplicationBuilder builder)
    {
        _ = builder;
    }
}
