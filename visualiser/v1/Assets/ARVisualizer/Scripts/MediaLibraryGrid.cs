using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ARVisualizer
{
    /// <summary>A continuous list backed by a small recycled tile pool and bounded thumbnail cache.</summary>
    public sealed class MediaLibraryGrid : MonoBehaviour
    {
        [SerializeField] ScrollRect scroll;
        [SerializeField] MediaTile[] tiles = Array.Empty<MediaTile>();
        [Header("Tile layout (canvas units)")]
        [SerializeField, Range(1, 3)] int columns = 3;
        [SerializeField] Vector2 tileSize = new Vector2(288, 186);
        [SerializeField] Vector2 spacing = new Vector2(16, 18);

        public ScrollRect Scroll => scroll;
        public bool IsLoading => pendingBatches.Count > 0;
        public int Total { get; private set; }
        public int CachedThumbnailCount { get; private set; }
        public event Action<LibraryAsset> Selected;
        public event Action<string> StatusChanged;

        const int BatchSize = 6;
        const int CachedBatches = 8;
        const int ConcurrentRequests = 2;

        sealed class Batch
        {
            public LibraryAsset[] assets;
            public Texture2D[] images;
            public int lastUsed;
        }

        readonly Dictionary<int, Batch> cache = new Dictionary<int, Batch>();
        readonly HashSet<int> pendingBatches = new HashSet<int>();
        readonly HashSet<int> failedBatches = new HashSet<int>();
        PhotoLibrary library;
        int[] tileIndices;
        int generation;
        int accessOrder;
        int firstVisibleIndex;
        bool active;
        bool interactive;
        bool limited;
        bool refreshing;
        string error;

        public void Initialize(PhotoLibrary source)
        {
            library = source;
            tileIndices = new int[tiles.Length];
            scroll.onValueChanged.AddListener(_ => RefreshWindow());
            for (int i = 0; i < tiles.Length; ++i)
            {
                int slot = i;
                tiles[i].button.onClick.AddListener(() => SelectTile(slot));
            }
            Clear();
        }

        void SelectTile(int slot)
        {
            if (!interactive || !TryAsset(tileIndices[slot], out var asset, out _)) return;
            Selected?.Invoke(asset);
        }

        public void Load(bool selectedPhotosOnly)
        {
            Clear();
            active = true;
            interactive = true;
            limited = selectedPhotosOnly;
            scroll.StopMovement();
            scroll.content.anchoredPosition = Vector2.zero;
            RequestBatch(0);
        }

        public void SetInteractable(bool value)
        {
            interactive = value;
            RefreshWindow();
        }

        public void Clear()
        {
            // Late replies belong to an older list, even if the user has already reopened it.
            ++generation;
            active = false;
            pendingBatches.Clear();
            failedBatches.Clear();
            error = null;
            Total = 0;
            if (scroll != null) scroll.StopMovement();
            foreach (var tile in tiles)
            {
                if (tile.thumbnail != null) tile.thumbnail.texture = null;
                if (tile.button != null) tile.button.gameObject.SetActive(false);
            }
            foreach (var batch in cache.Values) ReleaseImages(batch);
            cache.Clear();
            CachedThumbnailCount = 0;
        }

        void RequestBatch(int number)
        {
            if (!active || pendingBatches.Count >= ConcurrentRequests || pendingBatches.Contains(number)
                || cache.ContainsKey(number) || failedBatches.Contains(number)) return;

            pendingBatches.Add(number);
            int version = generation;
            library.Request("page", "", number, result => ReceiveBatch(number, version, result));
        }

        void ReceiveBatch(int number, int version, LibraryResult result)
        {
            if (this == null || !active || generation != version)
            {
                PhotoLibrary.DeleteResultFiles(result);
                return;
            }

            pendingBatches.Remove(number);
            if (!result.ok)
            {
                PhotoLibrary.DeleteResultFiles(result);
                failedBatches.Add(number);
                error = result.error;
                RefreshWindow();
                return;
            }

            Total = Mathf.Max(0, result.total);
            var batch = ReadBatch(result.assets ?? Array.Empty<LibraryAsset>());
            PhotoLibrary.DeleteResultFiles(result);
            cache[number] = batch;
            RefreshWindow();
        }

        Batch ReadBatch(LibraryAsset[] assets)
        {
            var batch = new Batch
            {
                assets = assets,
                images = new Texture2D[assets.Length],
                lastUsed = ++accessOrder
            };
            for (int i = 0; i < assets.Length; ++i)
            {
                batch.images[i] = PhotoLibrary.ReadImage(assets[i].path);
                if (batch.images[i] != null) ++CachedThumbnailCount;
            }
            return batch;
        }

        bool TryAsset(int index, out LibraryAsset asset, out Texture2D image)
        {
            asset = null;
            image = null;
            if (index < 0 || index >= Total || !cache.TryGetValue(index / BatchSize, out var batch)) return false;
            int offset = index % BatchSize;
            if (offset >= batch.assets.Length) return false;

            batch.lastUsed = ++accessOrder;
            asset = batch.assets[offset];
            image = batch.images[offset];
            return true;
        }

        void RefreshWindow()
        {
            if (!active || refreshing || scroll == null) return;
            refreshing = true;
            try
            {
                float rowStride = Mathf.Max(1, tileSize.y + spacing.y);
                UpdateContentBounds(rowStride);
                float left = (scroll.content.rect.width - columns * tileSize.x - (columns - 1) * spacing.x) * 0.5f;
                for (int i = 0; i < tiles.Length; ++i)
                    UpdateTile(i, firstVisibleIndex + i, left, rowStride);

                TrimCache();
                StatusChanged?.Invoke(GetStatus());
            }
            finally
            {
                refreshing = false;
            }
        }

        void UpdateContentBounds(float rowStride)
        {
            int rows = Mathf.CeilToInt((float)Total / columns);
            float height = Mathf.Max(scroll.viewport.rect.height, rows * rowStride - spacing.y);
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            float maximumOffset = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
            var offset = scroll.content.anchoredPosition;
            offset.y = Mathf.Clamp(offset.y, 0, maximumOffset);
            scroll.content.anchoredPosition = offset;

            // Keep one row above the viewport in the pool, ready for an upward swipe.
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(offset.y / rowStride) - 1);
            firstVisibleIndex = firstRow * columns;
        }

        void UpdateTile(int slot, int index, float left, float rowStride)
        {
            tileIndices[slot] = index;
            var tile = tiles[slot];
            tile.button.gameObject.SetActive(index < Total);
            if (index >= Total)
            {
                tile.thumbnail.texture = null;
                return;
            }

            var rect = (RectTransform)tile.button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = tileSize;
            rect.anchoredPosition = new Vector2(left + index % columns * (tileSize.x + spacing.x), -(index / columns) * rowStride);

            int batchNumber = index / BatchSize;
            bool loaded = TryAsset(index, out var asset, out var texture);
            tile.button.interactable = interactive && loaded;
            tile.caption.text = loaded ? asset.caption
                : failedBatches.Contains(batchNumber) ? "Unavailable / refresh to retry" : "Loading…";
            tile.thumbnail.texture = texture;
            tile.thumbnail.GetComponent<AspectRatioFitter>().aspectRatio = texture != null ? (float)texture.width / texture.height : 1;
            if (!loaded && interactive) RequestBatch(batchNumber);
        }

        string GetStatus()
        {
            if (!string.IsNullOrEmpty(error)) return error;
            if (Total == 0 && !IsLoading) return "No accessible photos or videos. Check Photos access in Settings.";

            return (limited ? "Selected Photos · " : "") + (IsLoading ? "Loading… · " : "")
                + Total + " items · Swipe up / down";
        }

        void TrimCache()
        {
            while (cache.Count > CachedBatches)
            {
                int oldest = FindOldestOffscreenBatch();
                if (oldest < 0) break;
                ReleaseImages(cache[oldest]);
                cache.Remove(oldest);
            }
        }

        int FindOldestOffscreenBatch()
        {
            int firstBatch = firstVisibleIndex / BatchSize;
            int lastBatch = (firstVisibleIndex + tiles.Length - 1) / BatchSize;
            int oldest = -1;
            int oldestAccess = int.MaxValue;
            foreach (var entry in cache)
            {
                if (entry.Key >= firstBatch && entry.Key <= lastBatch) continue;
                if (entry.Value.lastUsed >= oldestAccess) continue;
                oldest = entry.Key;
                oldestAccess = entry.Value.lastUsed;
            }
            return oldest;
        }

        void ReleaseImages(Batch batch)
        {
            foreach (var image in batch.images)
            {
                if (image == null) continue;
                Destroy(image);
                --CachedThumbnailCount;
            }
        }

        void OnDestroy() => Clear();
    }
}
