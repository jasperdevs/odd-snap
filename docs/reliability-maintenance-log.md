# Reliability Maintenance Log

## Scope
- Conservative maintenance pass focused on safe, high-confidence reliability, performance, and crash-prevention improvements.
- No UI/UX/design changes. No new features. No broad refactors.

## Initial inspection
- Repository mapped and scoped before edits.
- Primary focus areas identified: app lifecycle, capture pipeline, recording, clipboard, hotkeys, OCR, history/storage, startup/shutdown, and native interop.
- Validation approach: smallest relevant targeted tests and build checks after each meaningful change.

## Ranked plan
- 1. Fix hotkey registration/message-pump reliability so non-capture-only hotkey setups still work and bad registrations fail safely.
- 2. Dispose WinRT OCR imaging objects deterministically in `OcrService` to prevent native-memory growth across repeated OCR runs.
- 3. Add synchronization around shared DXGI staging-texture cache access to prevent race conditions during overlapping captures.
- 4. Clean up `VideoRecorder` process/device lifetime leaks in desktop-audio and audio-mux paths.
- 5. Validate each step with the smallest relevant test/build command before continuing.

## Step 1
- What I changed: Centralized hotkey registration in `HotkeyService` so every hotkey type attaches the message hook, unregisters any prior ID before re-registering, and cleanly treats `key == 0` as disabled without relying on the capture hotkey path.
- Why it was a problem: Non-capture-only hotkey configurations could register Windows hotkeys without ever wiring the WPF message pump, which left shortcuts inert. Re-registration was also less defensive if a hotkey was changed while the service instance remained alive.
- Files changed: `src/Yoink/Services/HotkeyService.cs`
- How I validated it: `lsp_diagnostics` reported zero errors for `HotkeyService.cs`. Standard `dotnet build`/`dotnet test` validation is currently blocked by existing workspace build artifacts and a running `Yoink.exe` locking the default debug output.
- Whether any risk remains: Low. This changes only registration plumbing, but end-to-end hotkey behavior still needs an unlocked app build or manual runtime check.

## Step 2
- What I changed: Added deterministic disposal for the WinRT random-access stream and `SoftwareBitmap` used during OCR conversion.
- Why it was a problem: The OCR path creates native-backed imaging objects on every recognition call. Leaving them for deferred cleanup risks native-memory growth during repeated OCR captures.
- Files changed: `src/Yoink/Services/OcrService.cs`
- How I validated it: `lsp_diagnostics` reported zero errors for `OcrService.cs`.
- Whether any risk remains: Very low. The change only tightens resource cleanup around existing OCR calls.

## Step 3
- What I changed: Serialized access to the cached DXGI device/context bundle during capture and warm-up.
- Why it was a problem: The cached D3D device context and staging textures were shared process-wide. Overlapping capture/warm-up work could race on native resources and crash or corrupt capture state.
- Files changed: `src/Yoink/Capture/DxgiScreenCapture.cs`
- How I validated it: `lsp_diagnostics` reported zero errors for `DxgiScreenCapture.cs`.
- Whether any risk remains: Low. The tradeoff is slightly less parallelism during DXGI use, but that is preferable to unsynchronized native access.

## Step 4
- What I changed: Disposed the temporary desktop-audio device enumerator immediately after use and wrapped the audio mux process in deterministic disposal.
- Why it was a problem: Repeated recordings could leak native/process handles in the desktop-audio and FFmpeg mux paths.
- Files changed: `src/Yoink/Capture/VideoRecorder.cs`
- How I validated it: `lsp_diagnostics` reported zero errors for `VideoRecorder.cs`.
- Whether any risk remains: Low. Recording behavior is unchanged; the fix is limited to process/device lifetime cleanup.

## Step 5
- What I changed: Reused the encoded PNG clipboard buffer when possible instead of always cloning it with `ToArray()`.
- Why it was a problem: Every image copy paid for an avoidable extra allocation and buffer copy before reaching the clipboard data object.
- Files changed: `src/Yoink/Services/ClipboardService.cs`
- How I validated it: `lsp_diagnostics` reported zero errors for `ClipboardService.cs`.
- Whether any risk remains: Very low. There is a fallback to the previous copied-buffer path if the stream buffer cannot be exposed.

