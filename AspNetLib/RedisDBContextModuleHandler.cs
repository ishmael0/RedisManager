using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    public static class RedisDBContextModuleHandler
    {
        public static void AddRedisDBContext<T>(this IServiceCollection services, bool keepDataInMemory, Func<string, string>? nameGeneratorStrategy = null,
            string? channelName = null)
            where T : RedisDBContextModule
        {
            services.AddSingleton<T>(sp =>
            {
                var mux = sp.GetRequiredService<IConnectionMultiplexer>();
                try
                {
                    return ActivatorUtilities.CreateInstance<T>(sp, mux, keepDataInMemory, nameGeneratorStrategy, channelName);
                }
                catch
                {
                    return ActivatorUtilities.CreateInstance<T>(sp, mux, mux, keepDataInMemory, nameGeneratorStrategy, channelName);
                }
            });
        }
    }
}

