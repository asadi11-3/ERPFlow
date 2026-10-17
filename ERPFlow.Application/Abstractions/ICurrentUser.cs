namespace ERPFlow.Application.Abstractions;

public interface ICurrentUser
{
    Guid UserId { get; }
}
