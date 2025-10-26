using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    public class RedisCommonProperties<T>
    {
        protected Func<string?, RedisDataWrapper<T>?> DeSerialize { set; get; }
        protected Func<T, string> Serialize { set; get; }
        public RedisKey FullName {   set; get; }
        public IDatabase Reader {   set; get; }
        public IDatabase Writer {   set; get; }
        public int DbIndex { protected set; get; }
        public RedisDBContextModuleConfigs ContextConfig { set; get; }
    }
    public interface IRedisKey
    {
        public RedisKey FullName { set; get; }
        public void ForceToReFetch();
        public void Init(RedisDBContextModuleConfigs contexConfig, RedisKey fullName);
    }
    public interface IRedisHashKey : IRedisKey
    {
        public void ForceToReFetch(string key);
    }
}
