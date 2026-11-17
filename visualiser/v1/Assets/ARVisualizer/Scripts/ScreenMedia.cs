using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace ARVisualizer
{
    /// <summary>Owns one screen's local photo/video and releases it when cleared or removed.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ScreenSurface))]
    public sealed class ScreenMedia : MonoBehaviour
    {
        [SerializeField] MeshRenderer mediaFace;
        [SerializeField] bool loopVideo = true;
        [SerializeField, Range(0, 1)] float volume = 0.7f;

        public bool IsBlank => string.IsNullOrEmpty(AppId);
        public string AppId { get; private set; } = "";
        public bool IsVideo => player != null && !string.IsNullOrEmpty(player.url);
        public bool IsPlaying => player != null && player.isPlaying;
        public bool IsLoading { get; private set; }
        public string Error { get; private set; } = "";

        const float PrepareTimeoutSeconds = 30;
        const int MaximumVideoDimension = 1920;

        Material material;
        ScreenSurface surface;
        Texture2D photo;
        RenderTexture videoTexture;
        VideoPlayer player;
        string ownedPath;
        float prepareDeadline;

        void Awake()
        {
            surface = GetComponent<ScreenSurface>();
            if (mediaFace != null)
            {
                material = new Material(mediaFace.sharedMaterial);
                mediaFace.sharedMaterial = material;
                material.SetTexture("_MainTex", Texture2D.blackTexture);
            }
        }

        void LateUpdate()
        {
            if (IsLoading && Time.realtimeSinceStartup > prepareDeadline)
                Fail("Video preparation timed out. Choose another item.");
        }

        public bool ShowPhoto(Texture2D image, string cachePath = null)
        {
            if (image == null || material == null) return false;

            Clear();
            photo = image;
            ownedPath = cachePath;
            AppId = "photos";
            Display(image, (float)image.width / image.height);
            return true;
        }

        public bool ShowVideo(string path)
        {
            if (material == null || !File.Exists(path)) return false;

            Clear();
            ownedPath = path;
            AppId = "photos";
            IsLoading = true;
            EnsureVideoPlayer();
            player.isLooping = loopVideo;
            player.url = path;
            prepareDeadline = Time.realtimeSinceStartup + PrepareTimeoutSeconds;
            player.Prepare();
            return true;
        }

        void EnsureVideoPlayer()
        {
            if (player != null) return;

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.renderMode = VideoRenderMode.RenderTexture;
            // The surface supplies the display aspect (including non-square video pixels).
            player.aspectRatio = VideoAspectRatio.Stretch;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            var audio = gameObject.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1;
            audio.volume = volume;
            player.controlledAudioTrackCount = 1;
            player.SetTargetAudioSource(0, audio);
            player.prepareCompleted += Prepared;
            player.errorReceived += VideoError;
        }

        void Prepared(VideoPlayer video)
        {
            if (!IsLoading || !isActiveAndEnabled) return;

            int width = Mathf.Max(1, (int)video.width);
            int height = Mathf.Max(1, (int)video.height);
            float scale = Mathf.Min(1, (float)MaximumVideoDimension / Mathf.Max(width, height));
            int textureWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            int textureHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            videoTexture = new RenderTexture(textureWidth, textureHeight, 0);
            videoTexture.Create();
            video.targetTexture = videoTexture;

            float pixelAspect = video.pixelAspectRatioNumerator > 0 && video.pixelAspectRatioDenominator > 0
                ? (float)video.pixelAspectRatioNumerator / video.pixelAspectRatioDenominator : 1;
            Display(videoTexture, (float)width / height * pixelAspect);
            IsLoading = false;
            video.Play();
        }

        void Display(Texture texture, float aspect)
        {
            surface.SetMediaAspectRatio(aspect);
            material.SetTexture("_MainTex", texture);
            mediaFace.gameObject.SetActive(true);
        }

        void VideoError(VideoPlayer video, string message) => Fail("This video could not be played. Try another item.");

        void Fail(string message)
        {
            Clear();
            Error = message;
        }

        public void TogglePlayback()
        {
            if (player == null || IsLoading) return;
            if (player.isPlaying) player.Pause();
            else player.Play();
        }

        public void Clear()
        {
            IsLoading = false;
            AppId = "";
            Error = "";
            if (player != null)
            {
                player.Stop();
                player.url = "";
                player.targetTexture = null;
            }
            if (material != null) material.SetTexture("_MainTex", Texture2D.blackTexture);
            if (surface != null) surface.SetMediaAspectRatio(ScreenSurface.DefaultAspectRatio);

            ReleaseOwnedMedia();
        }

        void ReleaseOwnedMedia()
        {
            if (photo != null) Destroy(photo);
            photo = null;

            if (videoTexture != null)
            {
                videoTexture.Release();
                Destroy(videoTexture);
                videoTexture = null;
            }

            PhotoLibrary.DeleteCacheFile(ownedPath);
            ownedPath = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && player != null) player.Pause();
        }

        void OnDisable()
        {
            if (player != null) player.Pause();
        }

        void OnDestroy()
        {
            Clear();
            if (material != null) Destroy(material);
        }
    }
}
