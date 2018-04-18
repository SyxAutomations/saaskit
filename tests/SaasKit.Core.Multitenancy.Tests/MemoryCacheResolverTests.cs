using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SaasKit.Multitenancy.Tests
{
    public class MemoryCacheTenantResolverTests
    {
        private HttpContext CreateHttpContext(string requestPath)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = requestPath;
            return context;
        }

        private IncomingStepContext CreateIncomingStepContext(string path)
        {
            using (new RebusTransactionScope())
            {
                var headers = new Dictionary<string, string> { { "path", path } };
                var msg = new TransportMessage(headers, Encoding.UTF8.GetBytes(path));
                var context = new IncomingStepContext(msg, AmbientTransactionContext.Current);
                return context;
            }
        }

        [Fact]
        public async Task Can_resolve_tenant_httpcontext()
        {
            var harness = new TestHarness();
            var context = CreateHttpContext("/apple");

            var tenantContext = await harness.Resolver.ResolveAsync(context);

            Assert.NotNull(tenantContext);
            Assert.Equal("fruit", tenantContext.Tenant.Id);
        }

        [Fact]
        public async Task Can_resolve_tenant_incomingstepcontext()
        {
            var harness = new TestHarness();
            var context = CreateIncomingStepContext("/apple");

            var tenantContext = await harness.Resolver.ResolveAsync(context);

            Assert.NotNull(tenantContext);
            Assert.Equal("fruit", tenantContext.Tenant.Id);
        }

        [Fact]
        public async Task Can_retrieve_tenant_httpcontext_from_cache()
        {
            var harness = new TestHarness();
            var context = CreateHttpContext("/apple");

            var tenantContext = await harness.Resolver.ResolveAsync(context);

            TenantContext<TestTenant> cachedTenant;
            Assert.True(harness.Cache.TryGetValue("/apple", out cachedTenant));
            Assert.Equal(tenantContext.Tenant.Id, cachedTenant.Tenant.Id);
        }

        [Fact]
        public async Task Can_retrieve_tenant_incomingstepcontext_from_cache()
        {
            var harness = new TestHarness();
            var context = CreateIncomingStepContext("/apple");

            var tenantContext = await harness.Resolver.ResolveAsync(context);

            TenantContext<TestTenant> cachedTenant;
            Assert.True(harness.Cache.TryGetValue("/apple", out cachedTenant));
            Assert.Equal(tenantContext.Tenant.Id, cachedTenant.Tenant.Id);
        }

        [Fact]
        public async Task Can_retrieve_tenant_context_from_cache_using_linked_identifier()
        {
            var harness = new TestHarness();
            var context = CreateHttpContext("/apple");

            var tenantContext = await harness.Resolver.ResolveAsync(context);

            TenantContext<TestTenant> cachedTenant;
            Assert.True(harness.Cache.TryGetValue("/pear", out cachedTenant));
            Assert.Equal(tenantContext.Tenant.Id, cachedTenant.Tenant.Id);
        }

        [Fact]
        public async Task Should_dispose_tenant_on_eviction_from_cache_by_default()
        {
            var harness = new TestHarness(cacheExpirationInSeconds: 1);
            var context = CreateHttpContext("/apple");

            var tenantContext = await harness.Resolver.ResolveAsync(context);

            TenantContext<TestTenant> cachedTenant = null;

            Thread.Sleep(1000);

            // force MemoryCache to examine itself cache for pending evictions
            harness.Cache.Get("/foobar");

            // and give it a moment to works its magic
            Thread.Sleep(100);

            // should also expire tenant context by default
            Assert.False(harness.Cache.TryGetValue("/apple", out cachedTenant), "Apple Exists");
            Assert.True(tenantContext.Tenant.Disposed);
            Assert.Null(cachedTenant);
        }

        [Fact]
        public async Task Should_evict_all_cache_entries_of_tenant_context_by_default()
        {
            var harness = new TestHarness(cacheExpirationInSeconds: 10);

            // first request for apple
            var tenantContext = await harness.Resolver.ResolveAsync(CreateHttpContext("/apple"));

            // cache should have all 3 entries
            Assert.NotNull(harness.Cache.Get("/apple"));
            Assert.NotNull(harness.Cache.Get("/pear"));
            Assert.NotNull(harness.Cache.Get("/grape"));

            TenantContext<TestTenant> cachedTenant;

            // expire apple
            harness.Cache.Remove("/apple");

            Thread.Sleep(500);

            // look it up again so it registers
            harness.Cache.TryGetValue("/apple", out cachedTenant);

            Thread.Sleep(500);

            // pear is expired - because apple is
            Assert.False(harness.Cache.TryGetValue("/pear", out cachedTenant), "Pear Exists");

            // should also expire tenant context by default
            Assert.True(tenantContext.Tenant.Disposed);
        }

        [Fact]
        public async Task Can_evict_single_cache_entry_of_tenant_context()
        {
            TenantContext<TestTenant> cachedTenant;
            var harness = new TestHarness(cacheExpirationInSeconds: 2, evictAllOnExpiry: false);

            // first request for apple
            await harness.Resolver.ResolveAsync(CreateHttpContext("/apple"));

            // wait 1 second
            Thread.Sleep(1000);

            // second request for pear
            await harness.Resolver.ResolveAsync(CreateHttpContext("/pear"));

            // wait 1 second
            Thread.Sleep(1000);

            // apple is expired
            Assert.False(harness.Cache.TryGetValue("/apple", out cachedTenant), "Apple Exists");

            // pear is not expired
            Assert.True(harness.Cache.TryGetValue("/pear", out cachedTenant), "Pear Does Not Exist");
        }

        [Fact]
        public async Task Can_dispose_on_eviction()
        {
            var harness = new TestHarness(cacheExpirationInSeconds: 1, disposeOnEviction: true);
            var context = CreateHttpContext("/apple");

            var tenantContext = await harness.Resolver.ResolveAsync(context);

            Thread.Sleep(2 * 1000);
            // access it again so that MemoryCache examines it's cache for pending evictions
            harness.Cache.Get("/foobar");

            Thread.Sleep(1 * 1000);
            // access it again and we should see the eviction
            Assert.True(tenantContext.Tenant.Disposed);
        }

        [Fact]
        public async Task Can_not_dispose_on_eviction()
        {
            var harness = new TestHarness(cacheExpirationInSeconds: 1, disposeOnEviction: false);
            var context = CreateHttpContext("/apple");

            var tenantContext = await harness.Resolver.ResolveAsync(context);

            Thread.Sleep(1 * 1000);
            // access it again so that MemoryCache examines it's cache for pending evictions
            harness.Cache.Get("/foobar");

            Thread.Sleep(1 * 1000);
            // access it again and even though it's disposed, it should not be evicted
            Assert.False(tenantContext.Tenant.Disposed);
        }

        class TestTenant : IDisposable
        {
            public bool Disposed { get; set; }

            public string Id { get; set; }

            public List<string> Paths { get; set; }

            public CancellationTokenSource Cts = new CancellationTokenSource();

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (Disposed)
                {
                    return;
                }

                if (disposing)
                {
                    Cts.Cancel();
                }

                Disposed = true;
            }
        }

        class TestTenantMemoryCacheResolver : MemoryCacheTenantResolver<TestTenant>
        {
            readonly List<TestTenant> tenants = new List<TestTenant>()
            {
                new TestTenant { Id = "fruit", Paths = new List<string>() { "/apple","/pear","/grape" }},
                new TestTenant { Id = "vegetable", Paths = new List<string>() { "/lettuce","/carrot","/onion" }}
            };

            private readonly int cacheExpirationInSeconds;

            public TestTenantMemoryCacheResolver(IMemoryCache cache, ILoggerFactory loggerFactory, MemoryCacheTenantResolverOptions options, int cacheExpirationInSeconds = 10)
                : base(cache, loggerFactory, options)
            {
                this.cacheExpirationInSeconds = cacheExpirationInSeconds;
            }

            protected override MemoryCacheEntryOptions CreateCacheEntryOptions()
            {
                return new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromSeconds(cacheExpirationInSeconds));
            }

            protected override string GetContextIdentifier(HttpContext context)
            {
                return context.Request.Path;
            }

            protected override string GetContextIdentifier(IncomingStepContext context)
            {
                var msg = context.Load<TransportMessage>();
                return msg.Headers.GetValueOrDefault("path");
            }

            protected override IEnumerable<string> GetTenantIdentifiers(TenantContext<TestTenant> context)
            {
                return context?.Tenant?.Paths;
            }

            protected override void HandleTenantContext(TenantContext<TestTenant> context)
            {
                return;
            }

            protected override Task<TenantContext<TestTenant>> ResolveAsync(HttpContext context)
            {
                var path = context.Request.Path;
                return CreateTenantContext(path);
            }

            protected override Task<TenantContext<TestTenant>> ResolveAsync(IncomingStepContext context)
            {
                var path = GetContextIdentifier(context);
                return CreateTenantContext(path);
            }

            private Task<TenantContext<TestTenant>> CreateTenantContext(PathString path)
            {
                var tenant = tenants.FirstOrDefault(testTenant => testTenant.Paths.Contains(path));
                var tenantContext = new TenantContext<TestTenant>(tenant);
                tenantContext.Properties.Add("Created", DateTime.UtcNow);
                return Task.FromResult(tenantContext);
            }
        }

        class TestHarness
        {
            static ILoggerFactory loggerFactory = new LoggerFactory().AddConsole();

            public IMemoryCache Cache = new MemoryCache(new MemoryCacheOptions()
            {
                // for testing purposes, we'll scan every 100 milliseconds
                ExpirationScanFrequency = TimeSpan.FromMilliseconds(100),
                Clock = new SystemClock()
            });

            public TestHarness(bool disposeOnEviction = true, int cacheExpirationInSeconds = 10, bool evictAllOnExpiry = true)
            {
                var options = new MemoryCacheTenantResolverOptions { DisposeOnEviction = disposeOnEviction, EvictAllEntriesOnExpiry = evictAllOnExpiry };
                Resolver = new TestTenantMemoryCacheResolver(Cache, loggerFactory, options, cacheExpirationInSeconds);
            }

            public ITenantResolver<TestTenant> Resolver { get; }
        }
    }
}