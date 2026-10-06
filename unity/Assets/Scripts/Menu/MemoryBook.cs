using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace PokeMemories.Menu
{
    /// <summary>
    /// The memory book: a storybook she can flip through. A cover, then one two-page spread per
    /// memory in the order she earned them (taped polaroids and the caption), then soft "keep
    /// skating" pages for the memories still to find. Swipe, tap a page edge, use the arrows or
    /// the arrow keys to turn pages; tap a photo to view it closer. On a narrow portrait screen
    /// each memory is one page instead of a spread.
    /// </summary>
    public class MemoryBook : MonoBehaviour
    {
        const int MaxTextureSize = 1024;
        const float BallSeconds = 0.9f, TurnSeconds = 0.55f;

        public Action OnBack;
        /// <summary>Multiplier on page-turn and pokeball speed; lowered by tests to capture mid-animation frames.</summary>
        public static float AnimationSpeed = 1;

        readonly Dictionary<string, Texture2D> photos = new();
        readonly HashSet<string> loading = new(), failed = new();
        readonly Queue<MediaItem> pending = new();
        int activeDownloads;

        int index;                       // 0 cover, 1..N memories (earned order, then locked), N+1 the end
        float turnTime = -1;
        int turnTo;
        float ballTime = -1;
        Action afterBall;
        MediaItem viewing;
        Memory viewingMemory;
        VideoPlayer video;
        bool videoFailed;
        Texture2D ballTexture;

        // Layout, recomputed every frame so rotating the device just works.
        float s;
        bool spread;
        Rect book, leftPage, rightPage, singlePage;
        List<Memory> unlocked = new();

        int N => MemoryPool.All.Count;
        int PageCount => N + 2;

        public void Open(int newMemories = 0)
        {
            index = 0;
            turnTime = ballTime = -1;
            viewing = null;
            StopVideo();
            Layout();
            if (newMemories > 0)
            {
                var target = Mathf.Clamp(unlocked.Count - newMemories + 1, 1, N);
                StartBall(() => index = target);
            }
        }

        void OnDisable() => StopVideo();

        // ───────────── Layout ─────────────

        void Layout()
        {
            s = UIInput.Scale;
            var ids = SaveStore.Data.unlockedMemoryIds;
            unlocked = ids.Select(MemoryPool.Find).Where(m => m != null).ToList();

            float w = Screen.width, h = Screen.height;
            spread = w > h * 1.15f;
            if (spread)
            {
                var bh = Mathf.Min(h * 0.78f, w * 0.94f / 1.5f);
                var bw = bh * 1.5f;
                book = new Rect((w - bw) / 2, (h - bh) / 2 - 8 * s, bw, bh);
                leftPage = new Rect(book.x, book.y, bw / 2, bh);
                rightPage = new Rect(book.center.x, book.y, bw / 2, bh);
            }
            else
            {
                var pw = w * 0.92f;
                var ph = Mathf.Min(h * 0.76f, pw * 1.4f);
                book = singlePage = new Rect((w - pw) / 2, (h - ph) / 2 - 8 * s, pw, ph);
            }
        }

        float SpineX => spread ? book.center.x : book.x;
        // A closed book is just the front cover, centred.
        bool Closed => spread && index == 0 && turnTime < 0;
        Rect CoverRect => Closed ? new Rect(Screen.width / 2f - rightPage.width / 2, rightPage.y, rightPage.width, rightPage.height) : book;
        Rect BackButton => new(20 * s, 16 * s, 100 * s, 48 * s);
        Rect PrevButton => new(book.x, Screen.height - 64 * s, 96 * s, 48 * s);
        Rect NextButton => new(book.xMax - 96 * s, Screen.height - 64 * s, 96 * s, 48 * s);

        // ───────────── Page content ─────────────

        // What lives on a page. side: 0 left, 1 right, -1 the only page (portrait).
        struct Photo2 { public Rect rect; public MediaItem item; public int n; }

        List<Photo2> PhotoSlots(int idx, int side, Rect page)
        {
            var result = new List<Photo2>();
            var slot = idx - 1;
            if (slot < 0 || slot >= unlocked.Count) return result;
            var media = unlocked[slot].media;
            int from = 0, to = media.Length;
            if (side == 0) to = media.Length / 2;
            else if (side == 1) from = media.Length / 2;
            var count = to - from;
            if (count <= 0) return result;

            float mg = 26 * s, gutter = 14 * s, footer = 30 * s;
            Rect area;
            if (side == 1) area = new Rect(page.x + mg + gutter, page.y + mg, page.width - 2 * mg - gutter, page.height - 2 * mg - footer);
            else
            {
                var titleHeight = page.height * (side == 0 ? 0.26f : 0.2f);
                area = new Rect(page.x + mg, page.y + titleHeight, page.width - 2 * mg - (side == 0 ? gutter : 0), page.height - titleHeight - mg - footer);
            }

            var cols = count == 1 ? 1 : count == 2 ? 2 : count <= 4 ? 2 : 3;
            var rows = Mathf.CeilToInt(count / (float)cols);
            float cw = area.width / cols, ch = area.height / rows;
            for (var i = 0; i < count; i++)
            {
                var cell = new Rect(area.x + i % cols * cw, area.y + i / cols * ch, cw, ch);
                var item = media[from + i];
                // The frame takes the photo's own shape, so nothing is cropped.
                var aspect = !item.IsVideo && photos.TryGetValue(item.url, out var loaded) ? (float)loaded.width / loaded.height : 0.75f;
                float border = 7 * s, below = 21 * s;
                float boxW = cw - 18 * s - 2 * border, boxH = ch - 18 * s - border - below;
                float innerW = Mathf.Min(boxW, boxH * aspect), innerH = innerW / aspect;
                float w = innerW + 2 * border, h = innerH + border + below;
                result.Add(new Photo2 { rect = new Rect(cell.center.x - w / 2, cell.center.y - h / 2, w, h), item = item, n = from + i });
            }
            return result;
        }

        static readonly Color PaperTint = new(1f, 0.975f, 0.95f), BlushTint = new(1f, 0.9f, 0.92f), Gold = new(0.96f, 0.76f, 0.42f);

        void Paper(Rect r, int side, Color tint)
        {
            Look.Tex(r, Look.Paper, tint);
            // Light falls off toward the outer edges, and the sheet curves down into the gutter.
            var edge = 40 * s;
            var warm = UIKit.WithAlpha(UIKit.Hex("#7a4b3a"), 0.10f);
            if (side == 0) Look.FadeRight(new Rect(r.x, r.y, edge, r.height), warm);
            if (side == 1) Look.FadeLeft(new Rect(r.xMax - edge, r.y, edge, r.height), warm);
            var gutter = UIKit.WithAlpha(UIKit.Hex("#4a2020"), 0.55f);
            if (spread && side == 0) Look.FadeLeft(new Rect(r.xMax - 84 * s, r.y, 84 * s, r.height), gutter);
            if (spread && side == 1) Look.FadeRight(new Rect(r.x, r.y, 84 * s, r.height), gutter);
            if (!spread) Look.FadeRight(new Rect(r.x, r.y, 36 * s, r.height), UIKit.WithAlpha(UIKit.Hex("#5a2d2d"), 0.25f));
            Look.FadeDown(new Rect(r.x, r.y, r.width, 26 * s), UIKit.WithAlpha(UIKit.Hex("#7a4b3a"), 0.06f));
            Look.FadeUp(new Rect(r.x, r.yMax - 26 * s, r.width, 26 * s), UIKit.WithAlpha(UIKit.Hex("#7a4b3a"), 0.08f));
        }

        void DrawPage(int idx, int side, Rect r)
        {
            var slot = idx - 1;
            if (idx == 0)
            {
                if (side == 0) DrawEndpaper(r);
                else DrawCover(r);
            }
            else if (idx == N + 1)
            {
                if (side == 1) { Paper(r, 1, PaperTint); DrawDoodles(r, idx); }
                else DrawEnd(r, side);
            }
            else if (slot >= unlocked.Count)
            {
                if (side == 1) { Paper(r, 1, BlushTint); DrawDoodles(r, idx); }
                else DrawLocked(r, side, slot);
            }
            else DrawMemory(r, side, idx, unlocked[slot]);
        }

        /// <summary>The patterned paper glued inside the front cover.</summary>
        void DrawEndpaper(Rect r)
        {
            Look.Tex(r, Look.Paper, UIKit.Hex("#f6b8c4"));
            var step = 34 * s;
            for (var y = 0; y < Mathf.CeilToInt(r.height / step) + 1; y++)
                for (var x = 0; x < Mathf.CeilToInt(r.width / step) + 1; x++)
                {
                    var c = new Vector2(r.x + x * step + (y % 2) * step / 2, r.y + y * step);
                    if (!r.Contains(c)) continue;
                    Look.HeartAt(c, 14 * s, UIKit.WithAlpha(Color.white, 0.32f));
                }
            Look.FadeLeft(new Rect(r.xMax - 84 * s, r.y, 84 * s, r.height), UIKit.WithAlpha(UIKit.Hex("#4a2020"), 0.55f));
            Look.FadeDown(new Rect(r.x, r.y, r.width, 30 * s), UIKit.WithAlpha(UIKit.Hex("#7a4b3a"), 0.12f));
        }

        void DrawMemory(Rect r, int side, int idx, Memory memory)
        {
            Paper(r, side, PaperTint);
            if (side != 1)
            {
                var hasPhotos = side == -1 || memory.media.Length / 2 > 0;
                var titleHeight = r.height * (side == 0 ? 0.26f : 0.2f);
                var box = hasPhotos
                    ? new Rect(r.x + 26 * s, r.y + 14 * s, r.width - 52 * s, titleHeight - 16 * s)
                    : new Rect(r.x + 26 * s, r.y + r.height * 0.3f, r.width - 52 * s, r.height * 0.4f);
                UIKit.Label(new Rect(box.x, box.y, box.width, 22 * s), $"memory {idx} of {N}", 17 * s, UIKit.Rose400, TextAnchor.MiddleCenter, true, FontStyle.Normal);
                var capSize = hasPhotos ? 34 * s : 40 * s;
                var capRect = new Rect(box.x, box.y + 20 * s, box.width, box.height - 20 * s - (string.IsNullOrEmpty(memory.date) ? 0 : 32 * s));
                UIKit.Label(capRect, memory.caption, capSize, UIKit.Rose700, TextAnchor.MiddleCenter, true, FontStyle.Normal, Look.Title);
                if (!string.IsNullOrEmpty(memory.date))
                {
                    // A little rubber stamp for the date.
                    var chip = new Rect(box.center.x - 84 * s, box.yMax - 30 * s, 168 * s, 28 * s);
                    UIKit.Rotated(-2f, chip, () =>
                    {
                        Look.RoundOutline(chip, UIKit.WithAlpha(UIKit.Rose500, 0.55f), 8 * s, 1.8f * s);
                        UIKit.Label(chip, memory.date, 17 * s, UIKit.WithAlpha(UIKit.Rose500, 0.85f));
                    });
                }
                if (!hasPhotos) Look.HeartAt(new Vector2(r.center.x, r.y + r.height * 0.74f), 90 * s, UIKit.WithAlpha(UIKit.Rose300, 0.5f));
            }
            else
            {
                Look.HeartAt(new Vector2(r.center.x, r.yMax - 24 * s), 18 * s, UIKit.WithAlpha(UIKit.Rose300, 0.7f));
            }

            var slots = PhotoSlots(idx, side, r);
            for (var i = 0; i < slots.Count; i++)
            {
                var slotCopy = slots[i];
                var tilt = ((slotCopy.n * 5 + idx) % 7 - 3) * 1.1f;
                UIKit.Rotated(tilt, slotCopy.rect, () =>
                {
                    var f = slotCopy.rect;
                    Look.Shadow(f, 10 * s, 0.5f, new Vector2(3 * s, 8 * s));
                    Look.Shadow(f, 2 * s, 0.22f, new Vector2(0, 1.5f * s));
                    Look.Tex(f, Look.Paper, Color.white);
                    UIKit.Fill(new Rect(f.x, f.y, f.width, 1), UIKit.WithAlpha(Color.white, 0.8f));
                    var photo = new Rect(f.x + 7 * s, f.y + 7 * s, f.width - 14 * s, f.height - 28 * s);
                    DrawMedia(photo, slotCopy.item);
                    // The photo sits slightly inside the card: a thin inner shadow along its top and left.
                    Look.FadeDown(new Rect(photo.x, photo.y, photo.width, 5 * s), UIKit.WithAlpha(Color.black, 0.28f));
                    Look.FadeRight(new Rect(photo.x, photo.y, 4 * s, photo.height), UIKit.WithAlpha(Color.black, 0.18f));
                    UIKit.DrawTape(f, slotCopy.n + idx, s);
                });
            }
        }

        void DrawLocked(Rect r, int side, int slot)
        {
            Paper(r, side, BlushTint);
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.14f, r.width, 34 * s), $"memory {slot + 1} of {N}", 17 * s, UIKit.Rose400, TextAnchor.MiddleCenter, true, FontStyle.Normal);
            // An empty, dashed photo frame waiting to be filled.
            var frame = new Rect(r.center.x - 70 * s, r.y + r.height * 0.24f, 140 * s, 160 * s);
            UIKit.Rotated(-3f, frame, () =>
            {
                Look.RoundOutline(frame, UIKit.WithAlpha(UIKit.Rose300, 0.7f), 6 * s, 2.5f * s);
                Look.Round(frame, UIKit.WithAlpha(Color.white, 0.35f), 6 * s);
                UIKit.Label(frame, "?", 100 * s, UIKit.WithAlpha(UIKit.Rose300, 0.85f), TextAnchor.MiddleCenter, true, FontStyle.Normal, Look.Title);
                UIKit.DrawTape(frame, slot, s);
            });
            UIKit.Label(new Rect(r.x + 24 * s, r.y + r.height * 0.6f, r.width - 48 * s, 70 * s), "Keep skating to unlock", 32 * s, UIKit.Rose400, TextAnchor.MiddleCenter, true, FontStyle.Normal, Look.Title);
            UIKit.Label(new Rect(r.x + 24 * s, r.y + r.height * 0.72f, r.width - 48 * s, 60 * s), "This page is waiting for a memory ♡", 20 * s, UIKit.Rose300, TextAnchor.MiddleCenter, true, FontStyle.Normal);
            DrawDoodles(r, slot);
        }

        void DrawEnd(Rect r, int side)
        {
            Paper(r, side, PaperTint);
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.22f, r.width, 80 * s), "The End", 58 * s, UIKit.Rose600, TextAnchor.MiddleCenter, false, FontStyle.Normal, Look.Title);
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.22f + 74 * s, r.width, 40 * s), "for now ♡", 28 * s, UIKit.Rose400, TextAnchor.MiddleCenter, false, FontStyle.Normal);
            UIKit.Label(new Rect(r.x + 20 * s, r.y + r.height * 0.55f, r.width - 40 * s, 70 * s),
                unlocked.Count >= N ? "Every memory is in the book." : $"{unlocked.Count} of {N} memories found.\nKeep skating to fill the rest.",
                22 * s, UIKit.Rose500, TextAnchor.MiddleCenter, true, FontStyle.Normal);
            DrawDoodles(r, 3);
        }

        /// <summary>Dashed stitching along `b`, like thread sewn through the cloth.</summary>
        static void Stitch(Rect b, Color colour, float dash, float gap, float thickness)
        {
            for (var x = b.x; x < b.xMax; x += dash + gap)
            {
                var w = Mathf.Min(dash, b.xMax - x);
                UIKit.Fill(new Rect(x, b.y, w, thickness), colour);
                UIKit.Fill(new Rect(x, b.yMax - thickness, w, thickness), colour);
            }
            for (var y = b.y; y < b.yMax; y += dash + gap)
            {
                var h = Mathf.Min(dash, b.yMax - y);
                UIKit.Fill(new Rect(b.x, y, thickness, h), colour);
                UIKit.Fill(new Rect(b.xMax - thickness, y, thickness, h), colour);
            }
        }

        void DrawCover(Rect r)
        {
            Look.Round(r, UIKit.Hex("#7a0f2c"), 9 * s);
            GUI.BeginClip(r);
            var inner = new Rect(0, 0, r.width, r.height);
            Look.Tiled(inner, Look.Cloth, 128 * s, UIKit.Hex("#c0143f"));
            // Soft light from the upper left, shade toward the lower right.
            Look.Tex(new Rect(-r.width * 0.2f, -r.height * 0.3f, r.width * 1.3f, r.height * 1.1f), Look.Glow, UIKit.WithAlpha(UIKit.Hex("#ff9eb5"), 0.22f));
            Look.FadeUp(new Rect(0, r.height * 0.55f, r.width, r.height * 0.45f), UIKit.WithAlpha(UIKit.Hex("#3a0516"), 0.35f));
            // The spine band down the left: darker, with raised ridges.
            var band = new Rect(0, 0, r.width * 0.085f, r.height);
            Look.Tiled(band, Look.Cloth, 128 * s, UIKit.Hex("#8c0d2f"));
            Look.FadeRight(new Rect(band.xMax, 0, 14 * s, r.height), UIKit.WithAlpha(Color.black, 0.35f));
            UIKit.Fill(new Rect(band.xMax, 0, 1.5f * s, r.height), UIKit.WithAlpha(UIKit.Hex("#ff9eb5"), 0.3f));
            foreach (var f in new[] { 0.14f, 0.2f, 0.8f, 0.86f })
                UIKit.Fill(new Rect(0, r.height * f, band.width, 2.5f * s), UIKit.WithAlpha(Gold, 0.8f));
            GUI.EndClip();

            // Gold foil frame and cream stitching.
            var gold = UIKit.WithAlpha(Gold, 0.95f);
            var content = new Rect(r.x + r.width * 0.085f, r.y, r.width * 0.915f, r.height);
            foreach (var inset in new[] { 18 * s, 26 * s })
                Look.RoundOutline(new Rect(content.x + inset, r.y + inset, content.width - 2 * inset, r.height - 2 * inset), gold, 4 * s, inset < 20 * s ? 2.5f * s : 1.2f * s);
            Stitch(new Rect(content.x + 36 * s, r.y + 36 * s, content.width - 72 * s, r.height - 72 * s), UIKit.WithAlpha(new Color(1f, 0.92f, 0.88f), 0.5f), 7 * s, 5 * s, 1.5f * s);
            foreach (var corner in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) })
                Look.HeartAt(new Vector2(Mathf.Lerp(content.x + 22 * s, content.xMax - 22 * s, corner.x), Mathf.Lerp(r.y + 22 * s, r.yMax - 22 * s, corner.y)), 11 * s, gold);

            ballTexture ??= MakeBallTexture();
            var size = Mathf.Min(r.width, r.height) * 0.32f;
            var cx = content.center.x;
            // The cover pokeball hands over to the opening animation while it plays.
            if (ballTime < 0)
            {
                var ball = new Rect(cx - size / 2, r.y + r.height * 0.13f, size, size);
                Look.Tex(new Rect(ball.x - 12 * s, ball.y + 6 * s, ball.width + 24 * s, ball.height + 18 * s), Look.Glow, UIKit.WithAlpha(Color.black, 0.35f));
                GUI.DrawTexture(ball, ballTexture);
            }
            var cream = new Color(1f, 0.96f, 0.93f);
            var titleY = r.y + r.height * 0.13f + size + 6 * s;
            // Foil-stamped title: a dark pressed edge above, a bright catch-light below.
            var tr = new Rect(content.x, titleY, content.width, 70 * s);
            UIKit.Label(new Rect(tr.x - 1.2f * s, tr.y - 1.5f * s, tr.width, tr.height), "Our Memory Book", 42 * s, UIKit.WithAlpha(UIKit.Hex("#5a0a22"), 0.7f), TextAnchor.MiddleCenter, false, FontStyle.Normal, Look.Title);
            UIKit.Label(new Rect(tr.x + 1.2f * s, tr.y + 1.5f * s, tr.width, tr.height), "Our Memory Book", 42 * s, UIKit.WithAlpha(UIKit.Hex("#fff0c8"), 0.55f), TextAnchor.MiddleCenter, false, FontStyle.Normal, Look.Title);
            UIKit.Label(tr, "Our Memory Book", 42 * s, Gold, TextAnchor.MiddleCenter, false, FontStyle.Normal, Look.Title);
            UIKit.Label(new Rect(content.x, titleY + 64 * s, content.width, 28 * s), "P O K E - M E M O R I E S", 16 * s, UIKit.WithAlpha(cream, 0.8f));
            Look.HeartAt(new Vector2(cx, titleY + 122 * s), 34 * s, Gold);
            var pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 2.4f);
            UIKit.Label(new Rect(content.x, r.yMax - 84 * s, content.width, 36 * s), "Tap to open ♡", 22 * s, UIKit.WithAlpha(cream, pulse));
        }

        void DrawDoodles(Rect r, int seed)
        {
            for (var i = 0; i < 3; i++)
            {
                var x = r.x + r.width * (0.2f + 0.3f * ((seed + i * 2) % 3));
                var y = r.y + r.height * (0.1f + 0.34f * ((seed + i) % 3)) + (i == 1 ? r.height * 0.4f : 0);
                if (i % 2 == 0) Look.HeartAt(new Vector2(x, y), (16 + i * 4) * s, UIKit.WithAlpha(UIKit.Rose300, 0.4f));
                else Look.Tex(new Rect(x - 14 * s, y - 14 * s, 28 * s, 28 * s), Look.Sparkle, UIKit.WithAlpha(UIKit.Hex("#f5b942"), 0.6f));
            }
        }

        /// <summary>A photo cropped to fill `rect`, or a play-badged tile for a video.</summary>
        void DrawMedia(Rect rect, MediaItem item)
        {
            UIKit.Fill(rect, UIKit.Rose50);
            if (item.IsVideo)
            {
                UIKit.Fill(rect, UIKit.WithAlpha(UIKit.Rose300, 0.35f));
                var size = Mathf.Min(rect.width, rect.height) * 0.34f;
                var badge = new Rect(rect.center.x - size / 2, rect.center.y - size / 2, size, size);
                Look.Tex(new Rect(badge.x - size * 0.2f, badge.y - size * 0.1f, size * 1.4f, size * 1.4f), Look.Glow, UIKit.WithAlpha(Color.black, 0.35f));
                Look.Round(badge, UIKit.WithAlpha(Color.white, 0.92f), size / 2);
                UIKit.Label(new Rect(badge.x + size * 0.05f, badge.y, badge.width, badge.height), "▶", size * 0.5f, UIKit.Rose500);
                return;
            }
            var texture = Photo(item);
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            else UIKit.Label(rect, failed.Contains(item.url) ? "✕" : "…", 30 * s, UIKit.Rose300);
        }

        // ───────────── Input ─────────────

        public void Tick()
        {
            Layout();
            var keyboard = Keyboard.current;
            var escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

            if (ballTime >= 0)
            {
                ballTime += Time.unscaledDeltaTime * AnimationSpeed;
                if (ballTime >= BallSeconds)
                {
                    ballTime = -1;
                    var action = afterBall;
                    afterBall = null;
                    action?.Invoke();
                }
                return;
            }
            if (turnTime >= 0)
            {
                turnTime += Time.unscaledDeltaTime * AnimationSpeed;
                if (turnTime >= TurnSeconds) { index = turnTo; turnTime = -1; }
                return;
            }
            if (viewing != null)
            {
                if (escape || UIInput.Tap) CloseViewer();
                return;
            }

            // Warm up the photos on the next page so a turn never reveals blanks.
            foreach (var neighbour in new[] { index, index + 1 })
                foreach (var slot in AllSlots(neighbour)) if (!slot.item.IsVideo) Photo(slot.item);

            if (escape || UIInput.TapIn(BackButton)) { OnBack?.Invoke(); return; }
            if (keyboard != null)
            {
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) { Next(); return; }
                if (keyboard.leftArrowKey.wasPressedThisFrame) { Prev(); return; }
            }

            if (UIInput.DragReleased)
            {
                var drag = UIInput.DragTotal;
                if (Mathf.Abs(drag.x) > 60 * s && Mathf.Abs(drag.x) > Mathf.Abs(drag.y)) { if (drag.x < 0) Next(); else Prev(); }
                return;
            }
            if (!UIInput.Tap) return;
            var tap = UIInput.TapPosition;

            if (UIInput.TapIn(NextButton)) { Next(); return; }
            if (UIInput.TapIn(PrevButton)) { Prev(); return; }
            foreach (var slot in AllSlots(index))
            {
                if (!slot.rect.Contains(tap)) continue;
                viewing = slot.item;
                viewingMemory = unlocked[index - 1];
                if (viewing.IsVideo) StartVideo(viewing.url);
                return;
            }
            if (index == 0 && CoverRect.Contains(tap)) { Next(); return; }
            // Tapping near a page's outer edge turns it, like pushing a real page over.
            if (!book.Contains(tap)) return;
            var edge = book.width * (spread ? 0.08f : 0.16f);
            if (tap.x > book.xMax - edge) Next();
            else if (tap.x < book.x + edge) Prev();
        }

        IEnumerable<Photo2> AllSlots(int idx)
        {
            if (spread) return PhotoSlots(idx, 0, leftPage).Concat(PhotoSlots(idx, 1, rightPage));
            return PhotoSlots(idx, -1, singlePage);
        }

        void Next()
        {
            if (index >= PageCount - 1) return;
            if (index == 0) StartBall(() => StartTurn(1));
            else StartTurn(index + 1);
        }

        void Prev()
        {
            if (index > 0) StartTurn(index - 1);
        }

        void StartTurn(int target)
        {
            turnTo = target;
            turnTime = 0;
        }

        void StartBall(Action then)
        {
            afterBall = then;
            ballTime = 0;
        }

        void CloseViewer()
        {
            StopVideo();
            viewing = null;
        }

        // ───────────── Photos ─────────────

        /// <summary>Returns the downloaded photo, or null while it loads (or if it failed).</summary>
        Texture2D Photo(MediaItem item)
        {
            if (photos.TryGetValue(item.url, out var texture)) return texture;
            if (!loading.Contains(item.url) && !failed.Contains(item.url))
            {
                loading.Add(item.url);
                pending.Enqueue(item);
                PumpDownloads();
            }
            return null;
        }

        void PumpDownloads()
        {
            // Two at a time keeps peak memory low: a phone JPEG decodes to tens of megabytes.
            while (activeDownloads < 2 && pending.Count > 0)
            {
                activeDownloads++;
                StartCoroutine(Fetch(pending.Dequeue()));
            }
        }

        IEnumerator Fetch(MediaItem item)
        {
            using (var request = UnityWebRequest.Get(item.url))
            {
                yield return request.SendWebRequest();
                Texture2D texture = null;
                if (request.result == UnityWebRequest.Result.Success)
                    texture = Decode(request.downloadHandler.data, MaxTextureSize, item.rotate / 90);
                if (texture != null) photos[item.url] = texture;
                else failed.Add(item.url);
            }
            loading.Remove(item.url);
            activeDownloads--;
            PumpDownloads();
        }

        /// <summary>Decodes a JPEG/PNG, shrinks it to `max` pixels and rotates it upright.</summary>
        static Texture2D Decode(byte[] bytes, int max, int extraQuarterTurns)
        {
            var exif = ExifOrientation(bytes);
            var turns = ((exif == 6 ? 1 : exif == 3 ? 2 : exif == 8 ? 3 : 0) + extraQuarterTurns % 4 + 4) % 4;
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(bytes, false))
            {
                Destroy(source);
                return null;
            }
            var scale = Mathf.Min(1f, (float)max / Mathf.Max(source.width, source.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(source.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

            var target = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(source, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var flat = new Texture2D(w, h, TextureFormat.RGBA32, false);
            flat.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Destroy(source);

            var result = flat;
            if (turns != 0)
            {
                var from = flat.GetPixels32();
                var to = new Color32[from.Length];
                var swapped = turns != 2;
                result = new Texture2D(swapped ? h : w, swapped ? w : h, TextureFormat.RGBA32, false);
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        var index = turns switch
                        {
                            1 => (w - 1 - x) * h + y,           // 90° clockwise
                            3 => x * h + (h - 1 - y),           // 90° counter-clockwise
                            _ => (h - 1 - y) * w + (w - 1 - x), // 180°
                        };
                        to[index] = from[y * w + x];
                    }
                result.SetPixels32(to);
                Destroy(flat);
            }
            result.wrapMode = TextureWrapMode.Clamp;
            result.Apply(false, true);
            return result;
        }

        // Phone photos are stored sideways with an EXIF tag; browsers honour it, Unity does not.
        static int ExifOrientation(byte[] d)
        {
            try
            {
                if (d.Length < 12 || d[0] != 0xFF || d[1] != 0xD8) return 1;
                var i = 2;
                while (i + 4 < d.Length && d[i] == 0xFF)
                {
                    var marker = d[i + 1];
                    var length = (d[i + 2] << 8) | d[i + 3];
                    if (marker == 0xE1 && d[i + 4] == 'E' && d[i + 5] == 'x' && d[i + 6] == 'i' && d[i + 7] == 'f')
                    {
                        var tiff = i + 10;
                        var little = d[tiff] == 'I';
                        int U16(int at) => little ? d[at] | d[at + 1] << 8 : d[at] << 8 | d[at + 1];
                        int U32(int at) => little
                            ? d[at] | d[at + 1] << 8 | d[at + 2] << 16 | d[at + 3] << 24
                            : d[at] << 24 | d[at + 1] << 16 | d[at + 2] << 8 | d[at + 3];
                        var ifd = tiff + U32(tiff + 4);
                        var entries = U16(ifd);
                        for (var e = 0; e < entries; e++)
                        {
                            var at = ifd + 2 + 12 * e;
                            if (U16(at) == 0x0112) return U16(at + 8);
                        }
                        return 1;
                    }
                    if (marker == 0xDA) return 1;
                    i += 2 + length;
                }
            }
            catch (IndexOutOfRangeException) { }
            return 1;
        }

        // ───────────── Video ─────────────

        void StartVideo(string url)
        {
            videoFailed = false;
            video = new GameObject("MemoryVideo").AddComponent<VideoPlayer>();
            video.source = VideoSource.Url;
            video.url = url;
            video.renderMode = VideoRenderMode.APIOnly;
            video.audioOutputMode = VideoAudioOutputMode.Direct;
            video.errorReceived += (_, _) => videoFailed = true;
            video.Play();
        }

        void StopVideo()
        {
            if (video != null) Destroy(video.gameObject);
            video = null;
        }

        // ───────────── Drawing ─────────────

        void DrawDesk()
        {
            float W = Screen.width, H = Screen.height;
            UIKit.Fill(new Rect(0, 0, W, H), UIKit.Hex("#2a1526"));
            Look.Tiled(new Rect(0, 0, W, H), Look.Felt, 256 * s, UIKit.WithAlpha(UIKit.Hex("#6b3a5c"), 0.75f));
            // A warm lamp from the upper left, and the room falling into darkness at the edges.
            Look.Tex(new Rect(-W * 0.25f, -H * 0.75f, W * 1.1f, H * 1.9f), Look.Glow, UIKit.WithAlpha(UIKit.Hex("#ffc58a"), 0.30f));
            Look.Tex(new Rect(-W * 0.15f, -H * 0.15f, W * 1.3f, H * 1.3f), Look.Vignette, UIKit.WithAlpha(UIKit.Hex("#ffd9b0"), 0.05f));
            Look.FadeDown(new Rect(0, 0, W, H * 0.18f), UIKit.WithAlpha(Color.black, 0.35f));
            Look.FadeUp(new Rect(0, H * 0.78f, W, H * 0.22f), UIKit.WithAlpha(Color.black, 0.5f));
            Look.FadeRight(new Rect(0, 0, W * 0.12f, H), UIKit.WithAlpha(Color.black, 0.3f));
            Look.FadeLeft(new Rect(W * 0.88f, 0, W * 0.12f, H), UIKit.WithAlpha(Color.black, 0.4f));
            // Dust drifting through the lamplight.
            for (var i = 0; i < 22; i++)
            {
                var seed = i * 12.9898f;
                var x = Mathf.Repeat(Mathf.Sin(seed) * 43758.5f, 1f) * W + Mathf.Sin(Time.unscaledTime * 0.3f + seed) * 18 * s;
                var y = Mathf.Repeat(Mathf.Sin(seed * 1.7f) * 24634.6f - Time.unscaledTime * (4 + i % 5), H);
                var size = (3 + i % 4) * s;
                Look.Tex(new Rect(x - size, y - size, size * 2, size * 2), Look.Glow, UIKit.WithAlpha(UIKit.Hex("#ffe2b8"), 0.35f * (0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.unscaledTime + seed)))));
            }
        }

        /// <summary>The cloth-covered boards and the block of page edges around `shown`.</summary>
        void DrawBoards(Rect shown)
        {
            var board = new Rect(shown.x - 13 * s, shown.y - 13 * s, shown.width + 26 * s, shown.height + 26 * s);
            Look.Shadow(board, 34 * s, 0.8f, new Vector2(8 * s, 22 * s));
            Look.Shadow(board, 6 * s, 0.4f, new Vector2(2 * s, 5 * s));
            Look.Round(board, UIKit.Hex("#6e0c29"), 9 * s);
            GUI.BeginClip(board);
            Look.Tiled(new Rect(0, 0, board.width, board.height), Look.Cloth, 128 * s, UIKit.Hex("#9d1037"));
            Look.Tex(new Rect(-board.width * 0.2f, -board.height * 0.3f, board.width * 1.3f, board.height * 1.1f), Look.Glow, UIKit.WithAlpha(UIKit.Hex("#ff9eb5"), 0.18f));
            GUI.EndClip();
            Look.RoundOutline(board, UIKit.WithAlpha(UIKit.Hex("#ff9eb5"), 0.35f), 9 * s, 1.5f * s);
            // Stacked page edges: a few cream layers, each a hair wider and lower than the last.
            for (var k = 4; k >= 1; k--)
            {
                var layer = new Rect(shown.x - k * 1.8f * s, shown.y + k * 0.6f * s, shown.width + k * 3.6f * s, shown.height + k * 1.5f * s);
                UIKit.Fill(layer, k % 2 == 0 ? UIKit.Hex("#e7dac6") : UIKit.Hex("#f4eadb"));
                UIKit.Fill(new Rect(layer.x, layer.yMax - 1, layer.width, 1), UIKit.WithAlpha(UIKit.Hex("#7a5a45"), 0.35f));
            }
        }

        public void Draw()
        {
            if (Event.current.type != EventType.Repaint) return;
            Look.Ensure();
            Layout();
            DrawDesk();

            var shown = CoverRect;
            if (Closed)
            {
                var board = new Rect(shown.x - 4 * s, shown.y - 4 * s, shown.width + 8 * s, shown.height + 8 * s);
                Look.Shadow(board, 34 * s, 0.8f, new Vector2(8 * s, 22 * s));
                Look.Shadow(board, 6 * s, 0.4f, new Vector2(2 * s, 5 * s));
                // Page block peeking from under the cover.
                for (var k = 3; k >= 1; k--)
                    UIKit.Fill(new Rect(shown.x + 2 * s, shown.y + 4 * s + k * 1.6f * s, shown.width - 2 * s + k * 1.2f * s, shown.height), k % 2 == 0 ? UIKit.Hex("#e7dac6") : UIKit.Hex("#f4eadb"));
                DrawCover(shown);
            }
            else
            {
                DrawBoards(shown);
                if (turnTime < 0 || ballTime >= 0) DrawSpread(index);
                else DrawTurning();
                if (spread && (index > 0 && index < PageCount - 1 || turnTime >= 0))
                {
                    // The gutter: a deep crease with a thread of light on each side.
                    UIKit.Fill(new Rect(book.center.x - 1.5f * s, book.y, 3 * s, book.height), UIKit.WithAlpha(UIKit.Hex("#2a0f14"), 0.45f));
                }
            }

            // Controls.
            UIKit.Button(BackButton, "◀ Menu", Color.white, UIKit.Rose500, 20 * s);
            if (index > 0) UIKit.Button(PrevButton, "◀", Color.white, UIKit.Rose500, 24 * s);
            if (index < PageCount - 1) UIKit.Button(NextButton, "▶", UIKit.Rose500, Color.white, 24 * s);
            var label = index == 0 ? "Tap the cover or swipe to open" : index <= N ? $"Memory {index} of {N}" : "";
            UIKit.Label(new Rect(book.x + 110 * s, Screen.height - 64 * s, book.width - 220 * s, 48 * s), label, 21 * s, UIKit.Hex("#f5d7dc"), TextAnchor.MiddleCenter, true, FontStyle.Normal);

            if (ballTime >= 0) DrawPokeball(ballTime / BallSeconds);
            if (viewing != null) DrawViewer();
        }

        void DrawSpread(int idx)
        {
            if (spread)
            {
                DrawPage(idx, 0, leftPage);
                DrawPage(idx, 1, rightPage);
            }
            else DrawPage(idx, -1, singlePage);
        }

        void DrawTurning()
        {
            var t = Mathf.Clamp01(turnTime / TurnSeconds);
            t = t * t * (3 - 2 * t);
            var forward = turnTo > index;
            var a = index;
            var b = turnTo;

            if (spread)
            {
                // The resting pages under the sheet, then the sheet itself as a curled strip.
                DrawPage(forward ? a : b, 0, leftPage);
                DrawPage(forward ? b : a, 1, rightPage);
                DrawCurl(Mathf.Min(a, b), Mathf.Max(a, b), forward, t);
            }
            else
            {
                DrawPage(forward ? b : a, -1, singlePage);
                var width = Mathf.Max(0.001f, forward ? 1 - t : t);
                Flip(width, new Vector2(book.x, book.center.y), forward ? a : b, -1, singlePage, t);
            }
        }

        // ───────────── Page curl ─────────────
        // The turning sheet is drawn as a bent ribbon: both faces are rendered into full-screen
        // textures, then sliced into thin vertical strips whose position, height and brightness
        // follow a simulated curve (orthographic position, mild perspective, Lambert-style shading).

        RenderTexture frontRT, backRT;
        const int CurlStrips = 56;

        RenderTexture PageTexture(ref RenderTexture rt)
        {
            if (rt == null || rt.width != Screen.width || rt.height != Screen.height)
            {
                if (rt != null) rt.Release();
                rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear, useMipMap = false };
                rt.Create();
            }
            return rt;
        }

        void RenderPage(RenderTexture rt, int idx, int side, Rect page)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            DrawPage(idx, side, page);
            RenderTexture.active = previous;
        }

        void DrawCurl(int lo, int hi, bool forward, float t)
        {
            var front = PageTexture(ref frontRT);
            var back = PageTexture(ref backRT);
            RenderPage(front, lo, 1, rightPage);
            RenderPage(back, hi, 0, leftPage);

            float W = Screen.width, H = Screen.height;
            var pw = rightPage.width;
            var spineX = SpineX;
            var cy = book.center.y;
            var dir = forward ? t : 1 - t;                 // 0 = sheet flat on the right, 1 = flat on the left
            var theta = dir * Mathf.PI;
            // The leading edge runs ahead of the spine, so the sheet bows; the bow eases off as it lands.
            var bow = 1.5f * Mathf.Sin(theta) * (forward ? 1f - 1.1f * t : -(1f - 1.1f * (1 - t)));
            var light = new Vector2(-0.45f, 0.9f).normalized;

            var xs = new float[CurlStrips + 1];
            var zs = new float[CurlStrips + 1];
            var al = new float[CurlStrips];
            float x = 0, z = 0;
            var du = pw / CurlStrips;
            for (var i = 0; i < CurlStrips; i++)
            {
                var u = (i + 0.5f) / CurlStrips;
                var alpha = Mathf.Clamp(theta + bow * u, 0f, Mathf.PI);
                al[i] = alpha;
                xs[i] = x; zs[i] = z;
                x += Mathf.Cos(alpha) * du;
                z += Mathf.Sin(alpha) * du;
            }
            xs[CurlStrips] = x; zs[CurlStrips] = z;

            var persp = 0.16f / pw;

            // Shadow the lifted sheet throws on whatever lies beneath it: one soft band covering the
            // shifted footprint of the raised part, darkest where the sheet is highest.
            float sMin = float.MaxValue, sMax = float.MinValue, peak = 0;
            for (var i = 0; i <= CurlStrips; i++)
            {
                if (zs[i] < 0.02f * pw) continue;
                var sx = spineX + xs[i] + zs[i] * 0.45f;
                sMin = Mathf.Min(sMin, sx); sMax = Mathf.Max(sMax, sx);
                peak = Mathf.Max(peak, zs[i]);
            }
            if (sMax > sMin)
            {
                var k = Mathf.SmoothStep(0, 1, peak / (0.35f * pw));
                var h = rightPage.height * 0.985f;
                var band = new Rect(sMin, cy - h / 2 + 8 * s, sMax - sMin, h);
                UIKit.Fill(band, UIKit.WithAlpha(Color.black, 0.2f * k));
                var soft = 46 * s;
                Look.FadeRight(new Rect(sMax, band.y, soft, band.height), UIKit.WithAlpha(Color.black, 0.2f * k));
                Look.FadeLeft(new Rect(sMin - soft, band.y, soft, band.height), UIKit.WithAlpha(Color.black, 0.2f * k));
            }
            float page_h(float zz) => rightPage.height * (1 + persp * zz);

            var old = GUI.color;
            for (var i = 0; i < CurlStrips; i++)
            {
                var alpha = al[i];
                var isFront = alpha < Mathf.PI / 2;
                var tex = isFront ? front : back;
                var u0 = i * du;
                var u1 = (i + 1) * du;
                float s0, s1;
                if (isFront) { s0 = rightPage.x + u0; s1 = rightPage.x + u1; }
                else { s0 = leftPage.xMax - u0; s1 = leftPage.xMax - u1; }
                float sx0 = spineX + xs[i], sx1 = spineX + xs[i + 1];
                if (sx1 < sx0) { (sx0, sx1) = (sx1, sx0); (s0, s1) = (s1, s0); }
                var zmid = (zs[i] + zs[i + 1]) * 0.5f;
                var h = page_h(zmid);
                // Normal of the visible face and a simple diffuse + sheen term.
                var n = isFront ? new Vector2(-Mathf.Sin(alpha), Mathf.Cos(alpha)) : new Vector2(Mathf.Sin(alpha), -Mathf.Cos(alpha));
                var diffuse = Mathf.Clamp01(Vector2.Dot(n, light));
                var shade = Mathf.Lerp(0.86f, 1f, diffuse);
                var rect = new Rect(sx0 - 0.5f, cy - h / 2, sx1 - sx0 + 1f, h);
                GUI.color = new Color(shade, shade, shade, 1);
                GUI.DrawTextureWithTexCoords(rect, tex, UvFor(s0, s1, W, H, h));
            }
            GUI.color = old;

            // A hairline of light along the top of the fold, where the paper catches the lamp.
            var edgeX = spineX + xs[CurlStrips];
            var eh = page_h(zs[CurlStrips]);
            UIKit.Fill(new Rect(edgeX - 1, cy - eh / 2, 2, eh), UIKit.WithAlpha(Color.white, 0.25f * Mathf.Sin(theta)));
        }

        Rect UvFor(float s0, float s1, float W, float H, float drawnHeight)
        {
            // The page occupies the same vertical band in the texture as on screen; stretching the
            // strip taller or shorter then gives the perspective for free.
            var top = rightPage.y;
            var bottom = rightPage.yMax;
            return new Rect(s0 / W, 1 - bottom / H, (s1 - s0) / W, (bottom - top) / H);
        }

        void Flip(float widthScale, Vector2 pivot, int idx, int side, Rect page, float t)
        {
            var lift = Mathf.Sin(t * Mathf.PI);
            var edgeX = side == 0 ? pivot.x - page.width * widthScale : pivot.x + page.width * widthScale;
            if (side == -1) edgeX = pivot.x + page.width * widthScale;

            // Shadow the moving page throws onto the sheet underneath, strongest mid-turn.
            var cast = (side == 0 ? 1 : -1) * page.width * 0.32f * lift;
            var shadowRect = side == 0
                ? new Rect(edgeX - Mathf.Abs(cast), page.y, Mathf.Abs(cast), page.height)
                : new Rect(edgeX, page.y, Mathf.Abs(cast), page.height);
            if (side == 0) Look.FadeLeft(shadowRect, UIKit.WithAlpha(Color.black, 0.38f * lift));
            else Look.FadeRight(shadowRect, UIKit.WithAlpha(Color.black, 0.38f * lift));

            var matrix = GUI.matrix;
            // Lifting toward the viewer: the page grows a touch and tips slightly as it passes overhead.
            GUIUtility.ScaleAroundPivot(new Vector2(widthScale, 1 + 0.035f * lift), pivot);
            DrawPage(idx, side, page);
            // Curvature: a bright sheen where the page faces the lamp, shading toward the fold.
            var shade = widthScale < 0.5f ? 0.45f : 0.2f;
            var inner = side == 0 ? page.xMax - page.width * 0.4f : page.x;
            if (side == 0) Look.FadeLeft(new Rect(inner, page.y, page.width * 0.4f, page.height), UIKit.WithAlpha(Color.black, shade * lift));
            else Look.FadeRight(new Rect(inner, page.y, page.width * 0.4f, page.height), UIKit.WithAlpha(Color.black, shade * lift));
            UIKit.Fill(page, UIKit.WithAlpha(UIKit.Hex("#ffe9c8"), 0.10f * lift));
            GUI.matrix = matrix;
        }

        void DrawViewer()
        {
            UIKit.Fill(new Rect(0, 0, Screen.width, Screen.height), UIKit.WithAlpha(Color.black, 0.82f));
            var card = new Rect(Screen.width * 0.04f, Screen.height * 0.05f, Screen.width * 0.92f, Screen.height * 0.9f);
            Look.Shadow(card, 30 * s, 0.5f, new Vector2(0, 14 * s));
            Look.Tex(card, Look.Paper, Color.white);
            var area = new Rect(card.x + 16 * s, card.y + 16 * s, card.width - 32 * s, card.height - 100 * s);
            UIKit.Fill(area, UIKit.Hex("#2a1526"));

            if (viewing.IsVideo)
            {
                if (videoFailed) UIKit.Label(area, "This video can't play here.\nTap to go back.", 26 * s, UIKit.Rose300);
                else if (video != null && video.isPlaying && video.texture != null) GUI.DrawTexture(area, video.texture, ScaleMode.ScaleToFit);
                else UIKit.Label(area, "Loading video…", 26 * s, UIKit.Rose300);
            }
            else
            {
                var texture = Photo(viewing);
                if (texture != null) GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
                else UIKit.Label(area, failed.Contains(viewing.url) ? "Couldn't load this photo" : "Loading…", 26 * s, UIKit.Rose300);
            }
            UIKit.Label(new Rect(card.x, card.yMax - 76 * s, card.width, 64 * s), viewingMemory != null ? viewingMemory.caption : "", 36 * s, UIKit.Rose600, TextAnchor.MiddleCenter, true, FontStyle.Normal, Look.Title);
            var close = new Rect(card.xMax - 62 * s, card.y + 12 * s, 48 * s, 48 * s);
            Look.Round(close, UIKit.WithAlpha(Color.black, 0.45f), 24 * s);
            UIKit.Label(close, "✕", 26 * s, Color.white);
        }

        // ───────────── Pokeball opening animation ─────────────

        void DrawPokeball(float t)
        {
            UIKit.Fill(new Rect(0, 0, Screen.width, Screen.height), UIKit.WithAlpha(Color.black, 0.45f * Mathf.Min(1, t * 3)));
            ballTexture ??= MakeBallTexture();
            var size = 150 * s;
            var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
            var fade = Mathf.Clamp01(1 - (t - 0.55f) / 0.4f);
            var open = Mathf.Clamp01((t - 0.3f) / 0.5f);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, fade);

            GUI.DrawTextureWithTexCoords(new Rect(centre.x - size / 2, centre.y + open * 14 * s, size, size / 2), ballTexture, new Rect(0, 0, 1, 0.5f));
            var top = new Rect(centre.x - size / 2, centre.y - size / 2 - open * open * 90 * s, size, size / 2);
            UIKit.Rotated(-open * 45f, top, () => GUI.DrawTextureWithTexCoords(top, ballTexture, new Rect(0, 0.5f, 1, 0.5f)));
            GUI.color = old;
        }

        static Texture2D MakeBallTexture()
        {
            const int n = 128;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            var red = (Color32)UIKit.Rose600;
            var ink = (Color32)UIKit.Ink;
            var white = (Color32)Color.white;
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float dx = x - 63.5f, dy = y - 63.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    var colour = dy > 0 ? red : white;
                    if (d > 59 || Mathf.Abs(dy) < 4 || (d < 16 && d > 11)) colour = ink;
                    else if (d <= 11) colour = d < 6 ? red : white;
                    // Shaded like a glossy sphere lit from the upper left, with a soft specular spot.
                    var lit = 1f - 0.34f * Mathf.Clamp01((dx * 0.6f - dy * 0.8f + 60f) / 120f);
                    var hx = dx + 22f; var hy = dy - 30f;
                    var spec = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy * 1.6f) / 22f), 2f) * 0.75f;
                    var shaded = new Color(colour.r / 255f * lit, colour.g / 255f * lit, colour.b / 255f * lit);
                    shaded = Color.Lerp(shaded, Color.white, spec);
                    colour = (Color32)shaded;
                    colour.a = (byte)(Mathf.Clamp01(62.5f - d) * 255);
                    pixels[y * n + x] = colour;
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
