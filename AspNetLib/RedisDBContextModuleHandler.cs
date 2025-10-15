using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    public static class RedisDBContextModuleHandler
    {
        public static void AddRedisDBContext<T>(this IServiceCollection services, bool keepDataInMemory, string? prefix = null,
            string? channelName = null)
            where T : RedisDBContextModule
        {
            services.AddSingleton<T>(sp =>
            {
                var mux = sp.GetRequiredService<IConnectionMultiplexer>();

                // Try single-multiplexer constructor first: (IConnectionMultiplexer, bool, ILogger, string?, string?)
                try
                {
                    return ActivatorUtilities.CreateInstance<T>(sp, mux, keepDataInMemory, prefix, channelName);
                }
                catch
                {
                    // Fallback to dual-multiplexer constructor: (IConnectionMultiplexer, IConnectionMultiplexer, bool, ILogger, string?, string?)
                    return ActivatorUtilities.CreateInstance<T>(sp, mux, mux, keepDataInMemory, prefix, channelName);
                }
            });
        }
    }
}

