# Parley

Proximity voice chat for Netcode for GameObjects over Steam Voice.

- The local player's `VoiceSpeaker` records with Steam Voice (the player's own Steam mic settings, voice detection
  and noise suppression) and sends compressed packets to the host as unreliable sequenced named messages.
- The host forwards each packet only to players within the speaker's **Hearing Range**, and only where
  `VoiceRelay.CanHear` allows (for game rules such as the dead hearing only the dead).
- Each voice plays from a `VoicePlayback` (an `AudioSource` at the speaker's mouth) through a small jitter buffer,
  so it's an ordinary 3D sound: distance falloff to silence, panning, and whatever filters or occlusion you put
  after it on the object.
- `ParleySettings`: open mic, push to talk (V by default, or your own `InputAction`), off; voice volume; a
  `CanTransmit` rule. Per-player `Muted` and `LocalVolume`. `IsSpeaking` and `Level` for HUDs.

## Setup
Add `VoiceSpeaker` to the player prefab and point **Mouth** at the head. Optionally make a "Voice" child under the
head with an `AudioSource`, then `VoicePlayback`, then your effects, and assign it as **Output**; otherwise one is
made at spawn. Steam must be running (SteamNet's `SteamPlatform`).

`ParleyDebugPanel`: F5 shows mode, who's speaking and traffic; F4 (while shown) leaves an echo where you stand that
plays your own voice back, so you can test alone by walking away and talking.
