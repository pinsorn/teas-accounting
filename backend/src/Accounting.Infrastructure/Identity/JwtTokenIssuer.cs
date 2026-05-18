using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Accounting.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Accounting.Infrastructure.Identity;

public sealed class JwtOptions
{
    public required string Issuer    { get; init; }
    public required string Audience  { get; init; }
    public required string SigningKey { get; init; }
    /// <summary>Access token lifetime in minutes. Default 60.</summary>
    public int AccessTokenMinutes { get; init; } = 60;
}

public sealed class JwtTokenIssuer : IJwtTokenIssuer
{
    private readonly JwtOptions _opts;
    private readonly SigningCredentials _creds;

    public JwtTokenIssuer(IOptions<JwtOptions> opts)
    {
        _opts = opts.Value;
        var keyBytes = Encoding.UTF8.GetBytes(_opts.SigningKey);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (256 bits).");
        _creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
    }

    public AccessToken Issue(TokenClaims c)
    {
        var now = DateTime.UtcNow;
        var exp = now.AddMinutes(_opts.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, c.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, c.UserId.ToString()),
            new(ClaimTypes.Name, c.Username),
            new(TenantClaims.CompanyId, c.CompanyId.ToString()),
            new(TenantClaims.BranchId, c.BranchId.ToString()),
            new(TenantClaims.IsSuperAdmin, c.IsSuperAdmin ? "true" : "false"),
        };
        claims.AddRange(c.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(c.Permissions.Select(p => new Claim(TenantClaims.Permission, p)));

        var jwt = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            notBefore: now,
            expires: exp,
            signingCredentials: _creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return new AccessToken(token, new DateTimeOffset(exp, TimeSpan.Zero));
    }
}
