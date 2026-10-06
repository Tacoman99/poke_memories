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
    /// The scrapbook: a gallery of polaroids for unlocked memories and taped-down "???" slots
    /// for the locked ones. Opening a polaroid plays a pokeball animation, then shows that
    /// memory's photos and videos on a scrapbook page; tapping one views it closer.
    /// Ported from the web game's MemoryGallery.tsx.
    /// </summary>
    public class MemoryBook : MonoBehaviour
    {
        const int MaxTextureSize = 1024;
        const float OpenSeconds = 0.9f;
        const float DetailHeader = 150;

        public Action OnBack;

        readonly Dictionary<string, Texture2D> photos = new();
        readonly HashSet<string> loading = new(), failed = new();
        readonly Queue<string> pending = new();
        int activeDownloads;

        float scrollY, detailScroll;
        Memory selected, opening;
        float openTimer = -1;
        MediaItem viewing;
        VideoPlayer video;
        bool videoFailed;
        Texture2D ballTexture;

        // Gallery layout, recomputed every frame so rotation and resizing just work.
        float s, margin, gap, header, cardW, cardH, pad;
        int cols;

        List<Memory> Unlocked
        {
            get
            {
                var ids = SaveStore.Data.unlockedMemoryIds;
                return MemoryPool.All.Where(m => ids.Contains(m.id)).ToList();
            }
        }

        public void Open()
        {
            scrollY = 0;
            selected = opening = null;
            viewing = null;
            openTimer = -1;
            StopVideo();
        }

        void OnDisable() => StopVideo();

        // ───────────── Layout ─────────────

        void Layout()
        {
            s = UIInput.Scale;
            margin = 24 * s;
            gap = 18 * s;
            pad = 10 * s;
            header = 84 * s;
            cols = Mathf.Clamp(Mathf.FloorToInt((Screen.width - 2 * margin + gap) / (230 * s + gap)), 2, 5);
            cardW = (Screen.width - 2 * margin - gap * (cols - 1)) / cols;
            cardH = pad + (cardW - 2 * pad) * 1.15f + 72 * s;
        }

        int SlotCount => MemoryPool.All.Count;
        Rect CardRect(int i) => new(margin + i % cols * (cardW + gap), header + margin + i / cols * (cardH + gap) - scrollY, cardW, cardH);
        float ContentHeight => header + margin + Mathf.CeilToInt(SlotCount / (float)cols) * (cardH + gap) + 90 * s;
        Rect BackButton => new(margin, 16 * s, 64 * s, 52 * s);

        Rect PanelRect()
        {
            var w = Mathf.Min(Screen.width - 32 * s, 900 * s);
            var h = Screen.height * 0.9f;
            return new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        }

        // The media grid inside the detail panel, in panel-local coordinates.
        int DetailCols(Memory m) => m.media.Length == 1 ? 1 : m.media.Length == 2 || PanelRect().width < 700 * s ? 2 : 3;

        float DetailThumb(Memory m)
        {
            var c = DetailCols(m);
            var inner = PanelRect().width - 48 * s;
            return c == 1 ? Mathf.Min(inner, 420 * s) : (inner - gap * (c - 1)) / c;
        }

        Rect DetailItemRect(Memory m, int i)
        {
            var c = DetailCols(m);
            var size = DetailThumb(m);
            var rowWidth = c * size + (c - 1) * gap;
            var left = (PanelRect().width - rowWidth) / 2;
            return new Rect(left + i % c * (size + gap), DetailHeader * s + i / c * (size + gap) - detailScroll, size, size);
        }

        float DetailContentHeight(Memory m) =>
            DetailHeader * s + Mathf.CeilToInt(m.media.Length / (float)DetailCols(m)) * (DetailThumb(m) + gap) + 70 * s;

        // ───────────── Input ─────────────

        public void Tick()
        {
            Layout();
            var escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

            if (openTimer >= 0)
            {
                openTimer += Time.unscaledDeltaTime;
                if (openTimer >= OpenSeconds)
                {
                    selected = opening;
                    opening = null;
                    openTimer = -1;
                    detailScroll = 0;
                    foreach (var item in selected.media)
                        if (!item.IsVideo) Photo(item.url);
                }
                return;
            }

            if (viewing != null)
            {
                if (escape || UIInput.Tap) CloseViewer();
                return;
            }

            if (selected != null)
            {
                TickDetail(escape);
                return;
            }

            // Gallery: drag or wheel to scroll, tap a polaroid to open it, back button or Escape to leave.
            var maxScroll = Mathf.Max(0, ContentHeight - Screen.height);
            scrollY = Mathf.Clamp(scrollY - UIInput.DragDelta.y - UIInput.Wheel * 0.5f * s, 0, maxScroll);

            if (escape || UIInput.TapIn(BackButton))
            {
                OnBack?.Invoke();
                return;
            }
            if (!UIInput.Tap || UIInput.TapPosition.y < header) return;
            var unlocked = Unlocked;
            for (var i = 0; i < unlocked.Count; i++)
            {
                if (!CardRect(i).Contains(UIInput.TapPosition)) continue;
                opening = unlocked[i];
                openTimer = 0;
                return;
            }
        }

        void TickDetail(bool escape)
        {
            var panel = PanelRect();
            var maxScroll = Mathf.Max(0, DetailContentHeight(selected) - panel.height);
            detailScroll = Mathf.Clamp(detailScroll - UIInput.DragDelta.y - UIInput.Wheel * 0.5f * s, 0, maxScroll);

            var closeButton = new Rect(panel.xMax - 48 * s, panel.y + 10 * s, 38 * s, 38 * s);
            if (escape || UIInput.TapIn(closeButton) || (UIInput.Tap && !panel.Contains(UIInput.TapPosition)))
            {
                selected = null;
                return;
            }
            if (!UIInput.Tap) return;
            var local = UIInput.TapPosition - panel.position;
            for (var i = 0; i < selected.media.Length; i++)
            {
                if (!DetailItemRect(selected, i).Contains(local)) continue;
                viewing = selected.media[i];
                if (viewing.IsVideo) StartVideo(viewing.url);
                return;
            }
        }

        void CloseViewer()
        {
            StopVideo();
            viewing = null;
        }

        // ───────────── Photos ─────────────

        /// <summary>Returns the downloaded photo, or null while it loads (or if it failed).</summary>
        Texture2D Photo(string url)
        {
            if (photos.TryGetValue(url, out var texture)) return texture;
            if (!loading.Contains(url) && !failed.Contains(url))
            {
                loading.Add(url);
                pending.Enqueue(url);
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

        IEnumerator Fetch(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                Texture2D texture = null;
                if (request.result == UnityWebRequest.Result.Success)
                    texture = Decode(request.downloadHandler.data, MaxTextureSize);
                if (texture != null) photos[url] = texture;
                else failed.Add(url);
            }
            loading.Remove(url);
            activeDownloads--;
            PumpDownloads();
        }

        /// <summary>Decodes a JPEG/PNG, shrinks it to `max` pixels and applies its EXIF rotation.</summary>
        static Texture2D Decode(byte[] bytes, int max)
        {
            var orientation = ExifOrientation(bytes);
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
            if (orientation is 3 or 6 or 8)
            {
                var from = flat.GetPixels32();
                var to = new Color32[from.Length];
                var swapped = orientation != 3;
                result = new Texture2D(swapped ? h : w, swapped ? w : h, TextureFormat.RGBA32, false);
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        var index = orientation switch
                        {
                            6 => (w - 1 - x) * h + y,           // rotate 90° clockwise
                            8 => x * h + (h - 1 - y),           // rotate 90° counter-clockwise
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

            UIKit.Fill(new Rect(0, 0, Screen.width, Screen.height), UIKit.Paper);
            UIKit.Fill(new Rect(0, 0, 4 * s, Screen.height), UIKit.WithAlpha(UIKit.Rose300, 0.6f));

            DrawGallery();
            if (openTimer >= 0) DrawPokeball(openTimer / OpenSeconds);
            if (selected != null) DrawDetail(selected);
            if (viewing != null) DrawViewer();
        }

        static float Rotation(int index) => (index * 7 + 3) % 9 - 4;

        void DrawGallery()
        {
            var unlocked = Unlocked;
            for (var i = 0; i < SlotCount; i++)
            {
                var card = CardRect(i);
                if (card.yMax < header || card.y > Screen.height) continue;
                if (i < unlocked.Count) DrawPolaroid(card, unlocked[i], i);
                else DrawLocked(card, i);
            }

            var footerY = header + margin + Mathf.CeilToInt(SlotCount / (float)cols) * (cardH + gap) - scrollY;
            UIKit.Label(new Rect(0, footerY, Screen.width, 60 * s), $"{unlocked.Count} of {SlotCount} memories unlocked", 26 * s, UIKit.Rose400);

            // The header bar sits over the scrolled cards.
            UIKit.Fill(new Rect(0, 0, Screen.width, header), UIKit.Paper);
            UIKit.Fill(new Rect(0, header - 2, Screen.width, 2), UIKit.WithAlpha(UIKit.Rose300, 0.35f));
            UIKit.Button(BackButton, "◀", Color.white, UIKit.Rose500, 26 * s);
            UIKit.Label(new Rect(0, 0, Screen.width, header), "♥ My Memory Book", 40 * s, UIKit.Rose600);
        }

        void DrawPolaroid(Rect card, Memory memory, int index)
        {
            UIKit.Rotated(Rotation(index), card, () =>
            {
                UIKit.Fill(new Rect(card.x + 3 * s, card.y + 5 * s, card.width, card.height), UIKit.WithAlpha(Color.black, 0.14f));
                UIKit.Fill(card, Color.white);
                var photo = new Rect(card.x + pad, card.y + pad, card.width - 2 * pad, (card.width - 2 * pad) * 1.15f);
                DrawMedia(photo, memory.media[0], 1f);
                if (memory.media.Length > 1)
                {
                    var badge = new Rect(photo.xMax - 54 * s, photo.y + 6 * s, 48 * s, 24 * s);
                    UIKit.Fill(badge, UIKit.WithAlpha(Color.black, 0.5f));
                    UIKit.Label(badge, $"+{memory.media.Length}", 16 * s, Color.white);
                }
                UIKit.Label(new Rect(card.x + pad, photo.yMax + 2 * s, card.width - 2 * pad, 44 * s), memory.caption, 18 * s, UIKit.Rose700, TextAnchor.UpperLeft, true);
                if (!string.IsNullOrEmpty(memory.date))
                    UIKit.Label(new Rect(card.x + pad, photo.yMax + 46 * s, card.width - 2 * pad, 22 * s), memory.date, 15 * s, UIKit.Rose400, TextAnchor.MiddleLeft, false, FontStyle.Normal);
                UIKit.DrawTape(card, index, s);
            });
        }

        void DrawLocked(Rect card, int index)
        {
            UIKit.Rotated(Rotation(index), card, () =>
            {
                UIKit.Fill(card, UIKit.WithAlpha(Color.white, 0.4f));
                var photo = new Rect(card.x + pad, card.y + pad, card.width - 2 * pad, (card.width - 2 * pad) * 1.15f);
                UIKit.Fill(photo, UIKit.WithAlpha(UIKit.Pink100, 0.5f));
                UIKit.Label(new Rect(photo.x, photo.y + photo.height * 0.25f, photo.width, photo.height * 0.3f), "?", 56 * s, UIKit.Rose300);
                UIKit.Label(new Rect(photo.x, photo.y + photo.height * 0.58f, photo.width, 30 * s), "Keep playing!", 18 * s, UIKit.Rose300);
                UIKit.Label(new Rect(card.x + pad, photo.yMax + 2 * s, card.width - 2 * pad, 28 * s), "???", 20 * s, UIKit.Rose300, TextAnchor.MiddleLeft, false);
                UIKit.DrawTape(card, index, s, 0.25f);
            });
        }

        /// <summary>A photo cropped to fill `rect`, or a play-badged tile for a video.</summary>
        void DrawMedia(Rect rect, MediaItem item, float playScale)
        {
            UIKit.Fill(rect, UIKit.Rose50);
            if (item.IsVideo)
            {
                UIKit.Fill(rect, UIKit.WithAlpha(UIKit.Rose300, 0.35f));
                var size = Mathf.Min(rect.width, rect.height) * 0.3f * playScale;
                var badge = new Rect(rect.center.x - size / 2, rect.center.y - size / 2, size, size);
                UIKit.Fill(badge, UIKit.WithAlpha(Color.white, 0.9f));
                UIKit.Label(badge, "▶", size * 0.55f, UIKit.Rose500);
                return;
            }
            var texture = Photo(item.url);
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleAndCrop);
            else UIKit.Label(rect, failed.Contains(item.url) ? "✕" : "…", 30 * s, UIKit.Rose300);
        }

        void DrawDetail(Memory memory)
        {
            var panel = PanelRect();
            UIKit.Fill(new Rect(0, 0, Screen.width, Screen.height), UIKit.WithAlpha(Color.black, 0.75f));
            UIKit.Fill(new Rect(panel.x - 3, panel.y - 3, panel.width + 6, panel.height + 6), UIKit.Rose300);
            UIKit.Fill(panel, UIKit.Paper);

            GUI.BeginClip(panel);
            UIKit.Label(new Rect(16 * s, 30 * s - detailScroll, panel.width - 32 * s, 60 * s), memory.caption, 38 * s, UIKit.Rose700);
            if (!string.IsNullOrEmpty(memory.date))
            {
                var chip = new Rect(panel.width / 2 - 80 * s, 92 * s - detailScroll, 160 * s, 30 * s);
                UIKit.Fill(chip, UIKit.WithAlpha(UIKit.Rose400, 0.15f));
                UIKit.Label(chip, memory.date, 17 * s, UIKit.Rose500);
            }
            UIKit.Fill(new Rect(panel.width / 2 - 140 * s, 132 * s - detailScroll, 280 * s, 2), UIKit.WithAlpha(UIKit.Rose300, 0.7f));

            for (var i = 0; i < memory.media.Length; i++)
            {
                var item = DetailItemRect(memory, i);
                if (item.yMax < 0 || item.y > panel.height) continue;
                var index = i;
                UIKit.Rotated((i * 5 + 2) % 5 - 2, item, () =>
                {
                    UIKit.Fill(new Rect(item.x + 2 * s, item.y + 4 * s, item.width, item.height), UIKit.WithAlpha(Color.black, 0.14f));
                    UIKit.Fill(item, Color.white);
                    DrawMedia(new Rect(item.x + 6 * s, item.y + 6 * s, item.width - 12 * s, item.height - 12 * s), memory.media[index], 1.2f);
                });
            }
            UIKit.Label(new Rect(0, DetailContentHeight(memory) - 60 * s - detailScroll, panel.width, 40 * s), "Tap a photo to view closer", 20 * s, UIKit.Rose300);
            GUI.EndClip();

            var tape = new Rect(panel.center.x - 40 * s, panel.y - 10 * s, 80 * s, 28 * s);
            var tapeColour = UIKit.Tape[Mathf.Abs(memory.id.GetHashCode()) % UIKit.Tape.Length];
            UIKit.Rotated(-1.5f, tape, () => UIKit.Fill(tape, UIKit.WithAlpha(tapeColour, 0.6f)));
            var close = new Rect(panel.xMax - 48 * s, panel.y + 10 * s, 38 * s, 38 * s);
            UIKit.Fill(close, UIKit.WithAlpha(Color.white, 0.85f));
            UIKit.Label(close, "✕", 22 * s, UIKit.Rose400);
        }

        void DrawViewer()
        {
            UIKit.Fill(new Rect(0, 0, Screen.width, Screen.height), UIKit.WithAlpha(Color.black, 0.8f));
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
                var texture = Photo(viewing.url);
                if (texture != null) GUI.DrawTexture(area, texture, ScaleMode.ScaleToFit);
                else UIKit.Label(area, failed.Contains(viewing.url) ? "Couldn't load this photo" : "Loading…", 24 * s, UIKit.Rose300);
            }
            UIKit.Label(new Rect(card.x, card.yMax - 66 * s, card.width, 56 * s), selected != null ? selected.caption : "", 32 * s, UIKit.Rose600);
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
