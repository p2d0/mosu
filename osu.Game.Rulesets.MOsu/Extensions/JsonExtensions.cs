using Newtonsoft.Json;

namespace osu.Game.Rulesets.MOsu.Extensions
{
    public static class JsonExtensions
    {
        public static string ToJson<T>(this T obj) => JsonConvert.SerializeObject(obj);

        public static T? FromJson<T>(this string json) => JsonConvert.DeserializeObject<T>(json);
    }
}
