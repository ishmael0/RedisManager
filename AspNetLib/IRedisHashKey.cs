using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    public class RedisCommonProperties<T>
    {
        public Func<string?, RedisDataWrapper<T>?> DeSerialize { set; get; } = null!;
        public Func<T, string> Serialize { set; get; } = null!;
        public RedisKey FullName { set; get; }
        public IDatabase Reader { set; get; } = null!;
        public IDatabase Writer { set; get; } = null!;
        public int DbIndex { set; get; }
        public RedisDBContextModuleConfigs ContextConfig { set; get; } = null!;
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
