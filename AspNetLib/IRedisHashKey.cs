using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    public class RedisCommonProperties<T>
    {
        public required Func<string?, RedisDataWrapper<T>?> DeSerialize { set; get; }
        public required Func<T, string> Serialize { set; get; }
        public required RedisKey FullName { set; get; }
        public required IDatabase Reader { set; get; }
        public required IDatabase Writer { set; get; }
        public required int DbIndex { set; get; }
        public required RedisDBContextModuleConfigs ContextConfig { set; get; }
    }
    public interface IRedisKey
    {
        public RedisKey FullName { set; get; }
        public void InvalidateCache();
        public void Init(RedisDBContextModuleConfigs contexConfig, RedisKey fullName);
    }
    public interface IRedisHashKey : IRedisKey
    {
        public void InvalidateCache(string key);
    }
}
