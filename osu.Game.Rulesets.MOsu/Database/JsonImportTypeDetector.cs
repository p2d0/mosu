using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace osu.Game.Rulesets.MOsu.Database
{
    public enum JsonImportType
    {
        Unknown,
        Presets,
        Collections,
    }

    /// <summary>
    /// Detects whether a JSON document contains mod presets or beatmap collections,
    /// based on the shape of its first array element.
    /// </summary>
    public static class JsonImportTypeDetector
    {
        public static JsonImportType Detect(string json)
        {
            try
            {
                if (JToken.Parse(json) is JArray { Count: > 0 } array && array[0] is JObject first)
                {
                    if (first.ContainsKey("Beatmaps"))
                        return JsonImportType.Collections;
                    if (first.ContainsKey("ModsJson"))
                        return JsonImportType.Presets;
                }
            }
            catch (JsonException)
            {
            }

            return JsonImportType.Unknown;
        }
    }
}
