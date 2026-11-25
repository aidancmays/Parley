using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Parley
{
    /// <summary>
    /// Gives a networked player a voice. On the local player it records from Steam Voice and sends it to the host;
    /// on everyone else's machine it plays what arrives from a <see cref="VoicePlayback"/> at the player's mouth.
    /// Put it on the player prefab.
    /// </summary>
    /// <remarks>
    /// If <see cref="output"/> is empty a "Voice" child is made under <see cref="mouth"/> at spawn. To give voices
    /// the game's own sound treatment (occlusion, reverb), make that child in the prefab yourself, with the
    /// AudioSource, then VoicePlayback, then any effects below it, and assign it here.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class VoiceSpeaker : NetworkBehaviour
    {
        [Tooltip("Where the voice comes from (the head). Empty = this object.")]
        [SerializeField] Transform mouth;
        [Tooltip("Plays this player's voice. Empty = made at spawn under the mouth.")]
        [SerializeField] VoicePlayback output;
        [Tooltip("The host only forwards this voice to players within this many metres. Keep it a little past the playback's silent distance.")]
        [SerializeField, Min(1f)] float hearingRange = 40f;

        static readonly Dictionary<ulong, VoiceSpeaker> byClient = new();

        byte[] captured = new byte[4096];
        float[] decoded = new float[4096];
        float lastSent = -10f;
        float localVolume = 1f;
        bool muted;

        /// <summary>The speaker owned by a client, or null.</summary>
        public static VoiceSpeaker ForClient(ulong clientId) => byClient.TryGetValue(clientId, out var s) ? s : null;

        public static IReadOnlyCollection<VoiceSpeaker> All => byClient.Values;

        /// <summary>The local player's speaker, or null.</summary>
        public static VoiceSpeaker Local { get; private set; }

        /// <summary>Raised on the local machine with each compressed packet the local player sends (for echo tests).</summary>
        public static event Action<byte[], int> LocalVoiceSent;

        public Transform Mouth => mouth != null ? mouth : transform;
        public VoicePlayback Output => output;
        public float HearingRange { get => hearingRange; set => hearingRange = Mathf.Max(1f, value); }

        /// <summary>True while this player is talking (sending, for the local player; being heard, for others).</summary>
        public bool IsSpeaking => IsOwner ? Time.unscaledTime - lastSent < 0.25f : output != null && output.Active;

        /// <summary>How loud they are right now, 0..1. Zero for the local player unless loopback is on.</summary>
        public float Level => output != null ? output.Level : 0f;

        /// <summary>Mute this player for the local listener only.</summary>
        public bool Muted
        {
            get => muted;
            set
            {
                muted = value;
                ApplyVolume();
            }
        }

        /// <summary>This player's loudness for the local listener only, 0..2 (times the global voice volume).</summary>
        public float LocalVolume
        {
            get => localVolume;
            set
            {
                localVolume = Mathf.Clamp(value, 0f, 2f);
                ApplyVolume();
            }
        }

        public override void OnNetworkSpawn()
        {
            byClient[OwnerClientId] = this;
            if (IsOwner)
                Local = this;
            EnsureOutput();
            ApplyVolume();
            VoiceRelay.Attach(NetworkManager);
        }

        public override void OnNetworkDespawn()
        {
            if (byClient.TryGetValue(OwnerClientId, out var s) && s == this)
                byClient.Remove(OwnerClientId);
            if (Local == this)
            {
                Local = null;
                SteamVoice.SetRecording(false);
            }
            if (output != null)
                output.Clear();
        }

        void EnsureOutput()
        {
            if (output != null)
                return;
            var go = new GameObject("Voice");
            go.transform.SetParent(Mouth, false);
            go.AddComponent<AudioSource>();
            output = go.AddComponent<VoicePlayback>();
        }

        void ApplyVolume()
        {
            if (output == null)
                return;
            bool self = IsSpawned && IsOwner;
            float v = muted || (self && !ParleySettings.Loopback) ? 0f : localVolume * (self ? 1f : ParleySettings.Volume);
            output.Volume = v;
        }

        void Update()
        {
            if (!IsSpawned)
                return;
            if (IsOwner)
            {
                ApplyVolume();
                Capture();
            }
            else if (output != null && !muted)
            {
                output.Volume = localVolume * ParleySettings.Volume;
            }
        }

        void Capture()
        {
            SteamVoice.SetRecording(ParleySettings.Transmitting);
            // Keep reading after recording stops: Steam hands over the tail end of the last words.
            int length;
            int guard = 0;
            while ((length = SteamVoice.ReadCompressed(ref captured)) > 0 && guard++ < 8)
            {
                lastSent = Time.unscaledTime;
                VoiceRelay.SendLocal(this, captured, length);
                LocalVoiceSent?.Invoke(captured, length);
                if (ParleySettings.Loopback)
                    Receive(captured, length);
            }
        }

        /// <summary>Decodes a packet of this player's voice and queues it for playback on this machine.</summary>
        internal void Receive(byte[] compressed, int length)
        {
            if (output == null)
                return;
            int count = SteamVoice.Decode(compressed, length, ref decoded);
            output.Write(decoded, count);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            byClient.Clear();
            Local = null;
            LocalVoiceSent = null;
        }
    }
}
