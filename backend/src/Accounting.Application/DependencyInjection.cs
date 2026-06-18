using Accounting.Application.Identity;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Accounting.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // ponytail (04-L2): Scoped is correct — LoginService takes Scoped deps (IUserRepository,
        // IPermissionLookup) and has no per-request mutable state; Transient would also be fine.
        // Application layer registering its own impl is a minor CA smell but harmless here.
        services.AddScoped<ILoginService, LoginService>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Scoped);
        return services;
    }
}
