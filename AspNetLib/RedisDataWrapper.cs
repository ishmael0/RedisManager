using System.Globalization;

namespace Modules.RedisModule
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
        public RedisDataWrapper(T data)
        {
            Data = data;
            DateTime = DateTime.Now;
            var pc = new PersianCalendar();
            PersianLastUpdate = $"{pc.GetYear(DateTime)}/{pc.GetMonth(DateTime)}/{pc.GetDayOfMonth(DateTime)} - {pc.GetHour(DateTime)}:{pc.GetMinute(DateTime)}"; ;
        }
        /// <summary>
        /// Actual stored value.
        /// </summary>
        public T Data { set; get; }
        /// <summary>
        /// Timestamp captured at construction time.
        /// </summary>
        public DateTime DateTime { set; get; }
        /// <summary>
        /// Persian formatted timestamp (yyyy/M/d - H:m).
        /// </summary>
        public string PersianLastUpdate { set; get; }
    }
}
