using ERPFlow.Domain;

namespace ERPFlow.Domain.Entities;

public sealed class ProfessionalDemoSeedOperation
{
    private ProfessionalDemoSeedOperation() { }

    public ProfessionalDemoSeedOperation(string scenarioKey, string operationKey, string operationType,
        string inputFingerprint, Guid actorId)
    {
        ScenarioKey = scenarioKey;
        OperationKey = operationKey;
        OperationType = operationType;
        InputFingerprint = inputFingerprint;
        ActorId = actorId;
        StartedAt = BusinessClock.UtcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string ScenarioKey { get; private set; } = null!;
    public string OperationKey { get; private set; } = null!;
    public string OperationType { get; private set; } = null!;
    public string InputFingerprint { get; private set; } = null!;
    public Guid ActorId { get; private set; }
    public Guid? ResultDocumentId { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public void Complete(Guid resultDocumentId)
    {
        ResultDocumentId = resultDocumentId;
        CompletedAt = BusinessClock.UtcNow;
    }
}
