using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    public static class RedisDBContextModuleHandler
    {
        public static void AddRedisDBContext<T>(this IServiceCollection services, RedisDBContextExtendedOptions opts)
            where T : RedisDBContextModule
        {
            services.AddSingleton<T>(sp =>
            {
                return ActivatorUtilities.CreateInstance<T>(sp, opts);
            });
        }
        public static void AddRedisDBContext<T>(this IServiceCollection services, RedisDBContextOptions opts)
            where T : RedisDBContextModule
        {
            services.AddSingleton<T>(sp =>
            {
                return ActivatorUtilities.CreateInstance<T>(sp, opts);
            });
        }
    }
}

