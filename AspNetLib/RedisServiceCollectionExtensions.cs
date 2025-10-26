using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    public static class RedisServiceCollectionExtensions
    {
        /// <summary>
        /// Register RedisDBContextModule with an options instance. Use this to centralize registration and DI wiring.
        /// </summary>
        public static IServiceCollection AddRedisDBContext(this IServiceCollection services, Action<RedisDBContextOptions> configure)
        {
            var options = new RedisDBContextOptions();
            configure(options);

            // Register the options instance so tests can replace it, or use DI to resolve it.
            services.AddSingleton(options);

            // Register the context as singleton (adjust lifetime as appropriate).
            services.AddSingleton<RedisDBContextModule>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<RedisDBContextModule>>();
                return new RedisDBContextModule(options, logger);
            });

            return services;
        }

        /// <summary>
        /// Convenience overload that registers a single multiplexer for both read and write.
        /// </summary>
        public static IServiceCollection AddRedisDBContext(this IServiceCollection services, IConnectionMultiplexer multiplexer, Action<RedisDBContextOptions>? configure = null)
        {
            return services.AddRedisDBContext(opts =>
            {
                opts.ConnectionMultiplexerRead = multiplexer;
                opts.ConnectionMultiplexerWrite = multiplexer;
                configure?.Invoke(opts);
            });
        }
    }
}