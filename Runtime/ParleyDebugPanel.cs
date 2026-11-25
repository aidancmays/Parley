using UnityEngine;
using UnityEngine.InputSystem;

namespace Parley
{
    /// <summary>
    /// Developer overlay (toggle with <see cref="toggleKey"/>): mic mode, who's speaking, bytes sent, and an echo
    /// test for trying voice alone: <see cref="echoKey"/> leaves an echo where you stand that plays your own voice
    /// back, so you can walk off and hear yourself from there (through walls and doorways, if the game's sounds do that).
    /// </summary>
    public sealed class ParleyDebugPanel : MonoBehaviour
    {
        [SerializeField] Key toggleKey = Key.F5;
        [SerializeField] Key echoKey = Key.F4;
        [Tooltip("Optional: a prefab with an AudioSource, VoicePlayback and any effects, used for the echo. Empty = a plain one.")]
        [SerializeField] VoicePlayback echoPrefab;
        [SerializeField] bool visible;

        VoicePlayback echo;
        bool voiceReverb = true;

        // Turns off both the shared listener reverb and any reverb filter on the voice's own chain.
        static void SetReverb(GameObject go, bool on)
        {
            if (go.TryGetComponent<AudioSource>(out var source))
                source.reverbZoneMix = on ? 1f : 0f;
            if (go.TryGetComponent<AudioReverbFilter>(out var filter))
                filter.enabled = on;
        }
        float[] decoded = new float[4096];

        void OnEnable() => VoiceSpeaker.LocalVoiceSent += OnLocalVoice;
        void OnDisable() => VoiceSpeaker.LocalVoiceSent -= OnLocalVoice;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard[toggleKey].wasPressedThisFrame)
                visible = !visible;
            if (visible && keyboard[echoKey].wasPressedThisFrame)
                ToggleEcho();
        }

        void ToggleEcho()
        {
            if (echo != null)
            {
                Destroy(echo.gameObject);
                echo = null;
                return;
            }
            var local = VoiceSpeaker.Local;
            var at = local != null ? local.Mouth.position : Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            if (echoPrefab != null)
            {
                echo = Instantiate(echoPrefab, at, Quaternion.identity);
            }
            else
            {
                var go = new GameObject("Parley Echo");
                go.transform.position = at;
                go.AddComponent<AudioSource>();
                echo = go.AddComponent<VoicePlayback>();
            }
            echo.name = "Parley Echo";
        }

        void OnLocalVoice(byte[] data, int length)
        {
            if (echo == null)
                return;
            int count = SteamVoice.Decode(data, length, ref decoded);
            echo.Write(decoded, count);
        }

        void OnGUI()
        {
            if (!visible)
                return;
            GUILayout.BeginArea(new Rect(Screen.width - 330, 10, 320, 400), GUI.skin.box);
            GUILayout.Label("Parley");
            GUILayout.Label(SteamVoice.Available ? $"Steam voice {SteamVoice.SampleRate} Hz" : "Steam not running");
            GUILayout.Label($"Mode {ParleySettings.Mode}  Recording {SteamVoice.Recording}  Sending {ParleySettings.Transmitting}");
            if (GUILayout.Button($"Mode: {ParleySettings.Mode}"))
                ParleySettings.Mode = (VoiceMode)(((int)ParleySettings.Mode + 1) % 3);
            ParleySettings.Loopback = GUILayout.Toggle(ParleySettings.Loopback, "Loopback");
            bool reverbOn = GUILayout.Toggle(voiceReverb, "Voice reverb");
            if (reverbOn != voiceReverb)
            {
                voiceReverb = reverbOn;
                foreach (var s in VoiceSpeaker.All)
                    if (s != null && s.Output != null)
                        SetReverb(s.Output.gameObject, reverbOn);
                if (echo != null)
                    SetReverb(echo.gameObject, reverbOn);
            }
            GUILayout.Label($"{echoKey}: {(echo == null ? "leave echo here" : "remove echo")}");
            GUILayout.Label($"Sent {VoiceRelay.BytesSent / 1024f:0.0} KB  Relayed {VoiceRelay.BytesRelayed / 1024f:0.0} KB");
            foreach (var s in VoiceSpeaker.All)
            {
                if (s == null)
                    continue;
                string buffered = s.Output != null ? $" buf {s.Output.Buffer.Count * 1000 / s.Output.Buffer.SampleRate} ms" : "";
                GUILayout.Label($"Client {s.OwnerClientId}{(s.IsOwner ? " (you)" : "")}: {(s.IsSpeaking ? "speaking" : "-")} {s.Level:0.00}{buffered}");
            }
            GUILayout.EndArea();
        }
    }
}
