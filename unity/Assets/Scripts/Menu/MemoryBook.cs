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

            var cols = count == 1 ? 1 : count == 2 ? (area.width > area.height ? 2 : 1) : count <= 4 ? 2 : 3;
            var rows = Mathf.CeilToInt(count / (float)cols);
            float cw = area.width / cols, ch = area.height / rows;
            for (var i = 0; i < count; i++)
            {
                var cell = new Rect(area.x + i % cols * cw, area.y + i / cols * ch, cw, ch);
                var w = cw - 18 * s;
                var h = ch - 18 * s;
                // Keep frames photo-shaped instead of stretching to the cell.
                if (w / h > 1.1f) w = h * 1.1f;
                else if (w / h < 0.72f) h = w / 0.72f;
                result.Add(new Photo2 { rect = new Rect(cell.center.x - w / 2, cell.center.y - h / 2, w, h), item = media[from + i], n = from + i });
            }
            return result;
        }

        void Paper(Rect r, int side, Color colour)
        {
            UIKit.Fill(r, colour);
            // Shading near the spine, as the page curves into the gutter.
            if (side == -1 && spread) return;
            const int steps = 8;
            var width = 30 * s;
            for (var i = 0; i < steps; i++)
            {
                var x = side == 0 ? r.xMax - width * (i + 1) / steps : r.x + width * i / steps;
                UIKit.Fill(new Rect(x, r.y, width / steps + 1, r.height), UIKit.WithAlpha(Color.black, 0.09f * (1 - i / (float)steps)));
            }
        }

        void DrawPage(int idx, int side, Rect r)
        {
            var slot = idx - 1;
            if (idx == 0)
            {
                if (side == 0) UIKit.Fill(r, UIKit.Hex("#8a1038"));
                else DrawCover(r);
            }
            else if (idx == N + 1)
            {
                if (side == 1) { Paper(r, 1, UIKit.Paper); DrawDoodles(r, idx); }
                else DrawEnd(r, side);
            }
            else if (slot >= unlocked.Count)
            {
                if (side == 1) { Paper(r, 1, UIKit.Pink100); DrawDoodles(r, idx); }
                else DrawLocked(r, side, slot);
            }
            else DrawMemory(r, side, idx, unlocked[slot]);
        }

        void DrawMemory(Rect r, int side, int idx, Memory memory)
        {
            Paper(r, side, UIKit.Paper);
            if (side != 1)
            {
                var hasPhotos = side == -1 || memory.media.Length / 2 > 0;
                var titleHeight = r.height * (side == 0 ? 0.26f : 0.2f);
                var box = hasPhotos
                    ? new Rect(r.x + 26 * s, r.y + 14 * s, r.width - 52 * s, titleHeight - 16 * s)
                    : new Rect(r.x + 26 * s, r.y + r.height * 0.3f, r.width - 52 * s, r.height * 0.4f);
                UIKit.Label(new Rect(box.x, box.y, box.width, 22 * s), $"MEMORY {idx} OF {N}", 14 * s, UIKit.Rose400);
                UIKit.Label(new Rect(box.x, box.y + 22 * s, box.width, box.height - 22 * s - (string.IsNullOrEmpty(memory.date) ? 0 : 30 * s)),
                    memory.caption, hasPhotos ? 32 * s : 38 * s, UIKit.Rose700);
                if (!string.IsNullOrEmpty(memory.date))
                {
                    var chip = new Rect(box.center.x - 80 * s, box.yMax - 28 * s, 160 * s, 26 * s);
                    UIKit.Fill(chip, UIKit.WithAlpha(UIKit.Rose400, 0.15f));
                    UIKit.Label(chip, memory.date, 15 * s, UIKit.Rose500);
                }
                if (!hasPhotos) UIKit.Label(new Rect(r.x, r.y + r.height * 0.68f, r.width, 90 * s), "♥", 80 * s, UIKit.WithAlpha(UIKit.Rose300, 0.5f));
                var tape = new Rect(r.center.x - 38 * s, r.y - 6 * s, 76 * s, 24 * s);
                UIKit.Rotated(-1.5f, tape, () => UIKit.Fill(tape, UIKit.WithAlpha(UIKit.Tape[idx % UIKit.Tape.Length], 0.65f)));
            }
            else
            {
                UIKit.Label(new Rect(r.x, r.yMax - 34 * s, r.width, 26 * s), "♡", 20 * s, UIKit.Rose300);
            }

            var slots = PhotoSlots(idx, side, r);
            for (var i = 0; i < slots.Count; i++)
            {
                var slotCopy = slots[i];
                var tilt = ((slotCopy.n * 5 + idx) % 7 - 3) * 1.1f;
                UIKit.Rotated(tilt, slotCopy.rect, () =>
                {
                    var f = slotCopy.rect;
                    UIKit.Fill(new Rect(f.x + 3 * s, f.y + 5 * s, f.width, f.height), UIKit.WithAlpha(Color.black, 0.16f));
                    UIKit.Fill(f, Color.white);
                    DrawMedia(new Rect(f.x + 7 * s, f.y + 7 * s, f.width - 14 * s, f.height - 14 * s - 14 * s), slotCopy.item);
                    UIKit.DrawTape(f, slotCopy.n + idx, s);
                });
            }
        }

        void DrawLocked(Rect r, int side, int slot)
        {
            Paper(r, side, UIKit.Pink100);
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.14f, r.width, 34 * s), $"MEMORY {slot + 1} OF {N}", 14 * s, UIKit.Rose400);
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.24f, r.width, r.height * 0.28f), "?", 130 * s, UIKit.WithAlpha(UIKit.Rose300, 0.8f));
            UIKit.Label(new Rect(r.x + 24 * s, r.y + r.height * 0.56f, r.width - 48 * s, 70 * s), "Keep skating to unlock", 30 * s, UIKit.Rose400);
            UIKit.Label(new Rect(r.x + 24 * s, r.y + r.height * 0.68f, r.width - 48 * s, 60 * s), "This page is waiting for a memory ♡", 18 * s, UIKit.Rose300, TextAnchor.MiddleCenter, true, FontStyle.Normal);
            DrawDoodles(r, slot);
        }

        void DrawEnd(Rect r, int side)
        {
            Paper(r, side, UIKit.Paper);
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.25f, r.width, 60 * s), "The End", 48 * s, UIKit.Rose600);
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.25f + 60 * s, r.width, 40 * s), "for now ♡", 26 * s, UIKit.Rose400);
            UIKit.Label(new Rect(r.x + 20 * s, r.y + r.height * 0.55f, r.width - 40 * s, 60 * s),
                unlocked.Count >= N ? "Every memory is in the book." : $"{unlocked.Count} of {N} memories found.\nKeep skating to fill the rest.",
                20 * s, UIKit.Rose500, TextAnchor.MiddleCenter, true, FontStyle.Normal);
            DrawDoodles(r, 3);
        }

        void DrawCover(Rect r)
        {
            UIKit.Fill(r, UIKit.Hex("#be123c"));
            // Gold double border.
            var gold = UIKit.WithAlpha(UIKit.Hex("#f5c26b"), 0.85f);
            foreach (var inset in new[] { 16 * s, 24 * s })
            {
                var b = new Rect(r.x + inset, r.y + inset, r.width - 2 * inset, r.height - 2 * inset);
                UIKit.Fill(new Rect(b.x, b.y, b.width, 2), gold);
                UIKit.Fill(new Rect(b.x, b.yMax - 2, b.width, 2), gold);
                UIKit.Fill(new Rect(b.x, b.y, 2, b.height), gold);
                UIKit.Fill(new Rect(b.xMax - 2, b.y, 2, b.height), gold);
            }
            ballTexture ??= MakeBallTexture();
            var size = Mathf.Min(r.width, r.height) * 0.32f;
            // The cover pokeball hands over to the opening animation while it plays.
            if (ballTime < 0) GUI.DrawTexture(new Rect(r.center.x - size / 2, r.y + r.height * 0.14f, size, size), ballTexture);
            var cream = new Color(1f, 0.96f, 0.93f);
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.14f + size + 12 * s, r.width, 60 * s), "Our Memory Book", 40 * s, cream);
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.14f + size + 66 * s, r.width, 30 * s), "P O K E - M E M O R I E S", 15 * s, UIKit.WithAlpha(cream, 0.75f));
            UIKit.Label(new Rect(r.x, r.y + r.height * 0.14f + size + 110 * s, r.width, 60 * s), "♥", 40 * s, gold);
            UIKit.Label(new Rect(r.x, r.yMax - 70 * s, r.width, 36 * s), "Tap to open ♡", 20 * s, cream);
        }

        void DrawDoodles(Rect r, int seed)
        {
            string[] glyphs = { "♥", "★", "♡" };
            for (var i = 0; i < 3; i++)
            {
                var x = r.x + r.width * (0.15f + 0.35f * ((seed + i * 2) % 3));
                var y = r.y + r.height * (0.08f + 0.36f * ((seed + i) % 3)) + (i == 1 ? r.height * 0.45f : 0);
                UIKit.Label(new Rect(x, y, 40 * s, 40 * s), glyphs[(seed + i) % 3], 26 * s, UIKit.WithAlpha(UIKit.Rose300, 0.35f));
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
                UIKit.Fill(badge, UIKit.WithAlpha(Color.white, 0.9f));
                UIKit.Label(badge, "▶", size * 0.55f, UIKit.Rose500);
                return;
            }
            var texture = Photo(item);
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleAndCrop);
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

        public void Draw()
        {
            if (Event.current.type != EventType.Repaint) return;
            Layout();

            // Dark plum desk, a vignette, then the book.
            UIKit.Fill(new Rect(0, 0, Screen.width, Screen.height), UIKit.Hex("#2b1a29"));
            UIKit.Fill(new Rect(0, 0, Screen.width, Screen.height * 0.5f), UIKit.WithAlpha(UIKit.Hex("#4a2a42"), 0.35f));

            var shown = CoverRect;
            var board = new Rect(shown.x - 12 * s, shown.y - 12 * s, shown.width + 24 * s, shown.height + 24 * s);
            UIKit.Fill(new Rect(board.x + 6 * s, board.y + 10 * s, board.width, board.height), UIKit.WithAlpha(Color.black, 0.35f));
            UIKit.Fill(board, UIKit.Hex("#7f1237"));
            // The stack of page edges peeking out below the pages.
            UIKit.Fill(new Rect(shown.x - 4 * s, shown.y - 4 * s, shown.width + 8 * s, shown.height + 8 * s), UIKit.Hex("#e8dccb"));

            if (Closed) DrawCover(shown);
            else if (turnTime < 0 || ballTime >= 0) DrawSpread(index);
            else DrawTurning();

            if (spread && !Closed && (index > 0 && index < PageCount - 1 || turnTime >= 0))
                UIKit.Fill(new Rect(book.center.x - 1 * s, book.y, 2 * s, book.height), UIKit.WithAlpha(Color.black, 0.18f));

            // Controls.
            UIKit.Button(BackButton, "◀ Menu", Color.white, UIKit.Rose500, 20 * s);
            if (index > 0) UIKit.Button(PrevButton, "◀", Color.white, UIKit.Rose500, 24 * s);
            if (index < PageCount - 1) UIKit.Button(NextButton, "▶", UIKit.Rose500, Color.white, 24 * s);
            var label = index == 0 ? "Tap the cover or swipe to open" : index <= N ? $"Memory {index} of {N}" : "";
            UIKit.Label(new Rect(book.x + 110 * s, Screen.height - 64 * s, book.width - 220 * s, 48 * s), label, 20 * s, UIKit.Hex("#f5d7dc"));

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
            var spine = new Vector2(SpineX, book.center.y);

            if (spread)
            {
                // The turning page folds about the spine: its front narrows to nothing, then its
                // back widens on the other side.
                DrawPage(forward ? a : b, 0, leftPage);
                DrawPage(forward ? b : a, 1, rightPage);
                var firstHalf = t < 0.5f;
                var width = Mathf.Max(0.001f, firstHalf ? 1 - 2 * t : 2 * t - 1);
                int idx, side;
                Rect page;
                if (forward) { idx = firstHalf ? a : b; side = firstHalf ? 1 : 0; }
                else { idx = firstHalf ? a : b; side = firstHalf ? 0 : 1; }
                page = side == 0 ? leftPage : rightPage;
                Flip(width, spine, idx, side, page, t);
            }
            else
            {
                DrawPage(forward ? b : a, -1, singlePage);
                var width = Mathf.Max(0.001f, forward ? 1 - t : t);
                Flip(width, new Vector2(book.x, book.center.y), forward ? a : b, -1, singlePage, t);
            }
        }

        void Flip(float widthScale, Vector2 pivot, int idx, int side, Rect page, float t)
        {
            var matrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(widthScale, 1), pivot);
            DrawPage(idx, side, page);
            UIKit.Fill(page, UIKit.WithAlpha(Color.black, 0.28f * Mathf.Sin(t * Mathf.PI)));
            GUI.matrix = matrix;
        }

        void DrawViewer()
        {
            UIKit.Fill(new Rect(0, 0, Screen.width, Screen.height), UIKit.WithAlpha(Color.black, 0.82f));
            var card = new Rect(Screen.width * 0.04f, Screen.height * 0.05f, Screen.width * 0.92f, Screen.height * 0.9f);
            UIKit.Fill(card, Color.white);
            var area = new Rect(card.x + 14 * s, card.y + 14 * s, card.width - 28 * s, card.height - 98 * s);
            UIKit.Fill(area, UIKit.Paper);

            if (viewing.IsVideo)
            {
                if (videoFailed) UIKit.Label(area, "This video can't play here.\nTap to go back.", 24 * s, UIKit.Rose400);
                else if (video != null && video.isPlaying && video.texture != null) GUI.DrawTexture(area, video.texture, ScaleMode.ScaleToFit);
                else UIKit.Label(area, "Loading video…", 24 * s, UIKit.Rose300);
            }
            else
            {
                var texture = Photo(viewing);
                if (texture != null) GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
                else UIKit.Label(area, failed.Contains(viewing.url) ? "Couldn't load this photo" : "Loading…", 24 * s, UIKit.Rose300);
            }
            UIKit.Label(new Rect(card.x, card.yMax - 66 * s, card.width, 56 * s), viewingMemory != null ? viewingMemory.caption : "", 32 * s, UIKit.Rose600);
            UIKit.Label(new Rect(card.xMax - 60 * s, card.y + 8 * s, 52 * s, 52 * s), "✕", 28 * s, UIKit.Rose400);
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
                    colour.a = (byte)(Mathf.Clamp01(62.5f - d) * 255);
                    pixels[y * n + x] = colour;
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
