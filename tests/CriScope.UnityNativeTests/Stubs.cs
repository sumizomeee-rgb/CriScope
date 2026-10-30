// Only the serializer is used by the real managed bridge. No Unity or native CRI library is loaded.
namespace UnityEngine
{
    public static class JsonUtility
    {
        public static string ToJson(object value) => System.Text.Json.JsonSerializer.Serialize(value,
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
}
namespace CriWare
{
    public static class CriAtomPlugin
    {
        public static bool IsLibraryInitialized() => false;
    }
}
