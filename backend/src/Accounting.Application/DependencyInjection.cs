using Accounting.Application.Identity;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Accounting.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ILoginService, LoginService>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Scoped);
        return services;
    }
}
