using System.Collections.Generic;
using UnityEngine;

namespace PokeMemories.Menu
{
    /// <summary>
    /// The hand-crafted look of the menus and the memory book: bundled OFL fonts and a set of
    /// textures generated in code (paper fibres, book cloth, washi tape, soft shadows, glows) so the
    /// UI has real material instead of flat fills. Everything is built once, lazily, at first use.
    /// </summary>
    public static class Look
    {
        public static Font Title, Hand, HandBold, Script;
        /// <summary>True off the course (menu, book) so the in-run atmosphere layer stays hidden.</summary>
        public static bool BookOpen = true;
        public static Texture2D GradH, Paper, Cloth, Shadow9, Glow, GradV, Felt, Vignette, Sparkle, Heart;
        public static Texture2D[] Washi;

        static bool built;
        const int ShadowBorder = 24;

        public static void Ensure()
        {
            if (built) return;
            built = true;
            Title = Resources.Load<Font>("Fonts/Pacifico-Regular");
            Hand = Resources.Load<Font>("Fonts/Kalam-Regular");
            HandBold = Resources.Load<Font>("Fonts/Kalam-Bold");
            Script = Resources.Load<Font>("Fonts/PatrickHand-Regular");

            var rng = new System.Random(7);
            Paper = MakePaper(512, rng);
            Cloth = MakeCloth(256, rng);
            Felt = MakeFelt(256, rng);
            Shadow9 = MakeShadow();
            Glow = MakeRadial(128, 2.2f);
            Vignette = MakeRadial(128, 1.0f);
            GradV = MakeGradV();
            GradH = MakeGradH();
            Sparkle = MakeSparkle(64);
            Heart = MakeHeart(128);
            Washi = new[]
            {
                MakeWashi(UIKit.Hex("#f59e0b"), 1, rng), MakeWashi(UIKit.Hex("#ec4899"), 2, rng),
                MakeWashi(UIKit.Hex("#60a5fa"), 0, rng), MakeWashi(UIKit.Hex("#34d399"), 2, rng),
                MakeWashi(UIKit.Hex("#a78bfa"), 1, rng),
            };
        }

        static Texture2D New(int w, int h, bool repeat = false) => new(w, h, TextureFormat.RGBA32, true)
        {
            wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4,
        };

        static float Fbm(float x, float y, int oct)
        {
            float sum = 0, amp = 0.5f, f = 1;
            for (var i = 0; i < oct; i++) { sum += (Mathf.PerlinNoise(x * f + 31.7f * i, y * f + 11.3f * i) - 0.5f) * amp; amp *= 0.5f; f *= 2; }
            return sum;
        }

