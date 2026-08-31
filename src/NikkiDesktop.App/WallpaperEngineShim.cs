namespace NikkiDesktop.App;

internal static class WallpaperEngineShim
{
    public const string Script = """
        (() => {
          if (window.__nikkiDesktopShimInstalled) return;
          window.__nikkiDesktopShimInstalled = true;

          const NativeAudio = window.Audio;
          const trackedAudio = [];
          window.__nikkiDesktopTrackedAudio = trackedAudio;
          let audioContext = null;
          let analyser = null;
          let frequencyData = null;
          let analyserConnectedToOutput = false;
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
              analyser.fftSize = 256;
              analyser.smoothingTimeConstant = 0.75;
              frequencyData = new Uint8Array(analyser.frequencyBinCount);
            }
            if (!analyserConnectedToOutput) {
              analyser.connect(audioContext.destination);
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
          window.__nikkiDesktopResumeAudioContext = resumeAudioContext;

          window.__bocchiPauseForFullscreen = function pauseForFullscreen() {
            for (const audio of trackedAudio) {
              if (audio && !audio.paused && !audio.ended) {
                fullscreenPausedAudio.add(audio);
                audio.pause();
              }
            }
            return fullscreenPausedAudio.size;
          };

          window.__bocchiResumeAfterFullscreen = async function resumeAfterFullscreen() {
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

          window.__bocchiClearFullscreenPause = function clearFullscreenPause() {
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
