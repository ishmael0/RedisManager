using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Modules.RedisModule
{
    /// <summary>
    /// Base class that holds common properties and delegates used by Redis key abstractions.
    /// </summary>
    /// <typeparam name="T">Value type of the key.</typeparam>
    public class RedisCommonProperties<T>
    {
        /// <summary>
        /// Deserializer delegate: raw JSON string -> wrapper.
        /// </summary>
        protected Func<string, RedisDataWrapper<T>> DeSerialize { set; get; }
        /// <summary>
        /// Serializer delegate: value -> JSON string.
        /// </summary>
        protected Func<T, string> Serialize { set; get; }
        /// <summary>
        /// Full Redis key string.
        /// </summary>
        public RedisKey FullName { protected set; get; }
        /// <summary>
        /// Reader DB instance.
        /// </summary>
        protected IDatabase DbReader { set; get; }
        /// <summary>
        /// Writer DB instance (may be same as reader).
        /// </summary>
        protected IDatabase DbWriter { set; get; }
        /// <summary>
        /// Indicates whether values should be cached in memory.
        /// </summary>
        protected bool KeepDataInMemory { set; get; }
        /// <summary>
        /// Logger instance.
        /// </summary>
        protected ILogger Logger { set; get; }
        /// <summary>
        /// Redis database index.
        /// </summary>
        public int DbIndex { protected set; get; }
    }
    /// <summary>
    /// Common methods exposed for simple (string) Redis keys.
    /// </summary>
    public interface IRedisCommonKeyMethods
    {
        /// <summary>
        /// Clears in-memory cache forcing next read to hit Redis.
        /// </summary>
        public void ForceToReFetch();
        /// <summary>
        /// Initializes the key with connections and metadata.
        /// </summary>
        public void Init(ILogger logger,
            IConnectionMultiplexer? cnWriter,
            IConnectionMultiplexer cnReader,
            Action publish,
            RedisKey fullName,
            bool keepDataInMemory);
    }
    /// <summary>
    /// Common methods exposed for Redis hash keys.
    /// </summary>
    public interface IRedisCommonHashKeyMethods
    {
        /// <summary>
        /// Removes a single cached field value forcing a Redis fetch next time.
        /// </summary>
        public void ForceToReFetch(string key);
        /// <summary>
        /// Clears the entire in-memory cache for the hash.
        /// </summary>
        public void ForceToReFetchAll();
        /// <summary>
        /// Initializes the hash key with connections and metadata.
        /// </summary>
        public void Init(ILogger logger,
            IConnectionMultiplexer? cnWriter,
            IConnectionMultiplexer cnReader,
            Action publishAll,
            Action<string> publish,
            RedisKey fullName,
            bool keepDataInMemory);
    }
}
