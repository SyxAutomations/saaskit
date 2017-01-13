using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasKit.Multitenancy;
using SaasKit.Multitenancy.Internal;
using System.Reflection;
using Rebus.Pipeline;
using System.Linq;
namespace Microsoft.Extensions.DependencyInjection
{
    public static class MultitenancyServiceCollectionExtensions
    {
        public static IServiceCollection AddMultitenancy<TTenant, TResolver>(this IServiceCollection services)
            where TResolver : class, ITenantResolver<TTenant>
            where TTenant : class
        {
            Ensure.Argument.NotNull(services, nameof(services));

            services.AddScoped<ITenantResolver<TTenant>, TResolver>();

            // No longer registered by default as of ASP.NET Core RC2
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            // Make Tenant and TenantContext injectable
            services.AddScoped(prov =>
            {
                var tenantContext = prov.GetService<IHttpContextAccessor>()?.HttpContext?.GetTenantContext<TTenant>();
                if (tenantContext == null) // if no HTTPContext is available, we are in rebus and should resolve it there.
                {
                    tenantContext = MessageContext.Current?.IncomingStepContext.Load<TenantContext<TTenant>>("saaskit.TenantContext");
                }
                return tenantContext;

            });
            services.AddScoped(prov =>
            {
                return prov.GetService<TenantContext<TTenant>>()?.Tenant;
            });

            // Make ITenant injectable for handling null injection, similar to IOptions
            services.AddScoped<ITenant<TTenant>>(prov => new TenantWrapper<TTenant>(prov.GetService<TTenant>()));

            // Ensure caching is available for caching resolvers
            var resolverType = typeof(TResolver);
            if (typeof(MemoryCacheTenantResolver<TTenant>).IsAssignableFrom(resolverType))
            {
                services.AddMemoryCache();
            }

            return services;
        }
    }
}
