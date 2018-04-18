using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Rebus.Pipeline;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SaasKit.Multitenancy
{
    public abstract class MemoryCacheTenantResolver<TTenant> : ITenantResolver<TTenant>
    {
        protected readonly IMemoryCache cache;
        protected readonly ILogger log;
        protected readonly MemoryCacheTenantResolverOptions options;

        public MemoryCacheTenantResolver(IMemoryCache cache, ILoggerFactory loggerFactory)
            : this(cache, loggerFactory, new MemoryCacheTenantResolverOptions())
        {
        }

        public MemoryCacheTenantResolver(IMemoryCache cache, ILoggerFactory loggerFactory, MemoryCacheTenantResolverOptions options)
        {
            Ensure.Argument.NotNull(cache, nameof(cache));
            Ensure.Argument.NotNull(loggerFactory, nameof(loggerFactory));
            Ensure.Argument.NotNull(options, nameof(options));

            this.cache = cache;
            this.log = loggerFactory.CreateLogger<MemoryCacheTenantResolver<TTenant>>();
            this.options = options;
        }

        protected virtual MemoryCacheEntryOptions CreateCacheEntryOptions()
        {
            return new MemoryCacheEntryOptions()
                .SetSlidingExpiration(new TimeSpan(1, 0, 0));
        }

        protected virtual void DisposeTenantContext(object cacheKey, TenantContext<TTenant> tenantContext)
        {
            if (tenantContext != null)
            {
                log.LogDebug("Disposing TenantContext:{id} instance with key \"{cacheKey}\".", tenantContext.Id, cacheKey);
                tenantContext.Dispose();
            }
        }

        protected abstract string GetContextIdentifier(HttpContext context);
        protected abstract string GetContextIdentifier(IncomingStepContext context);
        protected abstract IEnumerable<string> GetTenantIdentifiers(TenantContext<TTenant> context);
        protected abstract Task<TenantContext<TTenant>> ResolveAsync(HttpContext context);
        protected abstract Task<TenantContext<TTenant>> ResolveAsync(IncomingStepContext context);
        protected abstract void HandleTenantContext(TenantContext<TTenant> context);

        async Task<TenantContext<TTenant>> ITenantResolver<TTenant>.ResolveAsync(object context)
        {
            var httpContext = context as HttpContext;
            if (httpContext != null)
            {
                var tenantContext = RetrieveTenantContextFromCache(httpContext);
                var fromCache = tenantContext != null;

                if (!fromCache) tenantContext = await ResolveAsync(httpContext);

                HandleTenantContext(tenantContext);
                if (!fromCache) CacheTenantContext(tenantContext);

                return tenantContext;
            }

            var messageContext = context as IncomingStepContext;
            if (messageContext != null)
            {
                var tenantContext = RetrieveTenantContextFromCache(messageContext);
                var fromCache = tenantContext != null;

                if (!fromCache) tenantContext = await ResolveAsync(messageContext);

                HandleTenantContext(tenantContext);
                if (!fromCache) CacheTenantContext(tenantContext);

                return tenantContext;
            }

            throw new NotImplementedException("this context is not supported");
        }

        private TenantContext<TTenant> RetrieveTenantContextFromCache(object context)
        {
            // Obtain the key used to identify cached tenants from the current request
            var cacheKey = GetContextIdentifier(context);
            if (cacheKey == null) return null;

            var tenantContext = cache.Get(cacheKey) as TenantContext<TTenant>;
            if (tenantContext != null)
            {
                log.LogDebug("TenantContext:{id} retrieved from cache with key \"{cacheKey}\".", tenantContext.Id, cacheKey);
                return tenantContext;
            }

            log.LogDebug("TenantContext not present in cache with key \"{cacheKey}\". Attempting to resolve.", cacheKey);
            return null;
        }

        private string GetContextIdentifier(object context)
        {
            var httpContext = context as HttpContext;
            if (httpContext != null) return GetContextIdentifier(httpContext);

            var messageContext = context as IncomingStepContext;
            if (messageContext != null) return GetContextIdentifier(messageContext);

            throw new NotImplementedException("this context is not supported");
        }

        private void CacheTenantContext(TenantContext<TTenant> tenantContext)
        {
            if (tenantContext == null) return;

            var tenantIdentifiers = GetTenantIdentifiers(tenantContext);
            if (tenantIdentifiers == null) return;

            var cacheEntryOptions = GetCacheEntryOptions();

            log.LogDebug("TenantContext:{id} resolved. Caching with keys \"{tenantIdentifiers}\".", tenantContext.Id, tenantIdentifiers);

            foreach (var identifier in tenantIdentifiers)
            {
                cache.Set(identifier, tenantContext, cacheEntryOptions);
            }
        }

        private MemoryCacheEntryOptions GetCacheEntryOptions()
        {
            var cacheEntryOptions = CreateCacheEntryOptions();

            if (options.EvictAllEntriesOnExpiry)
            {
                var tokenSource = new CancellationTokenSource();

                cacheEntryOptions
                    .RegisterPostEvictionCallback(
                        (key, value, reason, state) =>
                        {
                            tokenSource.Cancel();
                        })
                    .AddExpirationToken(new CancellationChangeToken(tokenSource.Token));
            }

            if (options.DisposeOnEviction)
            {
                cacheEntryOptions
                    .RegisterPostEvictionCallback(
                        (key, value, reason, state) =>
                        {
                            DisposeTenantContext(key, value as TenantContext<TTenant>);
                        });
            }

            return cacheEntryOptions;
        }
    }
}
