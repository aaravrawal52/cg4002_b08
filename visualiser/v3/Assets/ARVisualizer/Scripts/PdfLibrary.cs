using System;
using System.IO;
using UnityEngine;

namespace ARVisualizer
{
    /// <summary>A filename snapshot of the user-owned PDFs folder. Never exports or deletes originals.</summary>
    public sealed class PdfLibrary
    {
        public static string FolderPath => Path.Combine(Application.persistentDataPath, "PDFs");
        public static string FilesLocation => "Files > On My iPhone > " + Application.productName + " > PDFs";
        readonly string[] files = Array.Empty<string>();
        readonly string error;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CreateFolderAtLaunch()
        {
            try { Directory.CreateDirectory(FolderPath); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { Debug.LogWarning("Could not create PDFs folder: " + exception.Message); }
        }

        public PdfLibrary(string directory = null)
        {
            try
            {
                string folder = directory ?? FolderPath;
                Directory.CreateDirectory(folder);
                files = Array.FindAll(Directory.GetFiles(folder), IsPdf);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            { error = "Cannot read the PDFs folder. Check Files and refresh."; }
        }

        static bool IsPdf(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

        public void RequestPage(int page, Action<LibraryResult> reply)
        {
            const int pageSize = MediaLibraryGrid.BatchSize;
            int start = Mathf.Clamp(page, 0, files.Length / pageSize + 1) * pageSize;
            var assets = new LibraryAsset[Mathf.Clamp(files.Length - start, 0, pageSize)];
            for (int i = 0; i < assets.Length; ++i)
                assets[i] = new LibraryAsset { id = files[start + i], kind = "pdf", caption = Path.GetFileName(files[start + i]) };
            // path is reserved for disposable thumbnail files; the original lives only in id.
            reply(new LibraryResult { ok = error == null, error = error, total = files.Length, assets = assets });
        }

        public static bool TryGetFileUrl(string path, out string url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(path) || !IsPdf(path) || !File.Exists(path)) return false;
            url = new Uri(Path.GetFullPath(path)).AbsoluteUri;
            return true;
        }

        // A small document icon keeps filename tiles consistent with the photo grid without parsing PDFs twice.
        public static Texture2D CreateIcon()
        {
            var icon = new Texture2D(64, 80, TextureFormat.RGBA32, false) { name = "PDF document", filterMode = FilterMode.Bilinear };
            var pixels = new Color32[64 * 80];
            for (int y = 0; y < 80; ++y)
                for (int x = 0; x < 64; ++x)
                {
                    bool paper = x >= 8 && x < 56 && y >= 4 && y < 76;
                    bool line = x >= 16 && x < 48 && y >= 20 && y < 60 && y % 10 < 3;
                    pixels[y * 64 + x] = !paper ? new Color32(0, 0, 0, 0)
                        : line ? new Color32(48, 108, 110, 255) : new Color32(223, 233, 236, 255);
                }
            icon.SetPixels32(pixels); icon.Apply(false, true);
            return icon;
        }
    }
}
