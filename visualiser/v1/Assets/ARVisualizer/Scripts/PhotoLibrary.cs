using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace ARVisualizer
{
    [Serializable]
    public sealed class LibraryAsset
    {
        public string id;
        public string kind;
        public string caption;
        public string path;
    }

    [Serializable]
    public sealed class LibraryResult
    {
        public int request;
        public bool ok;
        public string error;
        public bool limited;
        public int total;
        public LibraryAsset[] assets;
        public string path;
    }

    /// <summary>PhotoKit bridge. Paths stay local and are never sent in Wi-Fi replies.</summary>
    public sealed class PhotoLibrary : MonoBehaviour
    {
        const float RequestTimeoutSeconds = 60;
        const float ExportTimeoutSeconds = 180;

        sealed class Pending
        {
            public Action<LibraryResult> callback;
            public float deadline;
        }

        readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        int nextRequest;
        public string CacheDirectory { get; private set; }

        void Awake()
        {
            gameObject.name = "AR Photo Library " + Guid.NewGuid().ToString("N");
            CacheDirectory = Path.Combine(Application.temporaryCachePath, "ARScreenMedia", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(CacheDirectory);
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void AVPhotos_Request(string receiver, int request, string operation, string asset, int page, string directory);

        [DllImport("__Internal")]
        static extern void AVPhotos_Cancel(string receiver, int request);
#endif

        public void Request(string operation, string asset, int page, Action<LibraryResult> callback)
        {
            int id = ++nextRequest;
            float timeout = operation == "export" ? ExportTimeoutSeconds : RequestTimeoutSeconds;
            pending.Add(id, new Pending
            {
                callback = callback,
                deadline = Time.realtimeSinceStartup + timeout
            });
#if UNITY_IOS && !UNITY_EDITOR
            AVPhotos_Request(gameObject.name, id, operation, asset ?? "", page, CacheDirectory);
#else
            StartCoroutine(EditorReply(id, operation, asset, page));
#endif
        }

        [Preserve]
        public void OnLibraryResult(string json)
        {
            var result = JsonUtility.FromJson<LibraryResult>(json);
            if (result == null) return;
            if (pending.Remove(result.request, out var request)) request.callback(result);
            else DeleteResultFiles(result);
        }

        public void CancelAll()
        {
#if UNITY_IOS && !UNITY_EDITOR
            foreach (int id in pending.Keys) AVPhotos_Cancel(gameObject.name, id);
#endif
            pending.Clear();
        }

        void Update()
        {
            if (pending.Count == 0) return;

            // Collect first: callbacks can add or cancel requests.
            var expired = new List<int>();
            foreach (var entry in pending)
            {
                if (Time.realtimeSinceStartup > entry.Value.deadline) expired.Add(entry.Key);
            }
            foreach (int id in expired)
            {
                if (!pending.Remove(id, out var request)) continue;
#if UNITY_IOS && !UNITY_EDITOR
                AVPhotos_Cancel(gameObject.name, id);
#endif
                request.callback(new LibraryResult
                {
                    request = id,
                    error = "Library request timed out. Check your connection for iCloud items, then retry."
                });
            }
        }

        /// <summary>The caller owns a successfully decoded texture; failed textures are released here.</summary>
        internal static Texture2D ReadImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            var image = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (image.LoadImage(File.ReadAllBytes(path))) return image;
            }
            catch (IOException) { }

            Destroy(image);
            return null;
        }

        public static void DeleteResultFiles(LibraryResult result)
        {
            DeleteCacheFile(result.path);
            if (result.assets == null) return;
            foreach (var asset in result.assets) DeleteCacheFile(asset.path);
        }

        public static void DeleteCacheFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.temporaryCachePath, "ARScreenMedia")) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(path);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full);
            }
            catch (IOException) { }
        }

#if !UNITY_IOS || UNITY_EDITOR
        const int EditorPageSize = 6;

        // Allows Editor tests to exercise denial, errors and empty libraries without device permissions.
        public Func<string, LibraryResult> EditorResultOverride;
        public int EditorSampleCount = 8;

        IEnumerator EditorReply(int request, string operation, string asset, int page)
        {
            yield return null;
            if (!pending.ContainsKey(request)) yield break;

            var overridden = EditorResultOverride?.Invoke(operation);
            if (overridden != null)
            {
                overridden.request = request;
                OnLibraryResult(JsonUtility.ToJson(overridden));
                yield break;
            }

            int total = Mathf.Max(0, EditorSampleCount);
            var result = new LibraryResult { request = request, ok = true, total = total };
            if (operation == "page") result.assets = CreateEditorPage(page, total);
            else if (operation == "export") result.path = SampleImage(int.TryParse(asset, out int index) ? index : 0);
            OnLibraryResult(JsonUtility.ToJson(result));
        }

        LibraryAsset[] CreateEditorPage(int page, int total)
        {
            int start = Mathf.Min(Mathf.Max(0, page) * EditorPageSize, total);
            int count = Mathf.Min(EditorPageSize, total - start);
            var assets = new LibraryAsset[count];
            for (int i = 0; i < count; ++i)
            {
                int index = start + i;
                assets[i] = new LibraryAsset
                {
                    id = index.ToString(),
                    kind = "photo",
                    caption = "Editor sample " + (index + 1),
                    path = SampleImage(index)
                };
            }
            return assets;
        }

        string SampleImage(int index)
        {
            const int width = 240;
            const int height = 160;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    pixels[y * width + x] = Color.HSVToRGB((index * 0.12f + x / 1200f) % 1, 0.65f, 0.4f + y / 270f);
                }
            }
            image.SetPixels(pixels);
            image.Apply();
            string path = Path.Combine(CacheDirectory, Guid.NewGuid().ToString("N") + ".jpg");
            File.WriteAllBytes(path, image.EncodeToJPG());
            Destroy(image);
            return path;
        }
#endif
        void OnDestroy() => CancelAll();
    }
}
