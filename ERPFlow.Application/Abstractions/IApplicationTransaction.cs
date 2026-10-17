namespace ERPFlow.Application.Abstractions;

public interface IApplicationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
