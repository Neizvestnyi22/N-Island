## N-Island 1.9.0 — no more stutters, 17 new visualizers, lyrics animations

*made in TG [@Ne1zvestny1i](https://t.me/Ne1zvestny1i) & Claude*

Update from the app: **Settings → About → Update** · Обновление прямо из приложения: **Настройки → О программе → Обновить**.

### ⚡ Stutters fixed · Подтормаживания исправлены
Found by measuring, not guessing:
- The island character and the “living island” effects were hooked to WPF's frame loop, which redraws the window at the monitor's refresh rate even when nothing changes. They now share one frame clock (sleep 6 fps, idle 10, dancing 30), and the character, the bars and the rim are drawn in a single redraw.
- Audio analysis ran 64 times a second even in silence. Now it's 30 for the bars in the island, 60 only for the visualizer, 10 in silence.
- The settings window took up to a third of a core while music played: its shadow was an effect over the whole transparent window. It's now a separate layer, 2.5× cheaper.
- Real-time protection scanned every build output file in Desktop subfolders (`bin`, `obj`, `node_modules`) and locked them during builds. The Desktop is now watched at the top level only, build and dependency folders are skipped, scans run one at a time.

On the test laptop: idle island with the character ~3 % of one core instead of 12–18 %, with music 9.5 % instead of 17 %, with settings open 16 % instead of 35 %.
Замеры, а не догадки: персонаж и эффекты больше не крутят перерисовку окна, звук анализируется по необходимости, тень окна настроек — отдельным слоем, Защитник не трогает папки сборок.

### 🌈 17 new visualizers · 17 новых визуализаторов
Neon capsules, pulse ring, particle sphere, wireframe terrain, starburst, gradient clouds, smoke, 3D equalizer, dot matrix, spiral spectrum, laser show, comets, sound fabric, glitch, beat grid, orbits, line streams — 61 styles in total.
Styles that barely listened to the music were replaced: every style is now benchmarked for how much the picture changes between silence and a loud beat. Cassette, Vinyl and Night city are gone (your choice switches to Neon capsules, Spiral spectrum and 3D equalizer); Plasma, Orb, Synthwave, Blob and Lightning now take their size, brightness and speed from the loudness and beats.
Новая группа «Новые», слабые стили заменены или переделаны.

### 🎤 Lyrics animations · Анимации текста песен
11 line entrance animations — fade, rise, drop, slide, zoom, flip, from blur, flash, wipe, typewriter and random — in the player and in the desktop lyrics. Right-click the style button in the player for the next one. New text settings: animation speed, cascade on song change, current line size, line spacing, uppercase, shadow under the current line.
11 анимаций появления строк и новые настройки текста.

### 🛠 Fixes · Исправлено
The Defender section left a subscription on every visit, so old copies re-rendered on every file scan; the MCP call log piled up handlers; the hidden AI chat rebuilt on every Telegram message; Ukrainian used «Човка»/«чілка» instead of «острів». `NISLAND_PERF=1` logs frame-loop counts and animated elements every 10 s for hunting stutters.