        /// <summary>Warm off-white paper: mottled tone, faint fibres and fine grain. Tinted at draw time.</summary>
        static Texture2D MakePaper(int n, System.Random rng)
        {
            var px = new Color32[n * n];
            var fib = new float[n * n];
            for (var i = 0; i < 1800; i++)
            {
                float x = (float)rng.NextDouble() * n, y = (float)rng.NextDouble() * n;
                var a = (float)rng.NextDouble() * Mathf.PI;
                var len = 4 + (float)rng.NextDouble() * 14;
                var dark = rng.NextDouble() < 0.5 ? 1 : -1;
                for (var t = 0f; t < len; t += 0.7f)
                {
                    var cx = (int)(x + Mathf.Cos(a + t * 0.05f) * t);
                    var cy = (int)(y + Mathf.Sin(a + t * 0.05f) * t);
                    if (cx >= 0 && cx < n && cy >= 0 && cy < n) fib[cy * n + cx] += 0.035f * dark;
                }
            }
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var v = 0.955f + Fbm(x * 0.012f, y * 0.012f, 4) * 0.09f + fib[y * n + x] + ((float)rng.NextDouble() - 0.5f) * 0.03f;
                    var c = Mathf.Clamp01(v);
                    px[y * n + x] = new Color(c, c * 0.992f, c * 0.975f, 1f);
                }
            var tex = New(n, n, true);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Bookbinding cloth: a woven crosshatch with uneven dye. Tinted at draw time.</summary>
        static Texture2D MakeCloth(int n, System.Random rng)
        {
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var warp = Mathf.Sin(x * Mathf.PI * 2f / 3f) * 0.5f + 0.5f;
                    var weft = Mathf.Sin(y * Mathf.PI * 2f / 3f) * 0.5f + 0.5f;
                    var weave = (x / 3 + y / 3) % 2 == 0 ? warp : weft;
                    var v = 0.78f + weave * 0.13f + Fbm(x * 0.02f, y * 0.02f, 3) * 0.28f + ((float)rng.NextDouble() - 0.5f) * 0.06f;
                    var c = Mathf.Clamp01(v);
                    px[y * n + x] = new Color(c, c, c, 1);
                }
            var tex = New(n, n, true);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Texture2D MakeFelt(int n, System.Random rng)
        {
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var v = 0.82f + Fbm(x * 0.03f, y * 0.03f, 3) * 0.3f + ((float)rng.NextDouble() - 0.5f) * 0.1f;
                    var c = Mathf.Clamp01(v);
                    px[y * n + x] = new Color(c, c, c, 1);
                }
            var tex = New(n, n, true);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>A blurred square for nine-slice soft shadows.</summary>
        static Texture2D MakeShadow()
        {
            const int n = 96;
            var a = new float[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                    a[y * n + x] = (x > ShadowBorder && x < n - ShadowBorder && y > ShadowBorder && y < n - ShadowBorder) ? 1 : 0;
            for (var pass = 0; pass < 3; pass++) a = BoxBlur(a, n, 7);
            var px = new Color32[n * n];
            for (var i = 0; i < a.Length; i++) px[i] = new Color(1, 1, 1, a[i]);
            var tex = New(n, n);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static float[] BoxBlur(float[] src, int n, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float s = 0;
                    for (var k = -r; k <= r; k++) s += src[y * n + Mathf.Clamp(x + k, 0, n - 1)];
                    tmp[y * n + x] = s / (2 * r + 1);
                }
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float s = 0;
                    for (var k = -r; k <= r; k++) s += tmp[Mathf.Clamp(y + k, 0, n - 1) * n + x];
                    dst[y * n + x] = s / (2 * r + 1);
                }
            return dst;
        }