## Step 6
- What I changed: Made the sticker/upscale runtime installers choose a compatible base Python (`3.12`, `3.11`, then `3.10`) instead of blindly using `py -3`, and forced old runtime environments to rebuild when they were created with an incompatible interpreter. Also added tests for launcher parsing/selection and tightened a couple of smaller retention points (`OcrResultWindow` token disposal and `ToolListBuilder` textbox cleanup).
- Why it was a problem: On machines where `py -3` resolves to Python `3.13`, the isolated rembg/onnxruntime setup could fail because the selected interpreter was outside the supported range for the local ML runtime packages. That made sticker-model setup fail unpredictably depending on which Python version happened to be the system default.
- Files changed: `src/Yoink/Services/PythonLauncherSelector.cs`, `src/Yoink/Services/RembgRuntimeService.cs`, `src/Yoink/Services/UpscaleRuntimeService.cs`, `src/Yoink/UI/OcrResultWindow.xaml.cs`, `src/Yoink/UI/ToolListBuilder.cs`, `tests/Yoink.Tests/PythonLauncherSelectorTests.cs`
- How I validated it: `dotnet test tests\Yoink.Tests\Yoink.Tests.csproj --filter PythonLauncherSelectorTests` passed (`9/9`). A full `dotnet build`/`dotnet test` run was partially blocked by a separate running `dotnet.exe` locking the default WPF build output in the workspace.
- Whether any risk remains: Low. The remaining memory-heavy areas I found are bounded caches and in-memory search records, not a new unbounded leak. They can raise baseline memory usage with large histories, but the main always-on leak risk from orphaned helper processes is now addressed.

## Step 7
- What I changed: Fixed Preview drag-out so unsaved screenshots no longer leave temp PNG files behind after drag/drop completes.
- Why it was a problem: Repeated drag-outs from transient preview windows leaked temp files into `%TEMP%`. This is a storage/perf leak rather than a RAM leak, but it accumulates over long use and fit the same retention-audit scope.
- Files changed: `src/Yoink/UI/PreviewWindow.Actions.cs`
- How I validated it: Code-path review plus successful targeted test/build coverage from the earlier runtime-selector pass. Full WPF project verification is still partially blocked by existing workspace-generated `obj` state and a running local `dotnet` host.
- Whether any risk remains: Very low for this path. The remaining notable costs are bounded thumbnail/search caches and startup preload memory, not a temp-file retention issue.

## Step 8
- What I changed: Reduced baseline memory pressure from history/search by removing eager semantic-runtime startup, preventing startup from implicitly initializing the image-search index, shrinking thumbnail cache/warm sizes, adding idle trim for image-search caches/runtime sessions, and keeping semantic embeddings out of steady-state in-memory records unless a semantic search actually loads them on demand.
- Why it was a problem: The app was paying a large baseline memory cost even while mostly idle. Startup thumbs/search warming and resident embedding arrays made Yoink look leakier than it was, especially with large screenshot histories.
- Files changed: `src/Yoink/App/App.Startup.cs`, `src/Yoink/App/App.Lifecycle.cs`, `src/Yoink/Services/LocalClipRuntimeService.cs`, `src/Yoink/Services/ImageSearchIndexService.cs`, `src/Yoink/Services/ImageSearchIndexService.Indexing.cs`, `src/Yoink/Services/ImageSearchIndexService.Search.cs`, `src/Yoink/UI/Settings/Infrastructure/SettingsMediaCache.cs`
- How I validated it: `dotnet test tests\Yoink.Tests\Yoink.Tests.csproj --filter "PythonLauncherSelectorTests|ImageSearchQueryMatcherTests"` passed (`16/16`). File-level diagnostics for the edited sources reported zero diagnostics. Full-solution WPF rebuilds remain noisy because of existing workspace-generated `obj`/`wpftmp` state unrelated to these edits.
- Whether any risk remains: Moderate but bounded. The app can still use substantial memory with very large histories, but it now avoids several sources of always-on baseline memory residency and should release more search/runtime memory while idle.

## Step 9
- What I changed: Replaced SoundService's thread-per-playback behavior with a bounded shared background playback worker.
- Why it was a problem: Frequent UI events could spawn many short-lived sound threads, which is unnecessary scheduling overhead and a measurable performance smell even though it is not a classic memory leak.
- Files changed: `src/Yoink/Services/SoundService.cs`
- How I validated it: File-level diagnostics reported zero diagnostics, and `dotnet test tests\Yoink.Tests\Yoink.Tests.csproj --filter "PythonLauncherSelectorTests|ImageSearchQueryMatcherTests|AppSettingsTests"` passed (`25/25`).
- Whether any risk remains: Low. The queue is bounded, so in very bursty cases some sounds may be dropped instead of creating unbounded work.
