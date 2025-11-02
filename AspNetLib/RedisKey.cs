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
            var keyMemoryUsage = Reader.Execute("MEMORY", "USAGE", FullName);
            return keyMemoryUsage.IsNull ? 0 : Convert.ToInt64(keyMemoryUsage.ToString());
        }


        
        public RedisDataWrapper<T>? ReadFull(bool force = false)
        {
            if (!force && _data != null)
                return _data;
            try
            {
                lock (_locker)
                {
                    var temp = Reader.StringGet(FullName).ToString();
                    if (string.IsNullOrEmpty(temp))
                        return default;
                    var data = DeSerialize(temp);
                    if (data != null)
                    {
                        if (ContextConfig.KeepDataInMemory)
                            _data = data;
                        return data;
                    }
                }
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}");
            }
            if (_data != null)
                return _data;
            return default;
        }

        
        public T? Read(bool force = false)
        {
            var d = ReadFull(force);
            return d == null ? default : d.Data;
        }
        
        public async Task<T?> ReadAsync(bool force = false)
        {
            if (!force && _data != null)
                return _data.Data;
            try
            {
                var temp = (await Reader.StringGetAsync(FullName)).ToString();
                if (string.IsNullOrEmpty(temp))
                    return default;
                var data = DeSerialize(temp);
                lock (_locker)
                    if (data != null)
                    {
                        if (ContextConfig. KeepDataInMemory)
                            _data = data;
                        return data.Data;
                    }
            }
            catch (Exception e)
            {
                ContextConfig.Logger?.LogError(e, $"In RedisManager, in reading {FullName}");
            }
            if (_data != null)
                return _data.Data;
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
                if (ContextConfig.KeepDataInMemory)
                    lock (_locker)
                        _data = new RedisDataWrapper<T>(d);
                ContextConfig.Publish(this);
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
