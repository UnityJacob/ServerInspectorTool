using System;
using UnityEngine;

namespace Unity.Networking.QoS
{
    /// <summary>
    /// Helper class for serializing/deserializing JSON arrays in Unity
    /// </summary>
    /// <remarks>
    /// See https://stackoverflow.com/questions/36239705/serialize-and-deserialize-json-and-json-array-in-unity/36244111#36244111
    /// Modified so Wrapper class is specified as a generic.  It should look something like this:
    ///
    ///    [Serializable]
    ///    class Wrapper<T>
    ///    {
    ///        public T[] Name;
    ///    }
    ///
    /// where Name is the name of the JSON array to serialize to or deserialize from.
    /// </remarks>
    public static class JsonHelper
    {
        public static W FromJson<W>(string json)
        {
            W wrapper = JsonUtility.FromJson<W>(json);
            return wrapper;
        }

        public static string ToJson<W>(W wrapper, bool prettyPrint = false)
        {
            return JsonUtility.ToJson(wrapper, prettyPrint);
        }
    }
}