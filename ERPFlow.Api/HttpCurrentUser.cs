using System.IdentityModel.Tokens.Jwt;
using ERPFlow.Application.Abstractions;

namespace ERPFlow.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId => Guid.TryParse(
        accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
        out var id)
        ? id
        : Guid.Empty;
}