        static Texture2D MakeRadial(int n, float power)
        {
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var d = Mathf.Sqrt(Mathf.Pow((x - n / 2f + 0.5f) / (n / 2f), 2) + Mathf.Pow((y - n / 2f + 0.5f) / (n / 2f), 2));
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - d), power));
                }
            var tex = New(n, n);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // Texture row 0 is the bottom: alpha runs clear at the bottom to opaque at the top.
        static Texture2D MakeGradV()
        {
            var px = new Color32[64];
            for (var y = 0; y < 64; y++) px[y] = new Color(1, 1, 1, (y + 0.5f) / 64f);
            var tex = New(1, 64);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Texture2D MakeGradH()
        {
            var px = new Color32[64];
            for (var x = 0; x < 64; x++) px[x] = new Color(1, 1, 1, (x + 0.5f) / 64f);
            var tex = New(64, 1);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Texture2D MakeSparkle(int n)
        {
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float dx = Mathf.Abs(x - n / 2f + 0.5f) / (n / 2f), dy = Mathf.Abs(y - n / 2f + 0.5f) / (n / 2f);
                    var star = Mathf.Clamp01(1 - Mathf.Pow(Mathf.Sqrt(dx) + Mathf.Sqrt(dy), 2f));
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Pow(star, 0.8f));
                }
            var tex = New(n, n);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static Texture2D MakeHeart(int n)
        {
            // Classic parametric heart, rasterised with 4x4 supersampling for clean edges.
            const int pts = 160;
            var poly = new Vector2[pts];
            for (var i = 0; i < pts; i++)
            {
                var t = i / (float)pts * Mathf.PI * 2;
                poly[i] = new Vector2(16 * Mathf.Pow(Mathf.Sin(t), 3),
                    13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t));
            }
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var hit = 0;
                    for (var sy = 0; sy < 4; sy++)
                        for (var sx = 0; sx < 4; sx++)
                        {
                            var p = new Vector2(((x + (sx + 0.5f) / 4f) / n - 0.5f) * 38f, ((y + (sy + 0.5f) / 4f) / n - 0.5f) * 38f + 1.2f);
                            if (Inside(poly, p)) hit++;
                        }
                    px[y * n + x] = new Color(1, 1, 1, hit / 16f);
                }
            var tex = New(n, n);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            var inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>A strip of washi tape with torn zig-zag ends. pattern 0 plain, 1 stripes, 2 dots.</summary>
        static Texture2D MakeWashi(Color baseColour, int pattern, System.Random rng)
        {
            const int w = 128, h = 40;
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var zig = y % 6 < 3 ? 0 : 2;
                    var inset = 3 + zig;
                    var cut = x < inset || x > w - 1 - inset;
                    var shade = 1f + (Mathf.PerlinNoise(x * 0.15f, y * 0.4f) - 0.5f) * 0.18f;
                    if (pattern == 1 && (x / 6) % 2 == 0) shade += 0.18f;
                    if (pattern == 2)
                    {
                        float dx = (x % 14) - 7, dy = (y % 14) - 7;
                        if (dx * dx + dy * dy < 6) shade += 0.3f;
                    }
                    var c = new Color(Mathf.Clamp01(baseColour.r * shade + 0.05f), Mathf.Clamp01(baseColour.g * shade + 0.05f),
                        Mathf.Clamp01(baseColour.b * shade + 0.05f), cut ? 0 : (y < 1 || y > h - 2 ? 0.55f : 0.8f));
                    px[y * w + x] = c;
                }
            var tex = New(w, h);
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // ───────────── Drawing ─────────────

        static Vector4 Radii(float r) => new(r, r, r, r);

        /// <summary>Anti-aliased filled rounded rectangle.</summary>
        public static void Round(Rect rect, Color colour, float radius)
        {
            var r = Mathf.Min(radius, rect.height / 2, rect.width / 2);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, colour, Vector4.zero, Radii(r));
        }

        public static void RoundOutline(Rect rect, Color colour, float radius, float width) =>
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, colour, new Vector4(width, width, width, width), Radii(radius));

        /// <summary>A soft drop shadow under `rect`, spreading `blur` pixels past its edges.</summary>
        public static void Shadow(Rect rect, float blur, float alpha, Vector2 offset)
        {
            var r = new Rect(rect.x - blur + offset.x, rect.y - blur + offset.y, rect.width + 2 * blur, rect.height + 2 * blur);
            NineSlice(r, Shadow9, ShadowBorder, Mathf.Min(blur * 1.9f, r.width / 2.05f, r.height / 2.05f), UIKit.WithAlpha(Color.black, alpha));
        }

        public static void NineSlice(Rect dst, Texture2D tex, float srcBorder, float dstBorder, Color tint)
        {
            var old = GUI.color;
            GUI.color = tint;
            var u = srcBorder / tex.width;
            float[] sx = { dst.x, dst.x + dstBorder, dst.xMax - dstBorder, dst.xMax };
            float[] sy = { dst.y, dst.y + dstBorder, dst.yMax - dstBorder, dst.yMax };
            float[] t = { 0, u, 1 - u, 1 };
            for (var j = 0; j < 3; j++)
                for (var i = 0; i < 3; i++)
                {
                    // Texture v runs bottom-up, so screen row 0 samples the top of the texture.
                    var uv = new Rect(t[i], 1 - t[j + 1], t[i + 1] - t[i], t[j + 1] - t[j]);
                    GUI.DrawTextureWithTexCoords(new Rect(sx[i], sy[j], sx[i + 1] - sx[i], sy[j + 1] - sy[j]), tex, uv);
                }
            GUI.color = old;
        }

        /// <summary>A texture repeated across `rect`, one tile per `tilePx` screen pixels.</summary>
        public static void Tiled(Rect rect, Texture2D tex, float tilePx, Color tint)
        {
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, tex, new Rect(0, 0, rect.width / tilePx, rect.height / tilePx));
            GUI.color = old;
        }

        public static void Tex(Rect rect, Texture2D tex, Color tint)
        {
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        /// <summary>`colour` at the top of `rect` fading to nothing at the bottom.</summary>
        public static void FadeDown(Rect rect, Color colour) => Tex(rect, GradV, colour);

        /// <summary>`colour` at the bottom of `rect` fading to nothing at the top.</summary>
        public static void FadeUp(Rect rect, Color colour)
        {
            var m = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(1, -1), rect.center);
            Tex(rect, GradV, colour);
            GUI.matrix = m;
        }

        /// <summary>`colour` at the left edge fading to nothing at the right.</summary>
        public static void FadeRight(Rect rect, Color colour)
        {
            var old = GUI.color;
            GUI.color = colour;
            GUI.DrawTextureWithTexCoords(rect, GradH, new Rect(1, 0, -1, 1));
            GUI.color = old;
        }

        /// <summary>`colour` at the right edge fading to nothing at the left.</summary>
        public static void FadeLeft(Rect rect, Color colour)
        {
            var old = GUI.color;
            GUI.color = colour;
            GUI.DrawTextureWithTexCoords(rect, GradH, new Rect(0, 0, 1, 1));
            GUI.color = old;
        }

        /// <summary>A small flat pokeball icon, drawn from rounded shapes.</summary>
        public static void Ball(Rect r)
        {
            var ink = UIKit.Ink;
            Round(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3, r.height + 3), ink, r.width);
            GUI.BeginClip(new Rect(r.x, r.y, r.width, r.height / 2));
            Round(new Rect(0, 0, r.width, r.height), UIKit.Rose600, r.width);
            GUI.EndClip();
            GUI.BeginClip(new Rect(r.x, r.y + r.height / 2, r.width, r.height / 2));
            Round(new Rect(0, -r.height / 2, r.width, r.height), Color.white, r.width);
            GUI.EndClip();
            UIKit.Fill(new Rect(r.x, r.center.y - r.height * 0.07f, r.width, r.height * 0.14f), ink);
            Round(new Rect(r.center.x - r.width * 0.17f, r.center.y - r.width * 0.17f, r.width * 0.34f, r.width * 0.34f), ink, r.width);
            Round(new Rect(r.center.x - r.width * 0.1f, r.center.y - r.width * 0.1f, r.width * 0.2f, r.width * 0.2f), Color.white, r.width);
            Round(new Rect(r.x + r.width * 0.2f, r.y + r.height * 0.14f, r.width * 0.26f, r.height * 0.14f), UIKit.WithAlpha(Color.white, 0.55f), r.width);
        }

        static Sprite sparkleSprite;

        /// <summary>The four-point sparkle as a world sprite (0.64 units across at scale 1).</summary>
        public static Sprite SparkleSprite
        {
            get
            {
                Ensure();
                return sparkleSprite ??= Sprite.Create(Sparkle, new Rect(0, 0, Sparkle.width, Sparkle.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        static Sprite glowSprite;

        /// <summary>The soft radial glow as a world sprite (1.28 units across at scale 1).</summary>
        public static Sprite GlowSprite
        {
            get
            {
                Ensure();
                return glowSprite ??= Sprite.Create(Glow, new Rect(0, 0, Glow.width, Glow.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        public static void HeartAt(Vector2 centre, float size, Color colour) =>
            Tex(new Rect(centre.x - size / 2, centre.y - size / 2, size, size), Heart, colour);

        // ───────────── Motion ─────────────

        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1;
            return 1 + c3 * Mathf.Pow(t - 1, 3) + c1 * Mathf.Pow(t - 1, 2);
        }

        public static float EaseOutCubic(float t) => 1 - Mathf.Pow(1 - Mathf.Clamp01(t), 3);

        /// <summary>Frame-rate independent exponential approach, used for hover/press springs.</summary>
        public static float Approach(float value, float target, float speed) =>
            Mathf.Lerp(value, target, 1 - Mathf.Exp(-speed * Time.unscaledDeltaTime));

        static readonly Dictionary<string, float> springs = new();

        public static float Spring(string key, float target, float speed = 18f)
        {
            springs.TryGetValue(key, out var v);
            v = Approach(v, target, speed);
            springs[key] = v;
            return v;
        }
    }
}
