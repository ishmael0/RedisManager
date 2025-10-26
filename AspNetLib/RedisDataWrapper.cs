using System.Globalization;

namespace Santel.Redis.TypedKeys
{
    /// <summary>
    /// Wraps a data value with metadata such as capture timestamp and a pre-formatted Persian date string.
    /// Used to store additional context alongside cached / serialized Redis values.
    /// </summary>
    /// <typeparam name="T">Type of the wrapped data value.</typeparam>
    public class RedisDataWrapper<T>
    {
        /// <summary>
        /// Creates a new wrapper around the provided data, capturing the current time and Persian date.
        /// </summary>
        /// <param name="data">Payload value to wrap.</param>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        public RedisDataWrapper(T data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            DateTime = DateTime.UtcNow;
            var pc = new PersianCalendar();
            PersianLastUpdate = $"{pc.GetYear(DateTime):0000}/{pc.GetMonth(DateTime):00}/{pc.GetDayOfMonth(DateTime):00} - {pc.GetHour(DateTime):00}:{pc.GetMinute(DateTime):00}";
        }

        /// <summary>
        /// Actual stored value.
        /// </summary>
        public T Data { get; set; }

        /// <summary>
        /// UTC timestamp captured at construction time.
        /// </summary>
        public DateTime DateTime { get; set; }

        /// <summary>
        /// Persian formatted timestamp (yyyy/MM/dd - HH:mm).
        /// </summary>
        public string PersianLastUpdate { get; set; }
    }
}
