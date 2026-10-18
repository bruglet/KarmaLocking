using System.IO;
using UnityEngine;

namespace KarmaLocking
{
    // The lock badge image, embedded in the DLL so the mod doesn't depend on loose files.
    internal static class LockSprite
    {
        internal static Sprite Sprite { get; private set; }

        internal static void Load()
        {
            Sprite = LoadSprite("KarmaLocking.lock.png", "texKarmaLock");
        }

        private static Sprite LoadSprite(string resourceName, string name)
        {
            using (var stream = typeof(LockSprite).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    KarmaLockingPlugin.Log.LogError($"Embedded resource '{resourceName}' is missing.");
                    return null;
                }

                var bytes = new byte[stream.Length];
                using (var memory = new MemoryStream(bytes))
                {
                    stream.CopyTo(memory);
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = name,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                if (!texture.LoadImage(bytes))
                {
                    KarmaLockingPlugin.Log.LogError($"Could not decode '{resourceName}'.");
                    return null;
                }
                Object.DontDestroyOnLoad(texture);

                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = name;
                Object.DontDestroyOnLoad(sprite);
                return sprite;
            }
        }
    }
}
