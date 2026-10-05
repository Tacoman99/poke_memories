using System;
using System.Collections.Generic;
using UnityEngine;

namespace PokeMemories.Gameplay
{
    /// <summary>
    /// Placeholder sprites drawn in code for track items, until real art exists.
    /// Every sprite is 1x1 world unit with its pivot at the bottom centre, except the
    /// ball which is centred.
    /// </summary>
    public static class ShapeSprites
    {
        const int Size = 64;
        static readonly Dictionary<string, Sprite> Cache = new();

        public static Sprite Square => Get("square", Vector2.one * 0.5f, (x, y) => Color.white);

        public static Sprite Ball => Get("ball", Vector2.one * 0.5f, (x, y) =>
        {
            var p = new Vector2(x, y) - Vector2.one * 0.5f;
            var r = p.magnitude;
            if (r > 0.48f) return Color.clear;
            if (r > 0.43f || Mathf.Abs(p.y) < 0.04f) return new Color(0.12f, 0.1f, 0.12f);
            if (r < 0.13f) return r > 0.09f ? new Color(0.12f, 0.1f, 0.12f) : Color.white;
            return p.y > 0 ? new Color(0.9f, 0.16f, 0.25f) : Color.white;
        });

        public static Sprite Cone => Get("cone", new Vector2(0.5f, 0), (x, y) =>
        {
            if (Mathf.Abs(x - 0.5f) > 0.5f * (1 - y)) return Color.clear;
            return y > 0.35f && y < 0.5f ? Color.white : new Color(1f, 0.55f, 0.2f);
        });

        public static Sprite Kicker => Get("kicker", new Vector2(0.5f, 0), (x, y) =>
            y <= x ? new Color(0.98f, 0.75f, 0.85f) : Color.clear);

        public static Sprite Barrier => Get("barrier", new Vector2(0.5f, 0), (x, y) =>
        {
            var leg = (x < 0.14f || x > 0.86f) && y < 0.55f;
            var board = y > 0.45f;
            if (!leg && !board) return Color.clear;
            if (leg) return new Color(0.35f, 0.3f, 0.35f);
            return ((int)((x + y) * 6) % 2 == 0) ? new Color(0.93f, 0.25f, 0.5f) : Color.white;
        });

        static Sprite Get(string name, Vector2 pivot, Func<float, float, Color> paint)
        {
            if (Cache.TryGetValue(name, out var sprite)) return sprite;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[Size * Size];
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                pixels[y * Size + x] = paint((x + 0.5f) / Size, (y + 0.5f) / Size);
            texture.SetPixels(pixels);
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), pivot, Size);
            sprite.name = name;
            Cache[name] = sprite;
            return sprite;
        }
    }
}
