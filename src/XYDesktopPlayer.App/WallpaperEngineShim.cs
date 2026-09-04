namespace XYDesktopPlayer.App;

internal static class WallpaperEngineShim
{
    public const int FadeDurationMilliseconds = 500;

    public static string Script { get; } = $$"""
        (() => {
          if (window.__xyDesktopShimInstalled) return;
          window.__xyDesktopShimInstalled = true;

          const NativeAudio = window.Audio;
          const trackedAudio = [];
          window.__xyDesktopTrackedAudio = trackedAudio;
          let audioContext = null;
          let analyser = null;
          let masterGain = null;
          let frequencyData = null;
          let analyserConnectedToOutput = false;
          let fadeGeneration = 0;
          let listener = null;
          const connectedAudio = new WeakSet();
          const fullscreenPausedAudio = new Set();

          function connectAudioElement(audio) {
            if (!audioContext || !analyser || connectedAudio.has(audio)) return;
            try {
              const source = audioContext.createMediaElementSource(audio);
              source.connect(analyser);
              connectedAudio.add(audio);
            } catch (_) {
              // An element can only have one MediaElementSource. Ignore duplicates.
            }
          }

          function ensureAudioGraph() {
            if (!audioContext) {
              const AudioContextType = window.AudioContext || window.webkitAudioContext;
              if (!AudioContextType) return false;
              audioContext = new AudioContextType();
              analyser = audioContext.createAnalyser();
              masterGain = audioContext.createGain();
              analyser.fftSize = 256;
              analyser.smoothingTimeConstant = 0.75;
              frequencyData = new Uint8Array(analyser.frequencyBinCount);
            }
            if (!analyserConnectedToOutput) {
              analyser.connect(masterGain);
              masterGain.connect(audioContext.destination);
              analyserConnectedToOutput = true;
            }
            trackedAudio.forEach(connectAudioElement);
            return true;
          }

          function TrackedAudio(...args) {
            const audio = new NativeAudio(...args);
            trackedAudio.push(audio);
            if (audioContext) connectAudioElement(audio);
            return audio;
          }
          TrackedAudio.prototype = NativeAudio.prototype;
          Object.setPrototypeOf(TrackedAudio, NativeAudio);
          window.Audio = TrackedAudio;

          window.wallpaperRegisterAudioListener = function registerAudioListener(callback) {
            listener = typeof callback === 'function' ? callback : null;
            ensureAudioGraph();
          };

          function resumeAudioContext() {
            if (!ensureAudioGraph()) return;
            trackedAudio.forEach(connectAudioElement);
            if (audioContext.state === 'suspended') {
              audioContext.resume().catch(() => {});
            }
          }
          window.__xyDesktopResumeAudioContext = resumeAudioContext;

          const fadeDurationMilliseconds = {{FadeDurationMilliseconds}};
          window.__xyDesktopFadeDurationMs = fadeDurationMilliseconds;
          window.__xyDesktopLastFadeElapsedMs = 0;
          let autoPauseRequested = false;

          window.__xyDesktopSetAutoPauseRequested = function setAutoPauseRequested(value) {
            autoPauseRequested = value === true;
          };

          function resetMasterGain() {
            if (!masterGain || !audioContext) return;
            const now = audioContext.currentTime;
            masterGain.gain.cancelScheduledValues(now);
            masterGain.gain.setValueAtTime(1, now);
          }

          function waitForFade() {
            return new Promise(resolve => window.setTimeout(resolve, fadeDurationMilliseconds));
          }

          window.__xyDesktopPauseForFullscreen = async function pauseForFullscreen() {
            if (!autoPauseRequested) return fullscreenPausedAudio.size;
            const candidates = trackedAudio.filter(audio => audio && !audio.paused && !audio.ended);
            if (candidates.length === 0) return fullscreenPausedAudio.size;

            const generation = ++fadeGeneration;
            const fadeStartedAt = performance.now();
            if (ensureAudioGraph() && masterGain && audioContext) {
              if (audioContext.state === 'suspended') {
                try { await audioContext.resume(); } catch (_) {}
              }
              const now = audioContext.currentTime;
              masterGain.gain.cancelScheduledValues(now);
              masterGain.gain.setValueAtTime(masterGain.gain.value, now);
              masterGain.gain.linearRampToValueAtTime(0, now + fadeDurationMilliseconds / 1000);
            }

            await waitForFade();
            if (generation !== fadeGeneration) return fullscreenPausedAudio.size;
            if (!autoPauseRequested) {
              // The user returned to the desktop while the fade was running: undo it
              // instead of pausing. The resume path already restored the master gain.
              return fullscreenPausedAudio.size;
            }
            window.__xyDesktopLastFadeElapsedMs = performance.now() - fadeStartedAt;
            for (const audio of candidates) {
              if (audio && trackedAudio.includes(audio) && !audio.paused && !audio.ended) {
                fullscreenPausedAudio.add(audio);
                audio.pause();
              }
            }
            resetMasterGain();
            return fullscreenPausedAudio.size;
          };

          window.__xyDesktopResumeAfterFullscreen = async function resumeAfterFullscreen() {
            autoPauseRequested = false;
            fadeGeneration += 1;
            resetMasterGain();
            const pausedByFullscreen = Array.from(fullscreenPausedAudio);
            fullscreenPausedAudio.clear();
            let resumed = 0;
            for (const audio of pausedByFullscreen) {
              if (!audio || !trackedAudio.includes(audio) || !audio.paused || audio.ended) continue;
              try {
                await audio.play();
                resumed += 1;
              } catch (_) {}
            }
            return resumed;
          };

          // Manual pause from the player button: same 500ms fade-out as the window-switch
          // pause, but the paused audio is NOT tracked for automatic resume.
          window.__xyDesktopFadeStop = async function fadeStop() {
            const candidates = trackedAudio.filter(audio => audio && !audio.paused && !audio.ended);
            if (candidates.length === 0) return 0;

            const generation = ++fadeGeneration;
            if (ensureAudioGraph() && masterGain && audioContext) {
              if (audioContext.state === 'suspended') {
                try { await audioContext.resume(); } catch (_) {}
              }
              const now = audioContext.currentTime;
              masterGain.gain.cancelScheduledValues(now);
              masterGain.gain.setValueAtTime(masterGain.gain.value, now);
              masterGain.gain.linearRampToValueAtTime(0, now + fadeDurationMilliseconds / 1000);
            }

            await waitForFade();
            if (generation !== fadeGeneration) return 0;
            for (const audio of candidates) {
              if (audio && trackedAudio.includes(audio) && !audio.paused && !audio.ended) {
                audio.pause();
              }
            }
            resetMasterGain();
            return candidates.length;
          };

          // Called before any user-requested playback so a cancelled or finished fade never
          // leaves the master gain at zero.
          window.__xyDesktopPrepareManualResume = function prepareManualResume() {
            fadeGeneration += 1;
            resetMasterGain();
          };

          window.__xyDesktopClearFullscreenPause = function clearFullscreenPause() {
            autoPauseRequested = false;
            fadeGeneration += 1;
            resetMasterGain();
            fullscreenPausedAudio.clear();
          };

          window.addEventListener('pointerdown', resumeAudioContext, { capture: true });
          window.addEventListener('keydown', resumeAudioContext, { capture: true });

          function publishSpectrum() {
            if (listener && ensureAudioGraph() && frequencyData) {
              analyser.getByteFrequencyData(frequencyData);
              const stereoSpectrum = new Array(128);
              for (let index = 0; index < 64; index += 1) {
                const value = frequencyData[index] / 255;
                stereoSpectrum[index] = value;
                stereoSpectrum[index + 64] = value;
              }
              try { listener(stereoSpectrum); } catch (_) {}
            }
            window.requestAnimationFrame(publishSpectrum);
          }
          window.requestAnimationFrame(publishSpectrum);
        })();
        """;
}
