using System;
using SteamNet;
using Steamworks;
using UnityEngine;

namespace Parley
{
    /// <summary>
    /// The Steam Voice calls Parley uses: record from the player's mic (with their Steam voice settings: device,
    /// voice detection, noise suppression), read compressed packets, and decode packets to PCM. Main thread only.
    /// </summary>
    public static class SteamVoice
    {
        static byte[] pcm = new byte[22050];
        static bool recording;

        public static bool Available => SteamPlatform.IsReady;

        /// <summary>Rate voice is decoded at: the output rate, within what Steam can produce.</summary>
        public static int SampleRate => Mathf.Clamp(AudioSettings.outputSampleRate, 11025, 48000);

        public static bool Recording => recording;

        public static void SetRecording(bool on)
        {
            if (!Available)
            {
                recording = false;
                return;
            }
            if (on == recording)
                return;
            recording = on;
            if (on)
                SteamUser.StartVoiceRecording();
            else
                SteamUser.StopVoiceRecording();
            SteamFriends.SetInGameVoiceSpeaking(SteamUser.GetSteamID(), on);
        }

        /// <summary>
        /// Reads whatever compressed voice Steam has ready into <paramref name="buffer"/> (growing it if needed).
        /// Returns the byte count, 0 when there's nothing (silence, or not recording).
        /// </summary>
        public static int ReadCompressed(ref byte[] buffer)
        {
            if (!Available)
                return 0;
            var result = SteamUser.GetAvailableVoice(out uint available);
            if (result != EVoiceResult.k_EVoiceResultOK || available == 0)
                return 0;
            if (buffer == null || buffer.Length < available)
                buffer = new byte[Math.Max(1024, (int)available * 2)];
            result = SteamUser.GetVoice(true, buffer, (uint)buffer.Length, out uint written);
            return result == EVoiceResult.k_EVoiceResultOK ? (int)written : 0;
        }

        /// <summary>
        /// Decodes one compressed packet to mono float samples at <see cref="SampleRate"/> into
        /// <paramref name="samples"/> (growing it if needed). Returns the sample count.
        /// </summary>
        public static int Decode(byte[] compressed, int length, ref float[] samples, float gain = 1f)
        {
            if (!Available || length <= 0)
                return 0;
            uint rate = (uint)SampleRate;
            var result = SteamUser.DecompressVoice(compressed, (uint)length, pcm, (uint)pcm.Length, out uint written, rate);
            if (result == EVoiceResult.k_EVoiceResultBufferTooSmall)
            {
                pcm = new byte[Math.Max(pcm.Length * 2, (int)written)];
                result = SteamUser.DecompressVoice(compressed, (uint)length, pcm, (uint)pcm.Length, out written, rate);
            }
            if (result != EVoiceResult.k_EVoiceResultOK || written == 0)
                return 0;
            int count = (int)written / 2;
            if (samples == null || samples.Length < count)
                samples = new float[count];
            return VoiceBuffer.Pcm16ToFloat(pcm, (int)written, samples, gain);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => recording = false;
    }
}
