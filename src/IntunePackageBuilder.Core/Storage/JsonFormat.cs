using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace IntunePackageBuilder.Core.Storage
{
    /// <summary>Shared JSON settings: camelCase names, enums as strings, indented, UTC dates.</summary>
    public static class JsonFormat
    {
        public static JsonSerializerSettings CreateSettings()
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                DateParseHandling = DateParseHandling.None,
                // Lists initialized with defaults in a constructor must be replaced by the stored values,
                // otherwise every load would append to the defaults.
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                Converters = { new StringEnumConverter() }
            };
        }

        /// <summary>
        /// Parses a JSON object without turning date-like strings into dates, so free text such as
        /// notes is never reformatted.
        /// </summary>
        public static JObject ParseObject(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                return JObject.Load(reader);
            }
        }

        public static JsonSerializer CreateSerializer()
        {
            return JsonSerializer.Create(CreateSettings());
        }
    }
}
