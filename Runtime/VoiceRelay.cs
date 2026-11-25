using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Parley
{
    /// <summary>
    /// Carries voice over Netcode named messages. Clients send their packets to the host; the host forwards each
    /// one only to players within the speaker's <see cref="VoiceSpeaker.HearingRange"/> (and that the game's
    /// <see cref="CanHear"/> rule allows), and plays it itself when it can hear it. Voice is sent unreliable and
    /// sequenced: a lost packet is a tiny gap, never a delay.
    /// </summary>
    public static class VoiceRelay
    {
        public const string MessageName = "Parley.Voice";
        const int MaxPacket = 8192;

        static CustomMessagingManager attached;
        static NetworkManager network;
        static readonly List<ulong> targets = new();
        static byte[] scratch = new byte[MaxPacket];

        /// <summary>
        /// Host-side rule: may <c>listener</c> hear <c>speaker</c>? (listener is null when the receiving client has
        /// no speaker, e.g. spectating.) Null = everyone in range hears everyone. Use it for things like the dead
        /// only hearing the dead.
        /// </summary>
        public static Func<VoiceSpeaker, VoiceSpeaker, bool> CanHear { get; set; }

        /// <summary>Voice bytes relayed by the host since start (for debug readouts).</summary>
        public static long BytesRelayed { get; private set; }

        /// <summary>Voice bytes this machine sent since start.</summary>
        public static long BytesSent { get; private set; }

        internal static void Attach(NetworkManager manager)
        {
            if (manager == null || manager.CustomMessagingManager == null)
                return;
            if (attached == manager.CustomMessagingManager)
                return;
            Detach();
            network = manager;
            attached = manager.CustomMessagingManager;
            attached.RegisterNamedMessageHandler(MessageName, OnMessage);
            manager.OnClientStopped += OnStopped;
            manager.OnServerStopped += OnStopped;
        }

        static void OnStopped(bool _) => Detach();

        static void Detach()
        {
            if (network != null)
            {
                network.OnClientStopped -= OnStopped;
                network.OnServerStopped -= OnStopped;
            }
            try
            {
                attached?.UnregisterNamedMessageHandler(MessageName);
            }
            catch (Exception)
            {
                // The messaging manager may already be torn down with the session.
            }
            attached = null;
            network = null;
        }

        /// <summary>Sends a packet from the local player: to the host, or straight to listeners when we are the host.</summary>
        internal static void SendLocal(VoiceSpeaker speaker, byte[] data, int length)
        {
            if (network == null || !network.IsListening || length <= 0 || length > MaxPacket)
                return;
            BytesSent += length;
            if (network.IsServer)
            {
                Relay(speaker, data, length);
                return;
            }
            using var writer = new FastBufferWriter(length + 8, Allocator.Temp);
            writer.WriteValueSafe((ushort)length);
            writer.WriteBytesSafe(data, length);
            attached.SendNamedMessage(MessageName, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
        }

        static void OnMessage(ulong sender, FastBufferReader reader)
        {
            if (network == null)
                return;
            if (network.IsServer)
            {
                // From a client: [length][bytes]. The sender is the speaker.
                if (!reader.TryBeginRead(sizeof(ushort)))
                    return;
                reader.ReadValue(out ushort length);
                if (length == 0 || length > MaxPacket || !reader.TryBeginRead(length))
                    return;
                reader.ReadBytes(ref scratch, length);
                var speaker = VoiceSpeaker.ForClient(sender);
                if (speaker != null)
                    Relay(speaker, scratch, length);
            }
            else
            {
                // From the host: [speaker client id][length][bytes].
                if (!reader.TryBeginRead(sizeof(ulong) + sizeof(ushort)))
                    return;
                reader.ReadValue(out ulong speakerId);
                reader.ReadValue(out ushort length);
                if (length == 0 || length > MaxPacket || !reader.TryBeginRead(length))
                    return;
                reader.ReadBytes(ref scratch, length);
                var speaker = VoiceSpeaker.ForClient(speakerId);
                if (speaker != null && !speaker.IsOwner)
                    speaker.Receive(scratch, length);
            }
        }

        // Host only.
        static void Relay(VoiceSpeaker speaker, byte[] data, int length)
        {
            targets.Clear();
            var from = speaker.Mouth.position;
            float range = speaker.HearingRange;
            foreach (var clientId in network.ConnectedClientsIds)
            {
                if (clientId == speaker.OwnerClientId)
                    continue;
                var listener = VoiceSpeaker.ForClient(clientId);
                if (listener != null && (listener.Mouth.position - from).sqrMagnitude > range * range)
                    continue;
                if (CanHear != null && !CanHear(speaker, listener))
                    continue;
                if (clientId == network.LocalClientId)
                    speaker.Receive(data, length);
                else
                    targets.Add(clientId);
            }
            if (targets.Count == 0)
                return;
            BytesRelayed += (long)length * targets.Count;
            using var writer = new FastBufferWriter(length + 16, Allocator.Temp);
            writer.WriteValueSafe(speaker.OwnerClientId);
            writer.WriteValueSafe((ushort)length);
            writer.WriteBytesSafe(data, length);
            attached.SendNamedMessage(MessageName, targets, writer, NetworkDelivery.UnreliableSequenced);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            attached = null;
            network = null;
            CanHear = null;
            BytesRelayed = 0;
            BytesSent = 0;
        }
    }
}
