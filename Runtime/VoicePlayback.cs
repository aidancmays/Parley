using UnityEngine;

namespace Parley
{
    /// <summary>
    /// Plays one voice through this object's <see cref="AudioSource"/>, so it's a normal 3D sound: distance
    /// falloff, panning, and any filters or occlusion the game puts on the source. Decoded audio is fed in with
    /// <see cref="Write"/>; the audio thread plays it out of a <see cref="VoiceBuffer"/>.
    /// </summary>
    /// <remarks>
    /// The source loops a clip of constant 1s and this multiplies the voice in, which works whichever way Unity
    /// orders spatialisation and filters. Effects that should act on the voice (low-pass, occlusion gain) must be
    /// below this component on the object.
    /// </remarks>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public sealed class VoicePlayback : MonoBehaviour
    {
        [Tooltip("Set up the AudioSource for voice on Awake (3D, no doppler, voice falloff). Off = keep the source's own settings.")]
        [SerializeField] bool configureSource = true;
        [Tooltip("Full volume within this distance, metres.")]
        [SerializeField, Min(0.1f)] float fullVolumeDistance = 1.5f;
        [Tooltip("Silent beyond this distance, metres.")]
        [SerializeField, Min(1f)] float silentDistance = 32f;

        static AudioClip ones;

        AudioSource source;
        VoiceBuffer buffer;
        int outputRate;
        float[] mono = new float[0];
        double phase;
        volatile float volume = 1f;
        volatile float level;
        float lastWrite = -10f;

        /// <summary>Loudness of this voice for the local listener (mute = 0), 0..2.</summary>
        public float Volume { get => volume; set => volume = Mathf.Clamp(value, 0f, 2f); }

        /// <summary>Recent loudness of what's playing, 0..1 (for speaking indicators and mouth flaps).</summary>
        public float Level => level;

        /// <summary>True while audio arrived recently or is still playing out.</summary>
        public bool Active => Time.unscaledTime - lastWrite < 0.3f || (buffer != null && buffer.Playing);

        public VoiceBuffer Buffer => buffer;

        public float SilentDistance => silentDistance;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            outputRate = AudioSettings.outputSampleRate;
            buffer = new VoiceBuffer(SteamVoice.SampleRate);
            if (configureSource)
                Configure(source, fullVolumeDistance, silentDistance);
            source.clip = Ones();
            source.loop = true;
            source.playOnAwake = false;
        }

        void OnEnable()
        {
            if (source != null && !source.isPlaying)
                source.Play();
        }

        void OnDisable() => buffer?.Clear();

        /// <summary>Queues decoded mono samples (at <see cref="SteamVoice.SampleRate"/>).</summary>
        public void Write(float[] samples, int count)
        {
            if (count <= 0)
                return;
            buffer.Write(samples, 0, count);
            lastWrite = Time.unscaledTime;
            if (!source.isPlaying && isActiveAndEnabled)
                source.Play();
        }

        public void Clear() => buffer.Clear();

        void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            float ratio = (float)buffer.SampleRate / outputRate;
            int needed = Mathf.Approximately(ratio, 1f) ? frames : Mathf.CeilToInt((float)(frames * ratio + phase)) + 1;
            if (mono.Length < needed)
                mono = new float[needed * 2];

            float gain = volume;
            float peak = 0f;
            if (Mathf.Approximately(ratio, 1f))
            {
                buffer.Read(mono, 0, frames);
                for (int f = 0; f < frames; f++)
                {
                    float v = mono[f] * gain;
                    peak = Mathf.Max(peak, Mathf.Abs(v));
                    for (int c = 0; c < channels; c++)
                        data[f * channels + c] *= v;
                }
            }
            else
            {
                // Rare: the output runs faster than Steam can decode (e.g. 96 kHz). Nearest-sample resampling.
                buffer.Read(mono, 0, needed - 1);
                for (int f = 0; f < frames; f++)
                {
                    int i = Mathf.Min(needed - 2, (int)(phase + f * ratio));
                    float v = mono[i] * gain;
                    peak = Mathf.Max(peak, Mathf.Abs(v));
                    for (int c = 0; c < channels; c++)
                        data[f * channels + c] *= v;
                }
                phase = (phase + frames * ratio) - (needed - 1);
                if (phase < 0) phase = 0;
            }
            level = Mathf.Max(peak, level * 0.85f);
        }

        /// <summary>Sets up a source the way voice wants it: fully 3D, no doppler, loud close and silent at a distance.</summary>
        public static void Configure(AudioSource source, float fullVolumeDistance, float silentDistance)
        {
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.minDistance = fullVolumeDistance;
            source.maxDistance = silentDistance;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Falloff(fullVolumeDistance, silentDistance));
        }

        /// <summary>
        /// Natural inverse-distance falloff from <paramref name="full"/> metres, eased to silence at
        /// <paramref name="silent"/> metres. The curve's x is distance divided by <paramref name="silent"/>.
        /// </summary>
        public static AnimationCurve Falloff(float full, float silent)
        {
            const int keys = 16;
            var curve = new AnimationCurve();
            for (int k = 0; k <= keys; k++)
            {
                float x = (float)k / keys;
                float d = x * silent;
                float g = d <= full ? 1f : full / d;
                float edge = Mathf.InverseLerp(0.65f, 1f, x);
                g *= 1f - edge * edge * (3f - 2f * edge);
                curve.AddKey(new Keyframe(x, g));
            }
            for (int k = 0; k < curve.length; k++)
                curve.SmoothTangents(k, 0f);
            return curve;
        }

        static AudioClip Ones()
        {
            if (ones != null)
                return ones;
            int rate = AudioSettings.outputSampleRate;
            var data = new float[rate];
            for (int i = 0; i < data.Length; i++)
                data[i] = 1f;
            ones = AudioClip.Create("Parley Carrier", data.Length, 1, rate, false);
            ones.SetData(data, 0);
            return ones;
        }
    }
}
