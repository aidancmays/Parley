using System;

namespace Parley
{
    /// <summary>
    /// A small jitter buffer for one incoming voice: decoded samples go in on the main thread as packets arrive,
    /// the audio thread reads them out. It waits until <see cref="StartSamples"/> are queued before it plays (so
    /// uneven packet timing doesn't stutter), fades in and out at the edges of speech, and drops the oldest audio
    /// when more than <see cref="MaxSamples"/> pile up so the delay never grows.
    /// </summary>
    public sealed class VoiceBuffer
    {
        readonly float[] ring;
        readonly object gate = new();
        int read, count;
        bool playing;
        float fade;

        public VoiceBuffer(int sampleRate, float startSeconds = 0.08f, float maxSeconds = 0.4f)
        {
            SampleRate = sampleRate;
            StartSamples = Math.Max(1, (int)(sampleRate * startSeconds));
            MaxSamples = Math.Max(StartSamples * 2, (int)(sampleRate * maxSeconds));
            ring = new float[MaxSamples + sampleRate / 10];
            FadeStep = 1f / Math.Max(1, sampleRate / 200); // 5 ms edges
        }

        public int SampleRate { get; }
        public int StartSamples { get; }
        public int MaxSamples { get; }
        float FadeStep { get; }

        /// <summary>Samples queued and not yet played.</summary>
        public int Count
        {
            get { lock (gate) return count; }
        }

        /// <summary>True while it is playing out speech (between the start threshold and running dry).</summary>
        public bool Playing
        {
            get { lock (gate) return playing; }
        }

        /// <summary>How many samples were thrown away to keep the delay down.</summary>
        public int Dropped { get; private set; }

        public void Write(float[] samples, int offset, int length)
        {
            lock (gate)
            {
                for (int i = 0; i < length; i++)
                {
                    int write = (read + count) % ring.Length;
                    ring[write] = samples[offset + i];
                    if (count < ring.Length)
                        count++;
                    else
                        read = (read + 1) % ring.Length;
                }
                if (count > MaxSamples)
                {
                    int drop = count - StartSamples;
                    read = (read + drop) % ring.Length;
                    count -= drop;
                    Dropped += drop;
                }
            }
        }

        /// <summary>Reads mono samples; fills with silence when there's nothing to play. Audio thread.</summary>
        public void Read(float[] output, int offset, int length)
        {
            lock (gate)
            {
                if (!playing && count >= StartSamples)
                    playing = true;
                for (int i = 0; i < length; i++)
                {
                    if (playing && count > 0)
                    {
                        fade = Math.Min(1f, fade + FadeStep);
                        output[offset + i] = ring[read] * fade;
                        read = (read + 1) % ring.Length;
                        count--;
                        if (count == 0)
                            playing = false;
                    }
                    else
                    {
                        fade = 0f;
                        output[offset + i] = 0f;
                    }
                }
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                read = count = 0;
                playing = false;
                fade = 0f;
            }
        }

        /// <summary>Converts little-endian 16-bit PCM bytes to floats in [-1, 1]. Returns the sample count.</summary>
        public static int Pcm16ToFloat(byte[] pcm, int bytes, float[] output, float gain = 1f)
        {
            int samples = Math.Min(bytes / 2, output.Length);
            const float scale = 1f / 32768f;
            for (int i = 0; i < samples; i++)
            {
                short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                float v = s * scale * gain;
                output[i] = v > 1f ? 1f : v < -1f ? -1f : v;
            }
            return samples;
        }
    }
}
