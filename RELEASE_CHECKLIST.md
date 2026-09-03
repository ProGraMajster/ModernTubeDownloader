# ModernTubeDownloader 1.0.0 release checklist

This checklist distinguishes completed technical preparation from manual/public-release gates. Do not create or push `v1.0.0` until every required item is complete and the user has approved publication.

## Repository and version

- [x] Git repository initialized on `master`.
- [x] `.gitignore` excludes build, test, runtime, tool, local-worktree, and Codex artifacts.
- [x] Source checked for absolute user paths, credentials, cookies, tokens, and local runtime data.
- [x] Version, AssemblyVersion, FileVersion, and InformationalVersion set to 1.0.0 centrally.
- [x] Product, description, author/company, copyright, and repository URL configured.
- [ ] Application license selected and `LICENSE` committed.
- [ ] Repository visibility approved and GitHub remote created.
- [ ] Final branded Windows `.ico` approved and wired to executable/window/taskbar.

## Build and packaging

- [x] `dotnet restore`.
- [x] Debug build.
- [x] Release build.
- [x] Complete automated test suite.
- [x] Self-contained `win-x64` publish.
- [x] Publish launched from its output directory.
- [x] Main ZIP contains no PDB, tests, fake tool, yt-dlp, FFmpeg, logs, cache, or runtime user data.
- [x] Separate symbols ZIP created.
- [x] SHA-256 checksum created.
- [x] README and THIRD_PARTY_NOTICES included in the package.

## Managed tools and connectivity

- [x] Live clean-profile first start downloads and verifies yt-dlp.
- [x] Live clean-profile first start downloads and verifies FFmpeg/ffprobe.
- [x] Second start confirms valid tools are not downloaded again.
- [x] Installed-tools start without internet remains usable.
- [x] First start without internet shows a readable error and Retry without crashing.

## Download workflow

- [x] Deterministic fake-tool Analyze → queue → download → merge → final file.
- [x] Live lawful YouTube video download (Big Buck Bunny, 360p video + audio merge).
- [x] Live final JSON sidecar has the same basename and valid full metadata.
- [x] Live publication date produces the expected filesystem CreationTime.
- [x] Automated sidecar OFF behavior.
- [x] Automated publication timestamp OFF behavior.
- [ ] Live premiere/completed-live date smoke, if a suitable lawful sample is available.

## UI and persisted behavior

- [x] Details full metadata.
- [x] Details missing description, thumbnail, formats, subtitles, and chapters.
- [x] Details long Unicode title.
- [x] Details repeated open/close.
- [x] PL and EN principal screens.
- [x] Light and Dark principal screens.
- [x] System theme persistence covered automatically.
- [x] 1440×900 and 1280×720 layouts.
- [ ] 100%, 125%, and 150% DPI manual matrix.
- [x] One-item and several-item queue.
- [x] Queue overflow, auto-scroll, wheel routing, cancel, retry, removal, and persistence covered by implementation/tests or observed smoke.
- [ ] 20+ item queue manual stress pass.
- [x] History creation and restart persistence.
- [ ] Manual missing-history-file action and shell open-file/open-folder matrix from published build.
- [x] Empty, invalid, and unsupported URL handling covered by implementation/tests.
- [x] Live unavailable-video error smoke.

## Documentation and publication

- [x] README reviewed.
- [x] CHANGELOG 1.0.0 reviewed.
- [x] THIRD_PARTY_NOTICES reviewed against the selected yt-dlp and GPL FFmpeg sources.
- [x] CURRENT_STATUS separates implemented, tested, limited, and planned behavior.
- [x] CI workflow prepared for push and pull requests.
- [x] Tag-only release workflow prepared with a hard `LICENSE` prerequisite.
- [x] Local release-candidate ZIP contents inspected (208 entries; no PDB, test, fake-tool, downloaded-tool, runtime-data, XML-doc, or build-helper files).
- [ ] User approval for public release.
- [ ] Push repository.
- [ ] Create and push `v1.0.0` tag.
- [ ] Verify GitHub Actions release job.
- [ ] Verify GitHub Release assets and checksum.
