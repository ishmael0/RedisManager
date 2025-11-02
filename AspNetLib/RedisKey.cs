using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{

    public class RedisKey<T> : RedisCommonProperties<T>, IRedisKey
    {
        private readonly object _locker = new();
        private RedisDataWrapper<T>? _data;
        public RedisKey(int dbIndex, Func<T, string>? serialize = null, Func<string, T>? deSerialize = null)
        {
            DbIndex = dbIndex;
            Serialize = (d) => JsonConvert.SerializeObject(serialize == null ? new RedisDataWrapper<T>(d) : new RedisDataWrapper<string>(serialize(d)));
            DeSerialize = (str) =>
            {
                if (str == null)
                    return null;
                if (deSerialize == null)
                    return JsonConvert.DeserializeObject<RedisDataWrapper<T>>(str)!;
                var temp = JsonConvert.DeserializeObject<RedisDataWrapper<string>>(str);
                if (temp != null)
                    return new RedisDataWrapper<T>(deSerialize(temp!.Data))
                    {
                        DateTime = temp.DateTime,
                        PersianLastUpdate = temp.PersianLastUpdate
                    };
                return null;
            };
        }
        public void Init(RedisDBContextModuleConfigs contexConfig, RedisKey fullName)
        {
            ContextConfig = contexConfig;
            FullName = fullName;
            Reader = ContextConfig.Reader.GetDatabase(DbIndex);
            Writer = ContextConfig.Writer.GetDatabase(DbIndex);
        }

        public long GetSize()
        {
            try
            {
                var keyMemoryUsage = Reader.Execute("MEMORY", "USAGE", FullName);
                if (keyMemoryUsage.IsNull) return 0;
                var s = keyMemoryUsage.ToString();
                if (long.TryParse(s, out var size)) return size;
                return 0;
            }
            catch (RedisServerException ex)
            {
                // MEMORY USAGE may be disabled on some providers. Log and return 0.
                ContextConfig.Logger?.LogWarning(ex, $"MEMORY USAGE not supported or failed for {FullName}");
                return 0;
            }
            catch (Exception ex)
            {
                ContextConfig.Logger?.LogError(ex, $"Error getting size for {FullName}");
                return 0;
            }
        }
        public T? Read(bool force = false)
        {
            // First check without lock for performance (if not forcing refresh)
            if (!force)
            {
                lock (_locker)
                {
                    if (_data != null)
                        return _data.Data;
                }
            }
            try
            {
                var temp = Reader.StringGet(FullName).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;

                var data = DeSerialize(temp);

                if (data != null)
                {
                    lock (_locker)
                    {
                        if (ContextConfig.KeepDataInMemory)
                            _data = data;
                        return data.Data;
                    }
                }
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}");
            }

            lock (_locker)
            {
                if (_data != null)
                    return _data.Data;
            }
            return default;
        }

        public async Task<T?> ReadAsync(bool force = false)
        {
            if (!force)
            {
                lock (_locker)
                {
                    if (_data != null)
                        return _data.Data;
                }
            }
            try
            {
                var temp = (await Reader.StringGetAsync(FullName)).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;
                var data = DeSerialize(temp);
                lock (_locker)
                    if (data != null)
                    {
                        if (ContextConfig.KeepDataInMemory)
                            _data = data;
                        return data.Data;
                    }
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}");
            }
            lock (_locker)
            {
                if (_data != null)
                    return _data.Data;
            }
            return default;
        }

        public bool Write(T d)
        {
            if (d == null) return false;
            try
            {
                var res = Writer.StringSet(FullName, Serialize(d));
                ContextConfig.Publish(this);
                if (ContextConfig.KeepDataInMemory)
                    lock (_locker)
                        _data = new RedisDataWrapper<T>(d);
                return res;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}");
                return false;
            }
        }

        public async Task<bool> WriteAsync(T d)
        {
            if (d == null) return false;
            try
            {
                var res = await Writer.StringSetAsync(FullName, Serialize(d));
                ContextConfig.Publish(this);
                if (ContextConfig.KeepDataInMemory)
                    lock (_locker)
                        _data = new RedisDataWrapper<T>(d);
                return res;
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in Writing {FullName}");
                return false;
            }
        }

        public void InvalidateCache()
        {
            lock (_locker)
                _data = null;
            //Read(true);
        }

        public RedisValue this[string key]
        {
            get => Reader.HashGet(FullName, key);
        }
    }
}
