using System;
using System.Collections.Generic;
#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace CamboBIM.Revit2024.Addin
{
    internal static class CamboBimJson
    {
        public static Dictionary<string, object> DeserializeMap(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

#if NETFRAMEWORK
            return CreateJavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
#else
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                return ConvertJsonElement(document.RootElement) as Dictionary<string, object>;
            }
#endif
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default(T);
            }

#if NETFRAMEWORK
            return CreateJavaScriptSerializer().Deserialize<T>(json);
#else
            return JsonSerializer.Deserialize<T>(json, CreateJsonSerializerOptions());
#endif
        }

        public static string Serialize(object value)
        {
#if NETFRAMEWORK
            return CreateJavaScriptSerializer().Serialize(value);
#else
            return JsonSerializer.Serialize(value, CreateJsonSerializerOptions());
#endif
        }

#if NETFRAMEWORK
        private static JavaScriptSerializer CreateJavaScriptSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = 4 * 1024 * 1024
            };
        }
#else
        private static JsonSerializerOptions CreateJsonSerializerOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };
        }

        private static object ConvertJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        map[property.Name] = ConvertJsonElement(property.Value);
                    }

                    return map;

                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        list.Add(ConvertJsonElement(item));
                    }

                    return list;

                case JsonValueKind.String:
                    return element.GetString();

                case JsonValueKind.Number:
                    long longValue;
                    if (element.TryGetInt64(out longValue))
                    {
                        return longValue;
                    }

                    decimal decimalValue;
                    if (element.TryGetDecimal(out decimalValue))
                    {
                        return decimalValue;
                    }

                    double doubleValue;
                    if (element.TryGetDouble(out doubleValue))
                    {
                        return doubleValue;
                    }

                    return element.ToString();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                default:
                    return null;
            }
        }
#endif
    }
}
