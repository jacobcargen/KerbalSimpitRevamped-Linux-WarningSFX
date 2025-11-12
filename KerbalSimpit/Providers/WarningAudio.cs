using System;
using System.Reflection;
using UnityEngine;
using KSP.UI.Screens;

namespace KerbalSimpit.Providers
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class WarningAudio : MonoBehaviour
    {
        const int LOW_ALTITUDE_WARNING_THRESHOLD = 200; // TERRAIN warning: below this altitude in meters (match KSP mod)
        // NOTE: ALTITUDE_SPEED_THRESHOLD removed - terrain/pull-up warnings are now gated by gear state only
        const double TIME_TO_IMPACT_WARNING_THRESHOLD = 5.0; // PULL UP warning: time to impact in seconds (match KSP mod)
        const double HIGH_GEE_WARNING_SOLID_THRESHOLD = 4.5; // 4.5G (slow beeping)
        const double HIGH_GEE_WARNING_BLINKING_THRESHOLD = 6.5; // 6.5G (fast beeping)
        const double STALL_MIN_SPEED_THRESHOLD = 50.0; // Stall warning ignored below this speed (m/s)
        const double OVERSPEED_SPEED_THRESHOLD = 900.0; // Overspeed warning above this speed (m/s)
        const double OVERSPEED_ALTITUDE_THRESHOLD = 15000.0; // Overspeed warning only below this altitude (m) - INCREASED to 15km
        const double GEAR_SPEED_THRESHOLD = 100.0; // Gear warning when gear down and speed > 100 m/s
        const byte HIGH_TEMP_WARNING_SOLID_THRESHOLD = 50; // 50% - REDUCED from 60%
        const byte HIGH_TEMP_WARNING_BLINKING_THRESHOLD = 80; // 80% - REDUCED from 85%
        AudioSource src;
    	AudioSource genSrc; // separate source for generated tones so voice clips can play reliably
        // audio files for voice warnings - now arrays for multiple random clips
        AudioClip[] altWarningClips;
        AudioClip[] geeWarningClips;
        AudioClip[] pitchWarningClips;
        AudioClip[] stallWarningClips;
        AudioClip[] overspeedWarningClips;
        AudioClip[] gearWarningClips;
        
        // beep clips for each warning type
        AudioClip altBeepClip;
        AudioClip geeBeepClip;
        AudioClip pitchBeepClip;
        AudioClip stallBeepClip;
        AudioClip overspeedBeepClip;
        AudioClip gearBeepClip;
        
        System.Random rng = new System.Random();

        public static bool AudioEnabled = true;

        volatile int playGee = 0;
        volatile bool playAlt = false;
        volatile bool playPitch = false;
        volatile bool playStall = false;
        volatile bool playOverspeed = false;
        volatile bool playBrake = false;
        volatile bool playGear = false;
        volatile int playTemp = 0;  // 0=off, 1=blinking, 2=solid
        bool brakeAudioPlaying = false;  // Track if brake tone is currently playing
		// Playback cooldowns to avoid spamming the same warning every frame
		float lastGeePlay = 0f;
		float lastAltPlay = 0f;
		float lastPitchPlay = 0f;
		float lastStallPlay = 0f;
		float lastOverspeedPlay = 0f;
		float lastBrakePlay = 0f;
		float lastGearPlay = 0f;
		float lastTempPlay = 0f;
		// Require a 3 second repeat interval to avoid annoyance and match user request
		const float GEE_FAST_INTERVAL = 3.0f;
		const float GEE_SLOW_INTERVAL = 3.0f;
		const float ALT_INTERVAL = 3.0f;
		const float PITCH_INTERVAL = 5.0f;  // Increased to 5 seconds to reduce pull-up stutter
		const float STALL_INTERVAL = 3.0f;
		const float OVERSPEED_INTERVAL = 3.0f;
		const float BRAKE_INTERVAL = 0.1f;  // Play continuously while brakes are on
		const float GEAR_INTERVAL = 3.0f;
		const float TEMP_INTERVAL = 2.0f;   // Temperature warning interval
        void Awake()
        {
            // Load voice clips and beeps for each warning type from new folder structure
            altWarningClips = LoadVoiceClips("KerbalSimpit/voice_warnings/alt/voice");
            altBeepClip = TryLoadAudioClip("KerbalSimpit/voice_warnings/alt/beep");
            
            geeWarningClips = LoadVoiceClips("KerbalSimpit/voice_warnings/overg/voice");
            geeBeepClip = TryLoadAudioClip("KerbalSimpit/voice_warnings/overg/beep");
            
            pitchWarningClips = LoadVoiceClips("KerbalSimpit/voice_warnings/pullup/voice");
            pitchBeepClip = TryLoadAudioClip("KerbalSimpit/voice_warnings/pullup/beep");
            
            stallWarningClips = LoadVoiceClips("KerbalSimpit/voice_warnings/stall/voice");
            stallBeepClip = TryLoadAudioClip("KerbalSimpit/voice_warnings/stall/beep");
            
            overspeedWarningClips = LoadVoiceClips("KerbalSimpit/voice_warnings/overspeed/voice");
            overspeedBeepClip = TryLoadAudioClip("KerbalSimpit/voice_warnings/overspeed/beep");
            
            gearWarningClips = LoadVoiceClips("KerbalSimpit/voice_warnings/gear/voice");
            gearBeepClip = TryLoadAudioClip("KerbalSimpit/voice_warnings/gear/beep");
            
            Debug.Log($"[WarningAudio] Loaded: alt={altWarningClips.Length} voices, gee={geeWarningClips.Length} voices, pitch={pitchWarningClips.Length} voices, stall={stallWarningClips.Length} voices, overspeed={overspeedWarningClips.Length} voices, gear={gearWarningClips.Length} voices");
            ScreenDebug($"Warnings: alt={altWarningClips.Length}, gee={geeWarningClips.Length}, pitch={pitchWarningClips.Length}, stall={stallWarningClips.Length}, overspeed={overspeedWarningClips.Length}, gear={gearWarningClips.Length}", 4f);
        }
        
        // Load all voice clips from a directory
        AudioClip[] LoadVoiceClips(string dirPath)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();
            
            // Extract the warning name from the path (e.g., "alt" from ".../alt/voice")
            string warningName = "";
            string[] pathParts = dirPath.Split('/');
            if (pathParts.Length >= 2)
            {
                warningName = pathParts[pathParts.Length - 2]; // Get parent folder name
            }
            
            // Try loading the main voice file (e.g., alt.ogg, overg.ogg, pullup.ogg)
            if (!string.IsNullOrEmpty(warningName))
            {
                var clip = TryLoadAudioClip(dirPath + "/" + warningName);
                if (clip != null) 
                {
                    clips.Add(clip);
                    Debug.Log($"[WarningAudio] Found voice file: {warningName}");
                }
            }
            
            // Try common naming patterns for multiple voice files
            for (int i = 1; i <= 10; i++)
            {
                var clip = TryLoadAudioClip(dirPath + "/" + warningName + i);
                if (clip != null) clips.Add(clip);
            }
            
            // Try generic numbered files
            for (int i = 1; i <= 10; i++)
            {
                var clip = TryLoadAudioClip(dirPath + "/" + i);
                if (clip != null) clips.Add(clip);
            }
            
            // Try generic "voice" name
            var voiceClip = TryLoadAudioClip(dirPath + "/voice");
            if (voiceClip != null) clips.Add(voiceClip);
            
            return clips.ToArray();
        }

        // Try loading an audio clip using several candidate suffixes (no extension, .mp3, .ogg, .wav)
        AudioClip TryLoadAudioClip(string basePath)
        {
            string[] candidates = new string[] {
                basePath,            // without extension
                basePath + ".mp3",
                basePath + ".ogg",
                basePath + ".wav"
            };
            foreach (var p in candidates)
            {
                try
                {
                    var c = GameDatabase.Instance.GetAudioClip(p);
                    Debug.Log($"[WarningAudio] TryLoadAudioClip: tried '{p}' -> " + (c == null ? "NULL" : "FOUND"));
                    // Also post a brief on-screen debug so it's visible in-game during testing
                    ScreenDebug($"Tried '{p}' -> " + (c == null ? "NULL" : "FOUND"), 1.5f);
                    if (c != null)
                    {
                        Debug.Log($"[WarningAudio] AudioClip properties for '{p}': length={{c.length}}, samples={{c.samples}}, freq={{c.frequency}}, loadState={{c.loadState}}.");
                        return c;
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log($"[WarningAudio] Exception loading clip '{p}': " + ex.Message);
                    ScreenDebug($"Exception loading '{p}': {ex.Message}", 3f);
                }
            }
            Debug.Log($"[WarningAudio] All GameDatabase attempts failed for '{basePath}'.");
            // Fallback: try loading from StreamingAssets (if allowed)
            try
            {
                string streamingPath = System.IO.Path.Combine(Application.streamingAssetsPath, basePath + ".mp3");
                if (System.IO.File.Exists(streamingPath))
                {
                    Debug.Log($"[WarningAudio] Found file in StreamingAssets: {streamingPath}");
                    ScreenDebug("Found audio in StreamingAssets: " + System.IO.Path.GetFileName(streamingPath), 3f);
                }
                else
                {
                    Debug.Log($"[WarningAudio] Not found in StreamingAssets: {streamingPath}");
                    ScreenDebug("Not found in StreamingAssets: " + System.IO.Path.GetFileName(streamingPath), 2f);
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[WarningAudio] Exception checking StreamingAssets: " + ex.Message);
                ScreenDebug("Exception checking StreamingAssets: " + ex.Message, 3f);
            }
            return null;
        }

        // Helper: post a short debug message to the KSP screen and log to file
        void ScreenDebug(string msg, float duration = 3f)
        {
            try
            {
                Debug.Log("[WarningAudio] " + msg);
                ScreenMessages.PostScreenMessage("[WarningAudio] " + msg, duration, ScreenMessageStyle.UPPER_LEFT);
            }
            catch { /* swallow to avoid crashing in unusual contexts */ }
        }
        void Start()
        {
            src = CreateAudioSource("WarnSrc", 0.9f);
            genSrc = CreateAudioSource("WarnGenSrc", 0.9f);
            KSPit.AddToDeviceHandler(WarningAudioProvider);
            Debug.Log("[WarningAudio] simple warning provider started");
        }

        void OnDestroy()
        {
            KSPit.RemoveToDeviceHandler(WarningAudioProvider);
        }

        void Update()
        {
            // Use Time.time to rate-limit how often each warning can trigger so we
            // don't keep restarting the same sound every frame.
            float now = Time.time;

            if (playGee == 1)
            {
                if (now - lastGeePlay >= GEE_FAST_INTERVAL)
                {
                    PlayGeeFast();
                    lastGeePlay = now;
                }
            }
            else if (playGee == 2)
            {
                if (now - lastGeePlay >= GEE_SLOW_INTERVAL)
                {
                    PlayGeeSlow();
                    lastGeePlay = now;
                }
            }

            if (playAlt)
            {
                if (now - lastAltPlay >= ALT_INTERVAL)
                {
                    PlayAlt();
                    lastAltPlay = now;
                }
            }

            if (playPitch)
            {
                if (now - lastPitchPlay >= PITCH_INTERVAL)
                {
                    PlayPitch();
                    lastPitchPlay = now;
                }
            }
            
            if (playStall)
            {
                if (now - lastStallPlay >= STALL_INTERVAL)
                {
                    PlayStall();
                    lastStallPlay = now;
                }
            }
            
            if (playOverspeed)
            {
                if (now - lastOverspeedPlay >= OVERSPEED_INTERVAL)
                {
                    PlayOverspeed();
                    lastOverspeedPlay = now;
                }
            }
            
            if (playGear)
            {
                if (now - lastGearPlay >= GEAR_INTERVAL)
                {
                    PlayGear();
                    lastGearPlay = now;
                }
            }
            
            // Brake tone is continuous - start playing if not already playing
            // Also respect AudioEnabled - stop if audio is disabled
            if (playBrake && AudioEnabled)
            {
                if (!brakeAudioPlaying)
                {
                    StartBrakeTone();
                }
            }
            else
            {
                // Stop brake tone if it's playing but shouldn't be, OR if audio is disabled
                if (brakeAudioPlaying)
                {
                    StopBrakeTone();
                }
            }
        }

        AudioSource CreateAudioSource(string name, float vol)
        {
            var go = new GameObject(name);
            go.transform.parent = transform;
            var a = go.AddComponent<AudioSource>();
            a.volume = vol; a.spatialBlend = 0f; a.playOnAwake = false; a.loop = false;
            return a;
        }

        // Called from device thread by KSPit
        public void WarningAudioProvider()
        {
            if (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null) return;
            var v = FlightGlobals.ActiveVessel;

            double vs = v.verticalSpeed;
            bool gearDown = false;
            double alt = v.radarAltitude;
            double gforce = v.geeForce;
            double surfaceSpeed = v.srfSpeed;
            try { gearDown = v.ActionGroups[KSPActionGroup.Gear]; } catch { gearDown = v.LandedOrSplashed; }

            // G-force
            if (gforce >= HIGH_GEE_WARNING_BLINKING_THRESHOLD)
            {
                // Blinking for extreme G-force
                playGee = 1;
            }
            else if (gforce >= HIGH_GEE_WARNING_SOLID_THRESHOLD)
            {
                // Solid ON for high G-force
                playGee = 2;
            }
            else
            {
                playGee = 0;
            }
            // prevent underwater/negative alt warnings (clamp to zero)
            if (alt < 0.0) alt = 0.0;
            playAlt = (!gearDown && alt > 0.0 && alt < LOW_ALTITUDE_WARNING_THRESHOLD);

            // Pitch / pull-up: descending and time-to-impact < threshold
            bool descending = vs < 0;
            double timeToImpact = descending ? (alt / -vs) : double.PositiveInfinity;
            playPitch = (!gearDown && descending && timeToImpact < TIME_TO_IMPACT_WARNING_THRESHOLD);
            
            // Stall warning: only when under 100 mph (44.7 m/s) AND gear is up
            // Subtract vertical velocity to get horizontal component (ignore vertical descent)
            const double STALL_SPEED_MPH_THRESHOLD = 100.0; // 100 mph
            const double STALL_SPEED_MS_THRESHOLD = STALL_SPEED_MPH_THRESHOLD * 0.44704; // Convert to m/s (44.7 m/s)
            double horizontalSpeed = Math.Sqrt(Math.Max(0, surfaceSpeed * surfaceSpeed - vs * vs));
            playStall = (!gearDown && horizontalSpeed < STALL_SPEED_MS_THRESHOLD);
            
            // Overspeed warning: speed > 1000 m/s AND altitude < 10 km
            playOverspeed = (surfaceSpeed > OVERSPEED_SPEED_THRESHOLD && alt < OVERSPEED_ALTITUDE_THRESHOLD);
            
            // Brake warning: solid tone when brakes are on
            bool brakesOn = false;
            try { brakesOn = v.ActionGroups[KSPActionGroup.Brakes]; } catch { }
            playBrake = brakesOn;
            
            // Gear warning: flash when gear down and speed > 100 m/s
            playGear = (gearDown && surfaceSpeed > GEAR_SPEED_THRESHOLD);
            
            // Temperature warning: check max temperature across all parts
            byte maxTempPercentage = 0;
            foreach (Part part in v.Parts)
            {
                byte tempPercent = (byte)Math.Min(255, Math.Round(100.0 * part.temperature / part.maxTemp));
                byte skinTempPercent = (byte)Math.Min(255, Math.Round(100.0 * part.skinTemperature / part.skinMaxTemp));
                maxTempPercentage = Math.Max(maxTempPercentage, Math.Max(tempPercent, skinTempPercent));
            }
            
            if (maxTempPercentage >= HIGH_TEMP_WARNING_BLINKING_THRESHOLD)
            {
                playTemp = 1; // Blinking for critical temperature
            }
            else if (maxTempPercentage >= HIGH_TEMP_WARNING_SOLID_THRESHOLD)
            {
                playTemp = 2; // Solid for high temperature
            }
            else
            {
                playTemp = 0;
            }
            
            // Enable TEMP warning when overspeed is active too
            if (playOverspeed)
            {
                playTemp = Math.Max(playTemp, 2); // At least solid warning
            }
        }

        // ----- Audio helpers (class-level so callers can invoke) -----
        void PlayBeep(float freq, float dur)
        {
            if (!AudioEnabled || src == null) return;
            if (genSrc == null) return;
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sr * dur));
            var clip = AudioClip.Create("beep", samples, 1, sr, false);
            var data = new float[samples];
            for (int i = 0; i < samples; i++) data[i] = Mathf.Sin(2f * Mathf.PI * freq * i / sr) * 0.5f * (1f - (float)i / samples);
            clip.SetData(data, 0);
            genSrc.PlayOneShot(clip);
        }

        // Generate and play a short composite pattern for gee-fast double-beep (military style)
        void PlayGeneratedGeeFastPattern()
        {
            if (!AudioEnabled || genSrc == null) return;
            // pattern: 2 quick high beeps (1200Hz then 900Hz), short gap, repeat
            float[] freqs = new float[] { 1200f, 900f };
            float[] durs = new float[] { 0.06f, 0.06f };
            var clip = CreateCompositeClip(freqs, durs, 0.04f);
            genSrc.PlayOneShot(clip);
        }

        // Pull-up pattern: descending series of beeps to convey urgency
        void PlayPullUpPattern()
        {
            if (!AudioEnabled || genSrc == null) return;
            // a quick staccato: 800Hz -> 1000Hz -> 1200Hz, short notes
            float[] freqs = new float[] { 800f, 1000f, 1200f };
            float[] durs = new float[] { 0.08f, 0.08f, 0.08f };
            var clip = CreateCompositeClip(freqs, durs, 0.03f);
            genSrc.PlayOneShot(clip);
        }

        // Helper: create a composite clip of sequential tones with small gaps
        AudioClip CreateCompositeClip(float[] freqs, float[] durs, float gap)
        {
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int totalSamples = 0;
            int gapSamples = Mathf.Max(0, Mathf.RoundToInt(sr * gap));
            int[] toneSamples = new int[durs.Length];
            for (int i = 0; i < durs.Length; i++)
            {
                toneSamples[i] = Mathf.Max(1, Mathf.RoundToInt(sr * durs[i]));
                totalSamples += toneSamples[i];
                if (i < durs.Length - 1) totalSamples += gapSamples;
            }
            var data = new float[totalSamples];
            int idx = 0;
            for (int t = 0; t < freqs.Length; t++)
            {
                float freq = freqs[t];
                int samples = toneSamples[t];
                for (int i = 0; i < samples; i++)
                {
                    data[idx++] = Mathf.Sin(2f * Mathf.PI * freq * i / sr) * 0.6f * (1f - (float)i / samples);
                }
                // gap
                for (int g = 0; g < gapSamples && idx < totalSamples; g++) data[idx++] = 0f;
            }
            var clip = AudioClip.Create("composite", totalSamples, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Coroutine helper to stop an AudioSource after a timeout
        System.Collections.IEnumerator StopAfter(AudioSource a, float secs)
        {
            yield return new WaitForSeconds(secs);
            try { if (a != null && a.isPlaying) a.Stop(); }
            catch { }
        }

        void PlaySweep(float f0, float f1, float dur)
        {
            if (!AudioEnabled || genSrc == null) return;
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sr * dur));
            var clip = AudioClip.Create("sweep", samples, 1, sr, false);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float freq = Mathf.Lerp(f0, f1, t);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * i / sr) * 0.5f * (1f - t);
            }
            clip.SetData(data, 0);
            genSrc.PlayOneShot(clip);
        }

        void PlayGeeFast()
        {
            // Play beep first if available
            if (geeBeepClip != null && AudioEnabled && genSrc != null)
            {
                genSrc.PlayOneShot(geeBeepClip);
            }
            else
            {
                // Fallback to generated pattern
                PlayGeneratedGeeFastPattern();
            }
            
            ScreenMessages.PostScreenMessage("OVERGEE", 2f, ScreenMessageStyle.UPPER_CENTER);
            
            // Play random voice clip if available
            PlayRandomVoiceClip(geeWarningClips);
        }

        void PlayGeeSlow()
        {
            // Single slower beep to indicate sustained high gee
            PlayBeep(1000f, 0.12f);
            ScreenMessages.PostScreenMessage("OVERGEE", 2f, ScreenMessageStyle.UPPER_CENTER);
        }

        void PlayAlt()
        {
            // Play beep first if available
            if (altBeepClip != null && AudioEnabled && genSrc != null)
            {
                genSrc.PlayOneShot(altBeepClip);
            }
            else
            {
                // Fallback to generated sweep
                PlaySweep(500f, 1000f, 0.25f);
            }
            
            ScreenMessages.PostScreenMessage("TERRAIN", 2f, ScreenMessageStyle.UPPER_CENTER);
            
            // Play random voice clip if available
            PlayRandomVoiceClip(altWarningClips);
        }

        void PlayPitch()
        {
            // Play beep first if available
            if (pitchBeepClip != null && AudioEnabled && genSrc != null)
            {
                genSrc.PlayOneShot(pitchBeepClip);
            }
            else
            {
                // Fallback to generated pattern
                PlayPullUpPattern();
            }
            
            ScreenMessages.PostScreenMessage("PULL UP", 2.5f, ScreenMessageStyle.UPPER_CENTER);
            
            // Play random voice clip if available
            PlayRandomVoiceClip(pitchWarningClips);
        }
        
        void PlayStall()
        {
            // Play fast quick beeps for stall warning (3 quick beeps)
            if (stallBeepClip != null && AudioEnabled && genSrc != null)
            {
                genSrc.PlayOneShot(stallBeepClip);
            }
            else
            {
                // Fallback to generated fast beeps - 3 quick beeps at 800Hz
                StartCoroutine(PlayStallBeeps());
            }
            
            ScreenMessages.PostScreenMessage("STALL", 2f, ScreenMessageStyle.UPPER_CENTER);
            
            // Play random voice clip if available
            PlayRandomVoiceClip(stallWarningClips);
        }
        
        System.Collections.IEnumerator PlayStallBeeps()
        {
            // Play 3 fast quick beeps
            for (int i = 0; i < 3; i++)
            {
                PlayBeep(800f, 0.08f);  // Short 80ms beeps
                yield return new WaitForSeconds(0.12f);  // 120ms between beeps
            }
        }
        
        void PlayOverspeed()
        {
            // Play beep first if available
            if (overspeedBeepClip != null && AudioEnabled && genSrc != null)
            {
                genSrc.PlayOneShot(overspeedBeepClip);
            }
            else
            {
                // Fallback to generated beep
                PlayBeep(1500f, 0.15f);
            }
            
            ScreenMessages.PostScreenMessage("OVERSPEED", 2f, ScreenMessageStyle.UPPER_CENTER);
            
            // Play random voice clip if available
            PlayRandomVoiceClip(overspeedWarningClips);
        }
        
        void PlayGear()
        {
            // Play beep first if available
            if (gearBeepClip != null && AudioEnabled && genSrc != null)
            {
                genSrc.PlayOneShot(gearBeepClip);
            }
            else
            {
                // Fallback to generated beep - 700Hz moderate tone
                PlayBeep(700f, 0.15f);
            }
            
            ScreenMessages.PostScreenMessage("GEAR SPEED", 2f, ScreenMessageStyle.UPPER_CENTER);
            
            // Play random voice clip if available
            PlayRandomVoiceClip(gearWarningClips);
        }
        
        void StartBrakeTone()
        {
            // Start continuous solid tone at moderate volume - 600Hz
            if (!AudioEnabled || genSrc == null) return;
            
            // Create a 1-second looping clip for smooth continuous tone
            int sr = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int samples = sr; // 1 second worth of samples
            var clip = AudioClip.Create("brake_tone", samples, 1, sr, false);
            var data = new float[samples];
            
            // Generate solid 600Hz tone at 10% volume (quieter)
            for (int i = 0; i < samples; i++) 
                data[i] = Mathf.Sin(2f * Mathf.PI * 600f * i / sr) * 0.1f;
            
            clip.SetData(data, 0);
            genSrc.clip = clip;
            genSrc.loop = true;
            genSrc.Play();
            brakeAudioPlaying = true;
        }
        
        void StopBrakeTone()
        {
            if (genSrc != null)
            {
                genSrc.Stop();
                genSrc.loop = false;
                genSrc.clip = null;
            }
            brakeAudioPlaying = false;
        }
        
        // Helper to play a random voice clip from an array
        void PlayRandomVoiceClip(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0 || !AudioEnabled || src == null) return;
            
            try
            {
                // Select a random clip
                AudioClip selectedClip = clips[rng.Next(clips.Length)];
                src.clip = selectedClip;
                src.loop = false;
                src.Play();
                // Ensure the clip doesn't overrun — stop after 2s
                StartCoroutine(StopAfter(src, 2f));
            }
            catch (Exception ex)
            {
                Debug.LogError("[WarningAudio] Failed to play random voice clip: " + ex.Message);
            }
        }

        // Exposed so other components (KeyboardEmulator) can toggle audio
        public void SetAudioEnabled(bool e)
        {
            AudioEnabled = e;
            if (!e && src != null && src.isPlaying) src.Stop();
        }
    }
}
