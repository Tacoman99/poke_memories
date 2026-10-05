using UnityEngine;

namespace PokeMemories.Gameplay
{
    /// <summary>
    /// Layered scrolling backdrop: a sky gradient plus far / mid / near tiles that scroll at
    /// different speeds. One set per course section; the next set fades in over the end of
    /// the current section. Purely visual: it only reads the distance the simulation reports.
    /// </summary>
    public class ParallaxBackground : MonoBehaviour
    {
        // Fraction of the camera's motion each layer follows (0 = fixed to the screen, 1 = ground speed).
        static readonly float[] Speeds = { 0f, 0.12f, 0.35f, 0.65f };
        const float FadeStart = 0.93f;      // fraction of a section at which the next set starts fading in
        const int BaseOrder = -200;
        const int LayerOrderStep = 10;
        const int IncomingOrderOffset = 50; // draws the incoming set over the current one
        const float TileOverlap = 0.02f;    // world units; hides seams between tiles
        const int TilesPerLayer = 3;

        class Group
        {
            public SpriteRenderer sky;
            public SpriteRenderer[][] tiles; // [layer 0..2][tile]
            public GameObject root;
        }

        TrackArtSet art;
        Camera view;
        float groundY;
        Group[] groups;

        public Color GroundTint { get; private set; } = Color.white;

        public void Init(TrackArtSet artSet, Camera camera, float groundLine)
        {
            art = artSet;
            view = camera;
            groundY = groundLine;
            groups = new Group[art.backgrounds.Length];
            for (var i = 0; i < groups.Length; i++) groups[i] = Build(art.backgrounds[i]);
        }

        Group Build(TrackArtSet.BackgroundSet set)
        {
            var group = new Group { root = new GameObject($"Background {set.name}") };
            group.root.transform.SetParent(transform, false);
            group.sky = NewRenderer(group.root.transform, "sky", set.sky);
            var layers = new[] { set.far, set.mid, set.near };
            group.tiles = new SpriteRenderer[layers.Length][];
            for (var l = 0; l < layers.Length; l++)
            {
                group.tiles[l] = new SpriteRenderer[TilesPerLayer];
                for (var t = 0; t < TilesPerLayer; t++) group.tiles[l][t] = NewRenderer(group.root.transform, $"layer{l + 1}", layers[l]);
            }
            group.root.SetActive(false);
            return group;
        }

        static SpriteRenderer NewRenderer(Transform parent, string name, Sprite sprite)
        {
            var renderer = new GameObject(name).AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(parent, false);
            renderer.sprite = sprite;
            return renderer;
        }

        /// <param name="distance">Simulation distance in sim pixels.</param>
        /// <param name="cycle">Endless mode loops through the sets; the course stops on the last one.</param>
        public void Tick(float distance, float sectionLength, bool cycle)
        {
            var count = groups.Length;
            var section = Mathf.Max(0, Mathf.FloorToInt(distance / sectionLength));
            if (!cycle) section = Mathf.Min(section, count - 1);
            var current = section % count;
            var fraction = distance / sectionLength - section;

            var incoming = -1;
            var blend = 0f;
            if (fraction > FadeStart && (cycle || section + 1 < count))
            {
                incoming = (section + 1) % count;
                blend = Mathf.SmoothStep(0, 1, (fraction - FadeStart) / (1 - FadeStart));
            }

            for (var i = 0; i < count; i++) groups[i].root.SetActive(i == current || i == incoming);
            Place(groups[current], BaseOrder, 1);
            if (incoming >= 0) Place(groups[incoming], BaseOrder + IncomingOrderOffset, blend);
            GroundTint = incoming >= 0
                ? Color.Lerp(art.backgrounds[current].groundTint, art.backgrounds[incoming].groundTint, blend)
                : art.backgrounds[current].groundTint;
        }

        void Place(Group group, int baseOrder, float alpha)
        {
            var camera = view.transform.position;
            var halfHeight = view.orthographicSize;
            var colour = new Color(1, 1, 1, alpha);

            // Sky: stretched from the ground line up to the top of the view.
            var skyHeight = camera.y + halfHeight - groundY + 0.05f;
            var skyWidth = halfHeight * 2 * view.aspect + 1f;
            var size = group.sky.sprite.bounds.size;
            group.sky.transform.position = new Vector3(camera.x, groundY + skyHeight / 2, 0);
            group.sky.transform.localScale = new Vector3(skyWidth / size.x, skyHeight / size.y, 1);
            group.sky.sortingOrder = baseOrder;
            group.sky.color = colour;

            for (var l = 0; l < group.tiles.Length; l++)
            {
                var speed = Speeds[l + 1];
                var tileSize = group.tiles[l][0].sprite.bounds.size;
                var spacing = tileSize.x - TileOverlap;
                var origin = camera.x * (1 - speed);
                var first = Mathf.Round((camera.x - origin) / spacing);
                for (var t = 0; t < group.tiles[l].Length; t++)
                {
                    var tile = group.tiles[l][t];
                    tile.transform.position = new Vector3(origin + (first + t - 1) * spacing, groundY + tileSize.y / 2, 0);
                    tile.sortingOrder = baseOrder + (l + 1) * LayerOrderStep;
                    tile.color = colour;
                }
            }
        }
    }
}
