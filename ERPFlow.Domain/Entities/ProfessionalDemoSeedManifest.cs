namespace ERPFlow.Domain.Entities;

public sealed class ProfessionalDemoSeedManifest
{
    private ProfessionalDemoSeedManifest() { }

    public ProfessionalDemoSeedManifest(string scenarioKey, string version, string databaseName,
        string countsJson, string validationJson)
    {
        if (string.IsNullOrWhiteSpace(scenarioKey) || string.IsNullOrWhiteSpace(version) ||
            string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(countsJson) ||
            string.IsNullOrWhiteSpace(validationJson))
            throw new ArgumentException("A complete demo seed manifest is required.");

        Id = Guid.NewGuid();
        ScenarioKey = scenarioKey.Trim();
        Version = version.Trim();
        DatabaseName = databaseName.Trim();
        CountsJson = countsJson;
        ValidationJson = validationJson;
        CreatedAt = BusinessClock.UtcNow;
    }

    public Guid Id { get; private set; }
    public string ScenarioKey { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public string DatabaseName { get; private set; } = string.Empty;
    public string CountsJson { get; private set; } = string.Empty;
    public string ValidationJson { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
}
