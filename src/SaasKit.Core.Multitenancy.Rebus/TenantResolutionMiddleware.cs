using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rebus.Messages;
using Rebus.Pipeline;

namespace SaasKit.Multitenancy.Rebus
{
    public class TenantResolutionMiddleware<TTenant> : IIncomingStep
    {
        private readonly ITenantResolver<TTenant> _tenantResolver;
        private readonly ILogger _logger;
        public TenantResolutionMiddleware(ITenantResolver<TTenant> tenantResolver, ILoggerFactory factory)
        {
            _logger = factory.CreateLogger("SaasKit.Multitenancy.Rebus");
            this._tenantResolver = tenantResolver;
        }

        public async Task Process(IncomingStepContext context, Func<Task> next)
        {
            _logger.LogDebug("Resolving TenantContext using {loggerType}.");
            var message = context.Load<Message>();
            
            var tenantContext = await _tenantResolver.ResolveAsync(context);
            if (tenantContext != null)
            {
                Console.WriteLine("TenantContext Resolved. Adding to IncomingStepContext.");
                context.SetTenantContext(tenantContext);
            }
            else
            {
                Console.WriteLine("TenantContext Not Resolved.");
            }
            await next();
        }
    }

    public static class RebusExtensions
    {
        private const string TenantContextKey = "saaskit.TenantContext";
        // todo: can't this be done on the messagecontext
        public static void SetTenantContext<T>(this IncomingStepContext context, TenantContext<T> tenantContext)
        {
            context.Save(TenantContextKey, tenantContext);
        }

        public static TenantContext<T> GetTenantContext<T>(this IncomingStepContext context)
        {
            return context.Load<TenantContext<T>>(TenantContextKey);
        }

    }
}