using System;
using System.Threading.Tasks;
using UnityEngine;

namespace PokeMemories.Gameplay
{
    /// <summary>
    /// Procedural artwork for the bowl scene, painted in code from the simulation's own geometry so
    /// the picture can never drift from the physics: the skate bowl cross-section with its pool-tile
    /// band, steel coping and painted heart, plus bunting, rings and soft blobs. The big scene
    /// texture is computed on a worker thread while the menu is showing, then uploaded on first use.
    /// Pure arithmetic only (no UnityEngine API) inside the worker.
    /// </summary>
    public static class BowlArt
    {
        // Scene texture extents in simulation pixels, and texels per simulation pixel.
        public const float HalfWidth = 860, Bottom = -150, Top = 190, Texels = 2f;

        static Task<Color32[]> sceneTask;
        static Sprite scene, bunting, ring, blob;
        static int sceneWidth, sceneHeight;

        public static void BeginBuildingScene()
        {
            if (sceneTask != null || scene != null) return;
            sceneWidth = Mathf.RoundToInt(HalfWidth * 2 * Texels);
            sceneHeight = Mathf.RoundToInt((Top - Bottom) * Texels);
            int w = sceneWidth, h = sceneHeight;
#if UNITY_WEBGL
            sceneTask = Task.FromResult(PaintScene(w, h));
#else
            sceneTask = Task.Run(() => PaintScene(w, h));
#endif
        }

        public static void Release() { }

        /// <summary>The bowl and decks. Pivot sits at bowl-local (0, 0): the centre of the floor.</summary>
        public static Sprite Scene()
        {
            if (scene != null) return scene;
            BeginBuildingScene();
            var pixels = sceneTask.Result;
            var tex = new Texture2D(sceneWidth, sceneHeight, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4, name = "BowlScene",
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            var pivot = new Vector2(0.5f, -Bottom * Texels / sceneHeight);
            scene = Sprite.Create(tex, new Rect(0, 0, sceneWidth, sceneHeight), pivot, Texels / 0.0146f, 0, SpriteMeshType.FullRect);
            sceneTask = null;
            return scene;
        }

        public static Sprite Ring() => ring != null ? ring : ring = MakeRing(256, 0.82f, 0.96f);
        public static Sprite Blob() => blob != null ? blob : blob = MakeBlob(128);
        public static Sprite Bunting() => bunting != null ? bunting : bunting = MakeBunting();

        // ───────────── Worker-thread scene painting ─────────────

        struct Rgb
        {
            public float r, g, b;
            public Rgb(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }
            public static Rgb Hex(int hex) => new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
            public static Rgb Lerp(Rgb a, Rgb b, float t) => new(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);
            public static Rgb operator *(Rgb a, float k) => new(a.r * k, a.g * k, a.b * k);
            public static Rgb operator +(Rgb a, float k) => new(a.r + k, a.g + k, a.b + k);
        }

        static float Smooth(float a, float b, float x)
        {
            var t = Math.Clamp((x - a) / (b - a), 0f, 1f);
            return t * t * (3 - 2 * t);
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                var n = (uint)(x * 374761393 + y * 668265263);
                n = (n ^ (n >> 13)) * 1274126177u;
                return ((n ^ (n >> 16)) & 0xFFFFFF) / (float)0x1000000;
            }
        }

        // Smooth value noise.
        static float Noise(float x, float y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            var a = Hash(xi, yi) + (Hash(xi + 1, yi) - Hash(xi, yi)) * fx;
            var b = Hash(xi, yi + 1) + (Hash(xi + 1, yi + 1) - Hash(xi, yi + 1)) * fx;
            return a + (b - a) * fy;
        }

        // Height of the riding surface above the floor at horizontal offset ax (>= 0).
        static float SurfaceHeight(float ax, out float cosTheta)
        {
            const float flat = BowlSimulation.Flat, radius = BowlSimulation.Radius;
            if (ax <= flat) { cosTheta = 1; return 0; }
            var sin = Math.Min(1f, (ax - flat) / radius);
            var cos = (float)Math.Sqrt(1 - sin * sin);
            cosTheta = Math.Max(0.12f, cos);
            return radius * (1 - cos);
        }

