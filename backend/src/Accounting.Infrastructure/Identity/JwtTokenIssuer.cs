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
    private readonly IOptionsMonitor<JwtOptions> _options;

    // IOptionsMonitor (not IOptions) so the AccessTokenMinutes written by the first-run setup
    // endpoint into the reloadOnChange'd appsettings.Secrets.json takes effect live on the NEXT
    // issued token, with NO app restart. SigningKey/Issuer/Audience are read per-call too; the JWT
    // bearer VALIDATION wire-up in Program.cs reads them eagerly at boot (those don't change live).
    public JwtTokenIssuer(IOptionsMonitor<JwtOptions> options) => _options = options;

    public AccessToken Issue(TokenClaims c)
    {
        var opts = _options.CurrentValue;
        var keyBytes = Encoding.UTF8.GetBytes(opts.SigningKey);
        if (keyBytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (256 bits).");
        var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var exp = now.AddMinutes(opts.AccessTokenMinutes);

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
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claims,
            notBefore: now,
            expires: exp,
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return new AccessToken(token, new DateTimeOffset(exp, TimeSpan.Zero));
    }
}
