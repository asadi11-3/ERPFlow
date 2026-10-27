using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;

namespace ERPFlow.Infrastructure.Persistence.Inventory;

public sealed class DisabledInventoryCutoverPermission : IInventoryCutoverPermission
{
    public void EnsureOperatorExecutionAllowed() => throw new WorkflowConflictException(
        "Live cutover is not enabled. Operator execution requires a separately reviewed isolated rehearsal host.");
}