        static bool InsideHeart(float x, float y)
        {
            // Classic implicit heart, x/y in roughly [-1.3, 1.3].
            var a = x * x + y * y - 1;
            return a * a * a - x * x * y * y * y <= 0;
        }

        static Color32[] PaintScene(int width, int height)
        {
            var pixels = new Color32[width * height];
            var copingX = BowlSimulation.Coping.x;
            var copingH = BowlSimulation.Coping.h;

            // Palette: sun-faded pink concrete, peach back wall, pool-tile blue, steel coping.
            var deckA = Rgb.Hex(0xDDBCC8); var deckB = Rgb.Hex(0xB68FA2);
            var massA = Rgb.Hex(0xB48A9E); var massB = Rgb.Hex(0x86596F);
            var wallLow = Rgb.Hex(0xFFDCC8); var wallHigh = Rgb.Hex(0xEC8FAB);
            var tileA = Rgb.Hex(0x7FD3E3); var tileB = Rgb.Hex(0xA8E6F0); var grout = Rgb.Hex(0xE8F6F8);
            var line = Rgb.Hex(0x6B3150); var lipLight = Rgb.Hex(0xFFF6EE);
            var heartCol = Rgb.Hex(0xFB7185); var heartHi = Rgb.Hex(0xFFB3C1);
            var ground = new Rgb(0.53f, 0.35f, 0.44f); // keep in step with BowlGame.BowlGroundColour

            for (var j = 0; j < height; j++)
            {
                var h = Bottom + (j + 0.5f) / Texels;
                for (var i = 0; i < width; i++)
                {
                    var x = (i + 0.5f) / Texels - HalfWidth;
                    var ax = Math.Abs(x);
                    Rgb c;
                    float a = 1;

                    // Coping pipe: steel tube whose centre is the lip the skater launches from.
                    var pipeDx = ax - copingX; var pipeDy = h - copingH;
                    var pipeDist = (float)Math.Sqrt(pipeDx * pipeDx + pipeDy * pipeDy);
                    const float pipeR = 9f;

                    if (h > copingH && pipeDist > pipeR + 1)
                    {
                        pixels[j * width + i] = default;
                        continue;
                    }

                    if (ax > copingX)
                    {
                        // Deck slab seen from the side: lit top curb, slab joints, grain, deepening shade.
                        var depth = Smooth(0, 260, copingH - h);
                        c = Rgb.Lerp(deckA, deckB, depth);
                        var grain = (Noise(x * 0.55f, h * 0.55f) - 0.5f) * 0.07f + (Noise(x * 0.09f, h * 0.09f) - 0.5f) * 0.06f;
                        c += grain;
                        var joint = Math.Abs(((ax - copingX + 80) % 170) - 85);
                        if (joint < 1.4f) c *= 0.9f;
                        var curb = copingH - h;
                        if (curb < 18) c = Rgb.Lerp(c, lipLight, 0.45f * (1 - curb / 18f));
                        if (curb is > 17 and < 20.5f) c *= 0.86f;
                    }
                    else
                    {
                        var surface = SurfaceHeight(ax, out var cosT);
                        var d = h - surface;
                        var e = d * cosT; // distance perpendicular to the riding surface
                        if (d < 0)
                        {
                            // Cut section through the bowl mass: poured-concrete strata under the painted shell.
                            var strata = (Noise(x * 0.35f, h * 0.35f) - 0.5f) * 0.16f + (Hash((int)(x * 0.6f), (int)(h * 0.6f)) > 0.93f ? 0.07f : 0f);
                            c = Rgb.Lerp(massA, massB, Smooth(0, 120, -e));
                            c += strata + (Noise(x * 0.7f, h * 0.7f) - 0.5f) * 0.05f;
                            if (-e < 9) c = Rgb.Lerp(Rgb.Hex(0xE6C7D2), c, Smooth(5, 9, -e));
                        }
                        else
                        {
                            // Back wall of the bowl: a warm vertical wash with a pool-tile band under the lip.
                            var t = Math.Clamp(h / copingH, 0f, 1f);
                            c = Rgb.Lerp(wallLow, wallHigh, Smooth(0.05f, 1f, t));
                            // Faint seams echoing the curve of the bowl.
                            var seam = Math.Abs(((e * 0.5f) % 15f) - 7.5f);
                            if (e > 4 && seam < 0.5f) c *= 0.965f;
                            c += (Noise(x * 0.6f, h * 0.6f) - 0.5f) * 0.035f;

                            // Pool tiles.
                            var tileTop = copingH - 7; var tileBottom = copingH - 31;
                            if (h < tileTop && h > tileBottom)
                            {
                                var col = (int)Math.Floor((x + 4000) / 8f); var row = (int)Math.Floor((h + 4000) / 8f);
                                var fx = (x + 4000) % 8f; var fy = (h + 4000) % 8f;
                                var tile = Rgb.Lerp(tileA, tileB, Hash(col, row) * 0.8f);
                                var edge = Math.Min(Math.Min(fx, 8 - fx), Math.Min(fy, 8 - fy));
                                c = Rgb.Lerp(grout, tile, Smooth(0.4f, 1.2f, edge));
                                c *= 0.94f + 0.06f * Smooth(tileBottom, tileTop, h);
                            }

                            // Mural: a big painted heart with a highlight, centred on the back wall.
                            var hx = x / 62f; var hy = (h - 92f) / 54f;
                            if (InsideHeart(hx, hy))
                            {
                                var hl = Smooth(0.2f, 1f, (-hx * 0.6f + hy * 0.8f + 1f) / 2f);
                                c = Rgb.Lerp(heartCol, heartHi, hl * 0.55f);
                                c += (Noise(x * 0.8f, h * 0.8f) - 0.5f) * 0.03f;
                            }
                            else if (InsideHeart(hx * 0.9f, (hy - 0.02f) * 0.9f))
                            {
                                c = Rgb.Lerp(c, Rgb.Hex(0xFFF1E8), 0.65f);
                            }

                            // Ambient occlusion near the riding surface and under the lip.
                            c *= 0.82f + 0.18f * Smooth(0, 30, e);
                            c *= 0.92f + 0.08f * Smooth(0, 20, copingH - h);
                            // Riding-surface edge: dark line with a bright lip just inside it.
                            if (e < 3.2f) c = Rgb.Lerp(line, c, Smooth(1.2f, 3.2f, e));
                            else if (e < 6.5f) c = Rgb.Lerp(c, lipLight, 0.55f * (1 - Smooth(3.2f, 6.5f, e)));
                        }
                    }

                    // Steel pipe on top, with a tube shading and a specular streak.
                    if (pipeDist <= pipeR + 1)
                    {
                        var shade = Math.Clamp(0.5f - pipeDy / (2 * pipeR) + (ax > copingX ? 0f : 0f), 0f, 1f);
                        var steel = Rgb.Lerp(Rgb.Hex(0x7C879B), Rgb.Hex(0xF5F8FC), Smooth(0, 1, shade));
                        var spec = Smooth(0.55f, 0.95f, 1 - Math.Abs((pipeDx * -0.5f + pipeDy * 0.85f) / pipeR - 0.45f) * 2.2f);
                        steel = Rgb.Lerp(steel, Rgb.Hex(0xFFFFFF), spec * 0.5f);
                        var cover = 1 - Smooth(pipeR - 0.8f, pipeR + 0.8f, pipeDist);
                        c = Rgb.Lerp(c, steel, cover);
                        if (h > copingH) a = cover;
                    }

                    // Everything fades into one flat ground colour at the bottom edge, where a plain fill continues it.
                    c = Rgb.Lerp(c, ground, Smooth(-70, -135, h));
                    pixels[j * width + i] = new Color32(
                        (byte)(Math.Clamp(c.r, 0f, 1f) * 255), (byte)(Math.Clamp(c.g, 0f, 1f) * 255), (byte)(Math.Clamp(c.b, 0f, 1f) * 255), (byte)(a * 255));
                }
            }
            return pixels;
        }

