using UnityEngine;
using UnityEngine.InputSystem;

namespace Parley
{
    public enum VoiceMode
    {
        /// <summary>Always transmitting; Steam's voice detection only sends while you talk.</summary>
        OpenMic,
        /// <summary>Transmits while the push-to-talk action is held.</summary>
        PushToTalk,
        /// <summary>Mic off.</summary>
        Off,
    }

    /// <summary>
    /// The local player's voice settings, saved in PlayerPrefs. Games build their own settings UI on these.
    /// </summary>
    public static class ParleySettings
    {
        const string ModeKey = "Parley.Mode";
        const string VolumeKey = "Parley.Volume";

        static VoiceMode? mode;
        static float? volume;
        static InputAction pushToTalk;
        static InputAction defaultPushToTalk;

        public static VoiceMode Mode
        {
            get => mode ??= (VoiceMode)PlayerPrefs.GetInt(ModeKey, (int)VoiceMode.OpenMic);
            set
            {
                mode = value;
                PlayerPrefs.SetInt(ModeKey, (int)value);
            }
        }

        /// <summary>Loudness of everyone else's voices, 0..2.</summary>
        public static float Volume
        {
            get => volume ??= PlayerPrefs.GetFloat(VolumeKey, 1f);
            set
            {
                volume = Mathf.Clamp(value, 0f, 2f);
                PlayerPrefs.SetFloat(VolumeKey, volume.Value);
            }
        }

        /// <summary>
        /// The action held to talk in push-to-talk mode. Defaults to V on the keyboard; set it to an action from
        /// the game's own input asset to rebind it there.
        /// </summary>
        public static InputAction PushToTalkAction
        {
            get
            {
                if (pushToTalk != null)
                    return pushToTalk;
                if (defaultPushToTalk == null)
                {
                    defaultPushToTalk = new InputAction("Push To Talk", InputActionType.Button, "<Keyboard>/v");
                    defaultPushToTalk.Enable();
                }
                return defaultPushToTalk;
            }
            set
            {
                pushToTalk = value;
                pushToTalk?.Enable();
            }
        }

        /// <summary>Extra rule from the game: false stops the local player transmitting (dead, menu open...).</summary>
        public static System.Func<bool> CanTransmit { get; set; }

        /// <summary>Whether the local player is sending voice right now (given mode, push-to-talk and the game's rule).</summary>
        public static bool Transmitting
        {
            get
            {
                if (CanTransmit != null && !CanTransmit())
                    return false;
                return Mode switch
                {
                    VoiceMode.OpenMic => true,
                    VoiceMode.PushToTalk => PushToTalkAction.IsPressed(),
                    _ => false,
                };
            }
        }

        /// <summary>Developer option: hear your own voice back from your own mouth.</summary>
        public static bool Loopback { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            mode = null;
            volume = null;
            pushToTalk = null;
            defaultPushToTalk?.Dispose();
            defaultPushToTalk = null;
            CanTransmit = null;
            Loopback = false;
        }
    }
}