        // ───────────── Small sprites (main thread) ─────────────

        static Texture2D NewTex(int w, int h) => new(w, h, TextureFormat.RGBA32, true)
        { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 2 };

        static Sprite MakeRing(int size, float inner, float outer)
        {
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + 0.5f) / size * 2 - 1; var dy = (y + 0.5f) / size * 2 - 1;
                    var r = (float)Math.Sqrt(dx * dx + dy * dy);
                    var a = Smooth(inner - 0.04f, inner, r) * (1 - Smooth(outer, outer + 0.04f, r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / 2f);
        }

        static Sprite MakeBlob(int size)
        {
            var tex = NewTex(size, size);
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + 0.5f) / size * 2 - 1; var dy = (y + 0.5f) / size * 2 - 1;
                    var r = (float)Math.Sqrt(dx * dx + dy * dy);
                    var a = 1 - Smooth(0.15f, 1f, r);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / 2f);
        }

        // A string of paper pennants taped along a sagging line. 2 texels per sim pixel like the scene.
        static Sprite MakeBunting()
        {
            const int w = 1500, h = 300;
            var tex = NewTex(w, h);
            var px = new Color32[w * h];
            var colours = new[] { 0xFB7185, 0xFDE68A, 0xFBCFE8, 0x93C5FD, 0xFCA5A5, 0xA7F3D0, 0xC4B5FD };
            var ink = Rgb.Hex(0x9F3A5C);
            float StringY(float x) { var u = x / (w - 1) * 2 - 1; return h - 34 - 70 * (1 - u * u); }
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    float a = 0; var c = ink;
                    var sy = StringY(x);
                    var dist = Math.Abs(y - sy);
                    if (dist < 2.4f) { a = 1 - Smooth(1.2f, 2.4f, dist); c = ink; }
                    // Pennants every 100 texels hang below the string, each a downward triangle.
                    const float pitch = 100, half = 38, length = 92;
                    var idx = (int)Math.Floor(x / pitch);
                    var centre = idx * pitch + pitch / 2;
                    var below = sy - y - 1;
                    if (below > 0 && below < length)
                    {
                        var halfWidth = half * (1 - below / length);
                        var dx = Math.Abs(x - centre);
                        if (dx < halfWidth + 1f)
                        {
                            var cover = 1 - Smooth(halfWidth - 1f, halfWidth + 1f, dx);
                            var colour = Rgb.Hex(colours[idx % colours.Length]);
                            var paper = (Hash(x, y) - 0.5f) * 0.05f + (Noise(x * 0.2f, y * 0.2f) - 0.5f) * 0.06f;
                            colour = Rgb.Lerp(colour, Rgb.Hex(0xFFFFFF), 0.18f * (1 - below / length)) + paper;
                            if (dx > halfWidth - 3f) colour = colour * 0.93f;
                            c = a > 0 ? Rgb.Lerp(c, colour, cover) : colour;
                            a = Math.Max(a, cover);
                        }
                    }
                    // A little washi tape square where each pennant meets the string.
                    if (Math.Abs(x - centre) < 14 && Math.Abs(y - sy) < 8)
                    {
                        c = Rgb.Lerp(Rgb.Hex(0xFFF3E6), Rgb.Hex(0xFFD9E2), 0.4f); a = 0.92f;
                    }
                    px[y * w + x] = new Color32(
                        (byte)(Math.Clamp(c.r, 0f, 1f) * 255), (byte)(Math.Clamp(c.g, 0f, 1f) * 255), (byte)(Math.Clamp(c.b, 0f, 1f) * 255), (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 2f / 0.0146f);
        }
    }
}
