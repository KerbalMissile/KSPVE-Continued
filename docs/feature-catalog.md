# KSPVE — Feature Catalog (148 features)

Brainstorming artifact, 2026-09-29. Input to the architecture spec; not yet approved scope.

## Context and principles

- **Target:** KSP 1.12.5 (Unity 2019.4.18f1, built-in render pipeline), Windows, DirectX 11. Reference machine: RTX 4060 8 GB at 3840×2160.
- **Audience:** built for one player first, structured for public release (no hard NVIDIA dependency, graceful fallbacks, config-driven planet support).
- **Complement, don't replace:** EVE (volumetrics), Scatterer, Parallax, Firefly, Deferred, Waterfall stay in charge of their domains. KSPVE takes over only where a feature cannot work otherwise (black holes/wormholes from Singularity), and anything overlapping another mod is switchable.
- **Performance first:** every effect declares quality levels, runs at reduced resolution where it can, reuses history, and reports its own GPU time. A governor holds the frame-rate target.
- **Reference shots:** (A) in-cloud flight, (B) orbital shipyard over Kerbin. Every phase is judged on before/after captures and GPU timings from benchmark scenes built on these.
- **Rendering foundation (decided):** build on blackrack's Deferred as a required dependency. Deferred owns the deferred path and part shader replacements; KSPVE reads Unity's standard built-in G-buffer, injects lighting at Unity's deferred hook points (primarily by replacing the built-in deferred-reflections pass), and writes extra material channels to a small auxiliary buffer for KSPVE materials only. Without Deferred, KSPVE runs in reduced mode (camera/post, exposure, space, particles).
- **Frame-rate targets (decided):** *Play* mode holds 60 fps at 4K output on the reference machine (upscaled, ~1440p internal in heavy scenes; KSPVE's own passes budgeted at ≤ 4 ms of the 16.7 ms frame; GI up to tier 1; target user-adjustable to 90/120). *Cinematic* mode targets 30 fps with everything on (tier 2 GI, heaviest volumetrics); photo mode has no frame-rate limit.

## Legend

**Cost** (GPU time at 4K output with upscaling on the reference machine — targets, not measurements): Free < 0.1 ms · Low 0.1–0.5 ms · Med 0.5–1.5 ms · High 1.5–4 ms · Cine = stills / cinematic mode only.

**Risk** = integration risk with KSP's camera stack and other mods: L / M / H.

**Phases**

| Phase | Focus | Features |
|---|---|---|
| P0 | Foundation | 1–12, 98–100, 128, 133, feasibility spikes for 55 (DXR) and 127 (HDR output) |
| P1 | Camera, HDR & physical units | 23–27, 29, 101, 119, 120 |
| P2 | Temporal, upscaling & frame pacing | 14–22, 125, 138 |
| P3 | Environment lighting | 34, 39, 40, 41, 45, 46, 67, 115 |
| P4 | Shadows & motion | 30, 35–38, 42, 116, 135 |
| P5 | Screen-space & GI | 44, 47–53 |
| P6 | Materials | 56–65, 102–110, 132, 134 |
| P7 | Atmosphere, terrain & FX | 33, 43, 66, 68–72, 74–79, 111–114, 117, 118, 124, 129–131, 148 |
| P8 | Space | 73, 80–85, 87–91 |
| P9 | Interiors, cinematic, polish | 13, 28, 31, 32, 54, 55, 92–97, 121–123, 126, 127 |
| P10 | Space objects & world-building | 86, 140–147 |
| P11 | Engine optimisation II | 136, 137, 139 |

---

## 1. Core platform (13)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 1 | Render hook manager | Central registry that attaches CommandBuffers to every KSP camera (ScaledSpace, Camera 00 far, Camera 01 near, InternalCamera, editor/KSC) at declared CameraEvents, with explicit ordering relative to Scatterer, EVE and Deferred passes. Rebuilds on scene/camera changes. | 2 | Free | H | P0 | 3,000 |
| 2 | Scene context service | One per-frame source of truth: game scene, camera mode (flight/IVA/map/editor), main body, altitude, in-atmosphere and in-cloud flags, star directions/distances, body list. Every effect reads this instead of polling KSP itself. | — | Free | L | P0 | 1,500 |
| 3 | Floating-origin & precision | KSP rebases the world origin and switches to a Krakensbane velocity frame at speed. History buffers, probes and shadow maps are rebased on those shifts; planet-scale math stays double-precision on the CPU and camera-relative float on the GPU. | 2 | Free | M | P0 | 1,200 |
| 4 | Shader pipeline + hot reload | Unity 2019.4.18f1 project that builds the shader asset bundle, strips unused keyword variants to keep it small, and in dev builds reloads the bundle in-game without restarting KSP. | — | — | L | P0 | 2,300 |
| 5 | Compatibility adapters | One adapter per mod (EVE, Scatterer, Parallax, Firefly, Deferred, Waterfall, SpaceDust, Kopernicus, Singularity, TUFX, Shabby, CryoTanks), found by assembly name, accessed via version-guarded reflection. Each exposes only what KSPVE needs and degrades cleanly to "absent". | — | Free | H | P0+ | 2,500 |
| 6 | Config system | ModuleManager-patchable `KSPVE` nodes: global, per body (exposure, grade, planetshine albedo, rings), per part (material overrides). Schema validation with readable errors in log and UI. | — | — | L | P0 | 2,000 |
| 7 | Quality presets & scalability | Each effect declares quality levels (resolution scale, sample counts, update rate). Presets Low → Cinematic map onto them; the governor moves along them. | 6 | — | L | P0 | 1,000 |
| 8 | Settings UI + A/B compare | In-game window: presets, per-effect toggles and sliders, live split-screen before/after, save to config. | 6, 7 | Low | L | P0 | 2,500 |
| 9 | GPU profiler & VRAM overlay | D3D11 timestamp queries around each KSPVE pass via a small native plugin (shared with 19), frame-time graph, VRAM held by KSPVE render targets. | 1 | Low | M | P0 | 1,500 |
| 10 | Frame budget governor | Compares GPU frame time to target; steps effect quality or internal resolution with hysteresis so it doesn't oscillate. | 7, 9 | Free | M | P0 | 1,200 |
| 11 | Debug views | Fullscreen visualisations: depth, normals, G-buffer, motion vectors, exposure histogram, shadow atlas, probe and voxel contents, GI rays. | 1 | — | L | P0+ | 1,500 |
| 12 | Safe mode & diagnostics | An effect that throws or fails to compile disables itself, not the game. One-click diagnostic bundle (logs, config, mod list, GPU info) for bug reports. | 1 | — | L | P0 | 1,200 |
| 13 | Public modder API | Lets other mods register lights, GI emitters and material definitions, and read exposure/scene context. Versioned. | 1, 2, 56 | — | L | P9 | 1,200 |

**Subtotal: 22,600**

## 2. Performance & temporal (10)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 14 | Universal motion vectors | Per-object motion vectors for parts, kerbals, terrain quads and scaled-space bodies, written in a motion pass on each camera. Anything untrackable (other mods' custom shaders) gets camera-only motion from depth. Must be correct under Krakensbane, where the vessel is near-stationary and the world moves. | 1, 3 | Low–Med | H | P2 | 3,000 |
| 15 | Jitter & projection management | Halton sub-pixel jitter applied consistently to all cameras' projections each frame; unjittered matrices exposed to effects and to mods whose raymarchers would shimmer under jitter. | 1 | Free | H | P2 | 800 |
| 16 | Internal resolution control | Renders the whole camera stack into a lower-res target (e.g. 1440p for 4K output) so every mod's passes — EVE clouds, Scatterer, Parallax — get cheaper too, then upscales; UI composited at native resolution. The single biggest performance lever. | 1, 18 | Saves | H | P2 | 1,500 |
| 17 | TAA | Neighbourhood-clamped temporal AA with velocity-weighted history for when upscaling is off. Replaces Scatterer's TAA when both are present. | 14, 15, 20 | Low | M | P2 | 1,500 |
| 18 | Temporal upscaler + sharpening | FSR2-class open reconstruction (depth- and motion-aware, reactive masks for plumes, particles and transparents) plus contrast-adaptive sharpening. Runs on any DX11 GPU. | 14–16 | Med (net large saving) | H | P2 | 4,800 |
| 19 | DLSS backend (optional) | C++ native plugin using NVIDIA NGX on D3D11, fed the same inputs as 18; auto-selected on RTX cards, absent otherwise. NVIDIA SDK licence terms to check before public release. | 18, native plugin | Saves | M | P2 | 2,500 |
| 20 | History & resampling library | Shared reprojection, disocclusion detection, history clamping and bilateral up/downsampling used by AO, SSR, GI and shadows. | 14 | — | M | P2 | 1,600 |
| 21 | Frame scheduler & RT pool | Spreads expensive updates (cubemap faces, shadow-atlas tiles, voxel clipmap levels) across frames; pools and reuses render targets to cap VRAM on 8 GB cards. | 1 | Free | L | P2 | 1,500 |
| 22 | Shader prewarm | Warms shader variants during loading so the first use of an effect doesn't hitch mid-flight. | 4 | — | L | P2 | 500 |
| 125 | Texture filtering & LOD bias | Forces anisotropic filtering and applies a negative mip bias matched to the upscaling ratio, so textures stay sharp at lower internal resolution. | 16 | Free | L | P2 | 400 |

**Subtotal: 18,100**

## 3. Camera & post (17)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 23 | HDR pipeline | Float targets from the first camera through final composite, so sun, plumes and clouds keep real values until tonemapping. Coordinates ownership with Scatterer's own exposure/tonemapping. | 1, 5 | Free | M | P1 | 800 |
| 24 | Physical auto-exposure | Compute luminance histogram → EV100, with separate ranges and adaptation speeds for surface, orbit and IVA; eye adaptation when looking from a dark cabin to a sunlit window. | 23, 2 | Low | L | P1 | 1,200 |
| 25 | Tonemapping | AgX (default), ACES and a custom filmic curve, baked into one LUT. | 23 | Free | L | P1 | 700 |
| 26 | Colour grading & white balance | 3D LUTs, per-body grade presets, white balance from the illuminating star's colour temperature (Kopernicus stars, Kcalbeloh). | 25, 6 | Free | L | P1 | 1,000 |
| 27 | Energy-conserving bloom | Dual-filter mip chain weighted physically (no brightness-threshold hack), preserving total energy. | 23 | Low | L | P1 | 900 |
| 28 | Physically-based glare | Glare from the real aperture shape: FFT convolution in cinematic mode, precomputed kernel in play mode. Diffraction spikes on sun, stars and specular glints. | 27 | Low / Cine | L | P9 | 1,800 |
| 29 | Lens flares & imperfections | Occlusion-tested sun/star flares (switchable against Scatterer's sun flare), lens dirt, chromatic aberration, distortion. | 23, 5 | Low | L | P1 | 1,800 |
| 30 | Motion blur | Tile-max velocity, per-object + camera, shutter-angle based. | 14 | Med | M | P4 | 1,200 |
| 31 | Depth of field | Physical circle of confusion from focal length and aperture, bokeh gather, autofocus on the active vessel. | 23 | Med–High | L | P9 | 1,300 |
| 32 | Film emulation & camera presets | Grain, halation, vignette. Presets: onboard camera, long-lens tracking camera (heat haze + compression), IMAX. | 25 | Low | L | P9 | 1,700 |
| 33 | Lens droplets & frost | Rain and cloud droplets, frost, and airspeed-driven streaking on the camera lens or cockpit glass, from in-cloud, rain and temperature state. | 2, 5 | Low | M | P7 | 900 |
| 119 | Physical camera model | ISO, shutter speed and f-stop drive exposure, motion-blur length, depth of field and sensor noise together, like a real camera; auto modes choose them from the scene. | 24, 30, 31, 32 | Free | L | P1 | 900 |
| 120 | Local tonemapping | Local exposure (bilateral grid) keeps detail in both a sunlit hull and deep shadow, or a dark cabin and a bright window, the way real HDR photographs do. | 24, 25 | Low | L | P1 | 900 |
| 121 | Scotopic night vision | Human-eye mode: in very low light colour fades, hues shift blue (Purkinje effect) and fine detail softens. | 24 | Low | L | P9 | 500 |
| 122 | Sensor artifacts | CCD vertical smear and blooming from the sun, clipped-highlight colour, rolling shutter on fast motion; set per camera preset. | 32 | Low | L | P9 | 700 |
| 123 | Broadcast look | Optional video emulation for mission-control style shots: compression blocks, chroma subsampling, interlacing, sharpening halos. | 32 | Low | L | P9 | 600 |
| 127 | HDR display output (spike-gated) | Native HDR10/scRGB output for HDR monitors through the native plugin. If Unity 2019's swap chain can't be switched safely, fall back to a tonemapper mode tuned for Windows Auto HDR and RTX HDR. | 9, 25 | Free | H | P0 spike / P9 | 1,500 |

**Subtotal: 18,400**

## 4. Direct lighting & shadows (14)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 34 | Physical sun & star lights | Sun as a disk (area-light specular via representative point), intensity from real distance, colour from star temperature; multiple stars supported (Kcalbeloh). | 2 | Free | L | P3 | 900 |
| 35 | Soft sun shadows | Stabilised cascades fitted to vessel + terrain, PCSS penumbrae matching the sun's angular size, better bias/normal offset to remove acne and peter-panning. | 1, 3 | Med | M | P4 | 2,000 |
| 36 | Per-vessel shadow atlas | Each loaded vessel in range gets its own tight shadow frustum in an atlas tile sized by screen coverage, so ships kilometres apart keep crisp self-shadowing (reference shot B). | 35, 21 | Med | M | P4 | 1,800 |
| 37 | Contact shadows | Short screen-space ray march toward the sun per pixel for fine detail shadow maps miss: greebles, trusses, rivets. | 1 | Low | L | P4 | 700 |
| 38 | Long-range terrain shadows | PQS height sampled into a local heightmap and traced for mountain shadows at dawn and dusk beyond shadow-map range. Overlap with Scatterer's long-distance shadows to resolve. | 5, 35 | Med | M | P4 | 1,800 |
| 39 | Eclipse penumbra on vessels | Analytic sun-disk occlusion by spherical bodies applied to vessel lighting, multi-star aware. EVE/Scatterer handle planets; this keeps vessels consistent with them. | 34 | Free | L | P3 | 600 |
| 40 | Cloud shadows on vessels | Sample EVE cloud density along the sun ray above the vessel; also dims direct light while inside a cloud (reference shot A). | 5 (EVE) | Low | H | P3 | 1,000 |
| 41 | Planetshine area light | Planet as a large spherical area light: albedo from its texture, phase from sun angle, LTC evaluation, occluded by the vessel's own shadow map in the planet direction (reference shot B). Switchable against Scatterer planetshine. | 34, 36 | Low | M | P3 | 1,500 |
| 42 | Local light shadows | Part spotlights, landing lights and floodlights cast shadows through a shared spot-shadow atlas with budgeted updates. Coordinates with Deferred's light handling. | 21, 5 | Med | M | P4 | 1,500 |
| 43 | Plume & plasma light | Waterfall plumes and Firefly plasma emit dynamic lights (colour and intensity from plume or plasma state) onto parts and terrain, with shadows for the main engine light. | 5, 42 | Low–Med | M | P7 | 1,400 |
| 44 | Emissive surfaces as lights | Lit windows, glowing radiators, hot nozzles light nearby surfaces through the GI tiers, or cheap virtual point lights when GI is off. | 51 | Low | L | P5 | 900 |
| 101 | Physical light units & calibration | Everything in real units: sun illuminance derived from KSP's own solar flux (1,360 W/m² at Kerbin), lamps in lumens, emissives and screens in nits, plume and plasma brightness calibrated per Waterfall and Firefly template. Without this, one exposure can't be right for both sunlit hulls and night lights. | 34, 5 | Free | M | P1 | 1,500 |
| 113 | Vessel shadows on clouds & distant ground | The vessel's shadow falls onto EVE cloud tops (with a glory ring around it when the sun is behind the camera) and onto terrain from altitude, beyond normal shadow range. | 36, 40, 112 | Low–Med | H | P7 | 1,600 |
| 115 | Body-to-body light & night model | Moons light the surface at night by phase (a full Mun is bright enough to see terrain), planets light their moons' night sides (ashen glow), and a physically small airglow/starlight floor replaces stock's flat night ambient. | 41, 101 | Low | L | P3 | 1,200 |

**Subtotal: 18,400**

## 5. Global illumination & reflections (11)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 45 | Environment capture | Dynamic cubemap around the active vessel capturing scaled space, Scatterer sky, EVE clouds and terrain, updated face by face across frames, plus a low-res fast capture for rapid changes (reentry glow). Hard part: getting other mods' effects to render into a capture camera. | 1, 21, 5 | Med | H | P3 | 2,000 |
| 46 | Image-based lighting | GGX-prefiltered specular mips + L2 spherical-harmonics diffuse from 45, applied in Deferred's lighting (and forward shaders via 56); split-sum BRDF LUT; horizon occlusion. The core of reference shots A and B. | 45 | Low | M | P3 | 1,800 |
| 47 | Local reflection probes | Box-projected probes in hangars, KSC, bases and large stations so interiors don't reflect open sky. | 46 | Low | L | P5 | 1,300 |
| 48 | GTAO | Ground-truth ambient occlusion at half resolution with bent normals, temporal accumulation and specular occlusion. | 20 | Low–Med | L | P5 | 1,100 |
| 49 | Screen-space reflections | Hi-Z traced, half resolution, roughness-aware, temporally filtered, blended over the probe fallback. Deferred already ships Hi-Z SSR: extend or replace it, and disable its pass when ours is on. | 20, 46, 5 | Med | M | P5 | 1,600 |
| 50 | Shared denoiser | Spatio-temporal (SVGF / à-trous) denoiser reused by RTGI, SSR and soft shadows. | 20 | — | L | P5 | 1,200 |
| 51 | Screen-space RTGI (tier 1) | Cosine-distributed rays traced against Hi-Z depth; radiance taken from the previous frame's lit colour so bounces accumulate; misses fall back to IBL. Half resolution + denoise. | 20, 46, 50 | Med–High | M | P5 | 2,200 |
| 52 | World-space voxel GI (tier 2) | Clipmap voxelisation of active vessel(s) + nearby terrain into 3D radiance/opacity textures (geometry shader + UAV writes on DX11), refreshed a level per frame; traced for off-screen bounce, planetshine self-occlusion and IVA light. | 21, 51 | High | H | P5 | 4,500 |
| 53 | Irradiance probe volumes | DDGI-style probe grids around KSC, Kerbal Konstructs bases and landed bases, updated incrementally by tracing into the voxel/depth data. | 52 | Low | M | P5 | 2,500 |
| 54 | Path-traced photo mode (tier 3) | CPU-built BVH over vessel + nearby terrain meshes, compute-shader path tracer lit by the environment capture, accumulating hundreds of samples per pixel for a still, then denoised. | 45, 50, 96 | Cine | M | P9 | 3,500 |
| 55 | DXR backend (conditional) | If the P0 spike shows KSP runs on D3D12 with `SystemInfo.supportsRayTracing`, use Unity's experimental ray-tracing API for 52 and 54 rays. Otherwise dropped. | spike | — | H | P9 | 2,000 |

**Subtotal: 23,700**

## 6. Materials & surfaces (21)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 56 | Extended material framework | KSPVE uber-shader replacing part shaders (Shabby-style), writing to Deferred's G-buffer; auto-conversion of stock / ReStock / Near Future / BDB texture sets; per-part config overrides; specular anti-aliasing to stop 4K shimmer on greebles. | 5, 6 | Free | H | P6 | 2,800 |
| 57 | MLI foil | Anisotropic GGX with procedural crinkle normals; gold, silver and black variants for insulation blankets. | 56 | Free | L | P6 | 800 |
| 58 | Coatings | Painted metal with a clearcoat layer; thin-film iridescence for visors and coated optics. | 56 | Free | L | P6 | 900 |
| 59 | Glass & canopies | Fresnel reflections from 46/49, thin-surface refraction, grime and scratches, cabin reflections on the inside at night. | 56, 46 | Low | M | P6 | 1,300 |
| 60 | Solar panel material | Cell-grid microstructure, anisotropic cover-glass glint, angle-dependent reflectance from dark blue to mirror (reference shot B). | 56 | Free | L | P6 | 600 |
| 61 | Kerbal suits & visors | Fabric sheen BRDF on suits; visors reflecting the environment, with optional gold thin film. | 56, 58 | Free | L | P6 | 900 |
| 62 | Weathering & dust | Procedural edge wear and grime; dust building up on landed vessels over time and blown off by engine exhaust. | 56 | Free | L | P6 | 1,500 |
| 63 | Scorch & soot | Persistent heat-shield charring after reentry (from thermal and Firefly data), soot streaks near engines; saved with the vessel. | 56, 5 | Free | M | P6 | 1,400 |
| 64 | Blackbody heat glow | Parts and nozzles emit by temperature with physically correct blackbody colour; nozzle extensions glow with throttle and heat. Switchable against stock thermal glow. | 56 | Free | L | P6 | 700 |
| 65 | Cryo frost & ice shedding | Frost on cryogenic tanks (CryoTanks fill state) on the pad, ice chunks falling at liftoff, venting vapour. | 56, 5 | Low | M | P6 | 1,200 |
| 66 | Wet surfaces, rain streaks, icing | Wetness darkening and specular boost, rain streaks flowing with the airflow, leading-edge icing in cold clouds. EVE already applies wet-surface effects when Deferred is installed: extend it (streaks, icing) rather than duplicate its wetness. | 56, 40 | Low | M | P7 | 1,500 |
| 102 | Multi-scattering energy compensation | Rough metals keep the energy single-scatter GGX loses (Kulla–Conty style lookup), so rough metal doesn't render darker and dirtier than it should. | 56 | Free | L | P6 | 400 |
| 103 | Regolith BRDF (Hapke) | Airless bodies get lunar-style reflectance: opposition surge (brightening with the sun behind you), a flat full-moon disk, strong backscatter. Applied to terrain via a surface flag and to scaled-space bodies. | 56, 5 | Free | M | P6 | 1,200 |
| 104 | Subsurface scattering | Screen-space diffusion for kerbal skin, ice, snow and waxy materials. | 56 | Low | L | P6 | 1,200 |
| 105 | Thin-surface translucency | Parachutes, fabric, inflatables and the backs of solar panels glow when the sun is behind them. | 56 | Free | L | P6 | 800 |
| 106 | Machined-metal anisotropy | Spun (circular) and brushed anisotropy on nozzles, domes and tanks, oriented from part geometry. | 56 | Free | L | P6 | 500 |
| 107 | Stochastic glints | Tiny microfacet sparkles: snow, regolith, metal-flake paint, crinkled foil. | 56 | Low | L | P6 | 900 |
| 108 | Surface imperfection layer | Roughness breakup, smudges and micro-scratches that catch light at grazing angles. Perfectly uniform surfaces are what reads as CG. | 56 | Free | L | P6 | 1,000 |
| 109 | Heat tint on metal | Persistent temper colours (straw, blue, purple bands) on nozzles and exhaust-facing metal after firing. | 56, 63 | Free | L | P6 | 600 |
| 110 | Silhouette smoothing | PN-triangle tessellation on low-poly parts so round tanks and fairings have smooth outlines up close, faded out with distance. | 56 | Low–Med | M | P6 | 1,400 |
| 132 | Kerbal eyes & faces | Cornea refraction, wet specular highlights and eye occlusion, paired with skin subsurface scattering for close-up EVA shots. | 104 | Free | L | P6 | 700 |

**Subtotal: 22,300**

## 7. Atmosphere, weather & aero (11)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 67 | Vessel compositing in EVE volumetrics | Parts correctly fogged and occluded inside and behind volumetric clouds, and KSPVE's own transparents (plumes, particles, contrails) sorted against clouds (reference shot A). | 5 (EVE) | Low | H | P3 | 1,200 |
| 68 | Volumetric local lights | Froxel-based local scattering for nav, strobe and landing lights in cloud, fog and exhaust. | 42 | Med | M | P7 | 1,500 |
| 69 | Vapor cones | Prandtl–Glauert condensation around transonic vessels from Mach number, a humidity proxy (altitude, cloud proximity) and dynamic pressure. | 2 | Low | L | P7 | 900 |
| 70 | Wingtip vortices & wing condensation | Vortex trails at wingtips at high angle of attack in humid conditions; over-wing condensation in high-G manoeuvres. | 2 | Low | L | P7 | 900 |
| 71 | Contrails | Persistent trails from engines within a humidity/temperature altitude band, drifting with wind, lit using Scatterer's atmosphere parameters, fading over minutes. | 2, 5 | Low–Med | M | P7 | 1,800 |
| 72 | Heat haze | Screen-space refraction from exhaust, reentry heat and hot surfaces, driven by temperature. | 1 | Low | L | P7 | 700 |
| 73 | Airglow & night-side atmosphere | Thin emissive airglow layer (green and red) at the limb when seen from orbit at night, consistent with Scatterer's sky. | 5 | Low | M | P8 | 800 |
| 111 | Atmospheric refraction & scintillation | Setting sun flattened and reddened by refraction, green flash, stars twinkling and dimming near the horizon, stars refracted through the limb when seen from orbit. | 5 (Scatterer), 85 | Low | M | P7 | 1,300 |
| 112 | Atmospheric optics | Rainbows in rain, glories on cloud tops, 22° halos and sun dogs in ice cloud, cloud iridescence near the sun. Driven by EVE cloud type and sun angle. | 5 (EVE) | Low | M | P7 | 2,000 |
| 114 | Height fog & valley mist | Low ground fog pooling in valleys at dawn, burning off in sunlight, consistent with Scatterer's atmosphere. | 5, 20 | Low–Med | M | P7 | 1,500 |
| 131 | Plume & plasma light in clouds | Engine plumes and reentry plasma light up the clouds they pass through, e.g. a night launch glowing orange inside the cloud deck. Needs an EVE hook. | 43, 5 (EVE) | Med | H | P7 | 1,300 |

**Subtotal: 13,900**

## 8. Launch, reentry & ground effects (10)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 74 | Lit particles | All particle systems (SmokeScreen, stock, Waterfall where possible) receive sun shadows, IBL, planetshine and plume light instead of flat colour; soft particles against depth. | 46, 35, 43 | Med | M | P7 | 1,500 |
| 75 | Volumetric launch clouds | Raymarched density volumes for exhaust and deluge steam, directed by the pad's flame trench and deflector so clouds roll out sideways along the ground, then drift with the wind and linger for minutes. Animated 3D noise advection in Play mode; driven by the fluid simulation (#148) in Cinematic mode. Lit by plume, sun, sky and planetshine. | 43, 20, 148 | Med / Cine | M | P7 | 3,000 |
| 76 | Ground interaction | Dust, sand and snow kicked up under engines and rotor/prop wash, material chosen from biome and terrain. | 74 | Med | M | P7 | 1,600 |
| 77 | Dynamic decals | Deferred decals for pad scorch, landing-burn scorch, touchdown and tyre marks. Coordinates with KopernicusExpansion footprints. | 1 | Low | L | P7 | 1,300 |
| 78 | Ablation sparks & reentry debris | Glowing ablator particles shed from heat shields at peak heating, streaming into Firefly's plasma trail. | 74, 5 | Low | L | P7 | 900 |
| 124 | Transparency correctness | Weighted-blended order-independent transparency and better sorting for overlapping plumes, smoke, glass and contrails, so they stop popping in front of or behind each other. | 1, 67 | Med | M | P7 | 1,800 |
| 129 | Vacuum-correct ejecta | On airless bodies, regolith kicked up by engines flies in clean ballistic arcs and drops instantly with no billowing: the Apollo look. | 76 | Low | L | P7 | 1,200 |
| 130 | Venting & ice-crystal glitter | Propellant vents, boil-off and RCS puffs in orbit flash-freeze into glittering ice-crystal clouds that catch the sun. | 74, 65 | Low | L | P7 | 900 |
| 148 | GPU smoke simulation | 3D fluid simulation on compute shaders (advection, buoyancy, vorticity confinement) for launch clouds and large plumes in Cinematic mode, seeded by engine thrust and pad geometry. | 75, 20 | High / Cine | M | P7 | 3,500 |

**Subtotal: 17,900**

## 9. Space & celestial (12)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 80 | Relativistic black holes | Per-pixel null-geodesic integration (Schwarzschild, optional Kerr) inside a bounding sphere; lensed background from skybox + scaled-space capture; photon ring. Takes over black-hole objects from Singularity. | 45, 87 | Med (on screen only) | M | P8 | 2,800 |
| 81 | Volumetric accretion disks | Raymarched along the same curved rays: disk density with turbulence, temperature from a Novikov–Thorne-style profile → blackbody colour, Doppler beaming and gravitational redshift per sample. | 80 | High | M | P8 | 3,000 |
| 82 | Relativistic jets | Bipolar jets with synchrotron-like emission and relativistic beaming, config-driven. | 80 | Med | L | P8 | 1,200 |
| 83 | Wormholes | Kcalbeloh's WH3141 pair rendered through a traversable-wormhole metric with the same tracer, showing the destination's sky. | 80 | Med | M | P8 | 1,500 |
| 84 | Star surfaces & corona | Limb darkening, animated granulation, sunspots, corona visible during eclipses; per-star config for Kopernicus stars. | 34 | Low | L | P8 | 1,800 |
| 85 | Starfield & zodiacal light | Stars as HDR points with a realistic magnitude distribution so exposure naturally hides them in daylight and sunlit orbit; zodiacal light and gegenschein. | 24 | Low | M | P8 | 1,300 |
| 86 | Nebula objects | Nebulae as real world objects with a position, size and orientation: emission from ionised gas (H-alpha red, O-III teal, S-II), dust that absorbs and reddens light behind it, lit by embedded or nearby stars. From outside they are glowing clouds with correct parallax; flown into, they become local fog and glowing filaments around the vessel. | 140, 20 | Med | M | P10 | 4,500 |
| 87 | Galaxy & HDR skybox | HDR background with galactic band and dust lanes; converts existing LDR skyboxes (e.g. Kcalbeloh's) to calibrated HDR. | 24 | Low | M | P8 | 1,200 |
| 88 | Planetary rings | Forward-scattering lit rings, planet shadow on rings and ring shadow on planet and vessels, particle sparkle up close. Coordinates with Kopernicus rings. | 5, 35 | Low–Med | M | P8 | 1,800 |
| 90 | Comet tails | Curved sunlit dust tail and straight blue anti-sunward ion tail for KSP's comets. | 34 | Low–Med | L | P8 | 1,500 |
| 91 | Distant vessel impostors | Vessels beyond loaded range drawn as sunlit glints or impostors with correct brightness (reference shot B). | 24 | Low | M | P8 | 900 |

**Subtotal: 22,700**

## 10. Interiors, editor & KSC (5)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 92 | IVA lighting | Sunlight through windows with shadows, light shafts in cabin dust, GI bounce inside cockpits (tiers 1–2), cabin fixtures. Must coexist with FreeIva and RasterPropMonitor. | 51, 52, 42 | Med | M | P9 | 1,500 |
| 93 | Editor lighting | VAB/SPH lighting rigs with IBL, AO and soft shadows; studio preset for screenshots. | 46, 48 | Low | L | P9 | 1,200 |
| 94 | KSC & Kerbal Konstructs buildings | Material conversion and lighting (IBL, AO, probes) for KSC and KK statics. | 56, 47, 53 | Low | M | P9 | 1,500 |
| 95 | Night lighting at bases | Sodium and LED floodlights with volumetric glow, runway lights, light-pollution haze. | 42, 68 | Low–Med | L | P9 | 1,200 |
| 126 | Cockpit screen realism | RasterPropMonitor and IVA screens get calibrated emissive brightness, a subpixel grid up close, backlight bleed and glare in the glass. | 101, 59 | Free | M | P9 | 700 |

**Subtotal: 6,100**

## 11. Cinematic (2)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 96 | Photo mode | Pause with free camera, supersampled tiled high-res screenshots, EXR export, exposure bracketing, path-traced option via 54. | 23 | Cine | L | P9 | 2,800 |
| 97 | Cinematic camera system | Tracking cameras, launch-tower and pad cameras, keyframed camera paths, physically based camera shake. | 2 | — | L | P9 | 2,500 |

**Subtotal: 5,300**

## 12. Tooling & QA (4)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 98 | Benchmark scenes & flythroughs | Reference shots A and B plus launch, reentry and black-hole scenes as saves with scripted camera paths; logs per-pass GPU timings. | 9 | — | L | P0 | 1,500 |
| 99 | Capture & image-diff regression | Captures fixed frames from 98 and compares them to baselines with a perceptual diff to catch visual regressions. | 98 | — | L | P0 | 1,200 |
| 100 | Compatibility matrix automation | Launches KSP with mod subsets (with/without EVE, Scatterer, Parallax, Deferred…) and parses logs for errors and exceptions. | — | — | L | P0 | 1,000 |
| 128 | Lookdev kit | Grey ball, chrome ball and colour chart placeable in any scene, with side-by-side reference-photo comparison: the film-VFX way of proving lighting matches reality. | 11 | — | L | P0 | 1,200 |

**Subtotal: 4,900**

## 13. Terrain & ground surfaces (3)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 116 | Large-scale terrain sky occlusion | Horizon maps from PQS height: valleys and crater floors get less skylight and planetshine, ridges more. This is the large-scale darkening GTAO can't see. | 38, 46 | Low | M | P4 | 1,400 |
| 117 | G-buffer weather layer | After the G-buffer pass, modifies any deferred surface (terrain, buildings, parts) for rain wetness, puddles in low areas and snow on upward-facing surfaces, without touching Parallax's shaders. EVE already writes wet-surface effects into Deferred's G-buffer: KSPVE defers to EVE for wetness when present and adds puddles and snow. | 1, 5 (EVE), 6 | Low | M | P7 | 1,800 |
| 118 | Vegetation lighting | Leaf translucency when backlit, GI and AO on Parallax scatters (grass, trees), matched to wind. | 5 (Parallax), 48, 51 | Low–Med | H | P7 | 1,200 |

**Subtotal: 4,400**

## 14. Engine optimisation (7)

Scope: KSPVE optimises rendering (GPU and render-thread CPU) and, where its profiler proves a hotspot, targeted CPU paths. Physics and loading already have dedicated mods installed (KSPCommunityFixes, KSPBurst, KSPTextureLoader); KSPVE coordinates with them instead of duplicating them.

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 133 | Whole-frame profiler with mod attribution | CPU time per mod, measured by instrumenting Update, FixedUpdate, LateUpdate, OnGUI and camera callbacks per assembly with Harmony (on demand only), plus GPU time per camera and per KSPVE pass. Shows where a frame actually goes ("Scatterer 3.1 ms, EVE 2.4 ms, physics 5 ms") and drives every other optimisation. | 9 | Low (when on) | M | P0 | 2,500 |
| 134 | Part draw-call reduction | GPU instancing of identical part meshes and per-material mesh merging for large vessels, rebuilt on staging, damage and animation changes. Cuts render-thread cost at high part counts. Needs instancing-capable part shaders, hence after #56. | 1, 56 | Saves | H | P6 | 3,000 |
| 135 | Shadow caster culling | Culls shadow casters outside cascade relevance and drops tiny parts from far cascades and the per-vessel atlas; KSP's many small parts otherwise dominate shadow draw calls. | 35, 36 | Saves | M | P4 | 1,200 |
| 136 | Part and vessel LOD | Simplified meshes generated once per part (mesh decimation, cached to disk) used below a screen-size threshold, with impostors beyond that, so big stations and fleets stay cheap at distance. | 21 | Saves | M | P11 | 3,000 |
| 137 | Occlusion culling | GPU Hi-Z occlusion of KSC statics, Kerbal Konstructs bases and vessels hidden behind terrain or buildings. | 20 | Saves | M | P11 | 1,800 |
| 138 | Frame pacing & stutter diagnostics | Low-latency frame limiter with even frame delivery (replacing the vsync + cap combination), and frame-time spike detection with cause attribution (garbage collection, shader compile, scene load, a specific mod) from #133. | 133 | Free | L | P2 | 1,000 |
| 139 | Profiler-driven CPU optimisations | Targeted Harmony patches for hot paths #133 proves expensive in real scenes, coordinated with KSPCommunityFixes, KSPBurst and KSPTextureLoader so nothing is patched twice. Scope set by measurements, not guesses. | 133 | Saves | H | P11 | 3,000 |

**Subtotal: 15,500**

## 15. Space objects & world-building (8)

| # | Feature | How it works | Needs | Cost | Risk | Phase | LOC |
|---|---|---|---|---|---|---|---|
| 140 | Space-object framework | Shared foundation for fields, nebulae (#86) and clusters: config nodes (`KSPVE_ASTEROID_FIELD`, `KSPVE_NEBULA`, …), double-precision placement relative to a body, star or barycentre, rendering that hands over smoothly between the scaled-space and local cameras, LOD, culling and floating-origin handling. | 1, 3, 6 | — | M | P10 | 3,000 |
| 141 | Asteroid fields | Config-defined belts (tori around a body or star) and clumps. Rocks are GPU-instanced and each follows its own Kepler orbit computed on the GPU, so the belt actually moves. Three bands: full meshes near, impostors mid-range, a glowing dust band from far away and in scaled space. | 140, 142 | Med | M | P10 | 4,500 |
| 142 | Procedural rocks | Rock meshes and materials generated at load (noise-displaced shapes, craters; carbonaceous, silicate, metallic and icy types), cached, shared by fields and rings. | 56 | — | L | P10 | 2,000 |
| 143 | Asteroid proximity physics | Rocks within a few kilometres of a vessel get lightweight colliders; large ones become real KSP asteroid vessels you can grab or land on. Deterministic seeding keeps every rock in the same place across sessions. | 141 | Low | H | P10 | 2,500 |
| 144 | Nebula authoring tools | Procedural generator (noise stacks, shock fronts, pillars, cavities blown out by stars), a baked 3D-texture cache, and an in-game editor to place and tune nebulae. | 86 | — | M | P10 | 2,500 |
| 145 | Map-view integration | Nebulae, fields and clusters appear in map view and the tracking station with markers, and can be set as targets. | 140 | Low | M | P10 | 1,500 |
| 146 | Star clusters & distant galaxies | Open and globular clusters and background galaxies as positioned objects with correct parallax, for interstellar planet packs. | 140, 85 | Low | L | P10 | 1,800 |
| 147 | Dust & debris clouds | Interplanetary dust, comet debris trails and orbital debris fields as instanced particles that light up in forward scattering. | 140 | Low | L | P10 | 1,500 |

**Subtotal: 19,300**

---

## Line-of-code summary

| Module | Features | LOC |
|---|---|---|
| Core platform | 13 | 22,600 |
| Performance & temporal | 10 | 18,100 |
| Camera & post | 17 | 18,400 |
| Direct lighting & shadows | 14 | 18,400 |
| GI & reflections | 11 | 23,700 |
| Materials & surfaces | 21 | 22,300 |
| Atmosphere, weather & aero | 11 | 13,900 |
| Launch, reentry & ground | 10 | 17,900 |
| Space & celestial | 12 | 22,700 |
| Interiors, editor & KSC | 5 | 6,100 |
| Cinematic | 2 | 5,300 |
| Tooling & QA | 4 | 4,900 |
| Terrain & ground surfaces | 3 | 4,400 |
| Engine optimisation | 7 | 15,500 |
| Space objects & world-building | 8 | 19,300 |
| **Features** | **148** | **233,500** |
| Automated tests (math, config, geodesics, orbits, exposure, BRDFs, integration) | | ≈28,000 |
| Unity editor tooling (bundle build, material conversion, LUT/BRDF/geodesic/horizon-map generators, nebula baking) | | ≈7,000 |
| **Total** | | **≈268,500** (honest range 225k–320k) |

## Highest-risk items

1. **14–18 (motion vectors, jitter, upscaling)** — correct motion for everything across KSP's cameras under Krakensbane, and other mods' raymarchers under jitter.
2. **45 (environment capture)** — getting Scatterer and EVE to render into a capture camera.
3. **40, 67 (EVE integration)** — depends on internals of a mod KSPVE doesn't control.
4. **56 (material framework)** — replacing part shaders while staying compatible with Deferred and every part pack.
5. **52 (voxel GI)** — DX11 voxelisation cost and quality at KSP's scales.
6. **55 (DXR)** — may be impossible; decided by a cheap P0 spike, never a dependency.
7. **113, 131 (vessel shadows and plume light in EVE clouds)** — need EVE's cloud raymarch to sample KSPVE data; may require coordination with EVE's author.
8. **117 (G-buffer weather layer)** — writing into the G-buffer after Parallax's terrain pass must not break Parallax's own deferred output.
9. **127 (HDR display output)** — Unity 2019 has no HDR output in the built-in pipeline; decided by a P0 spike, with an Auto HDR–friendly fallback.
10. **134 (part draw-call reduction)** — instancing and merging must survive part animation, highlighting, staging and every part pack's shaders.
11. **139 (profiler-driven CPU patches)** — Harmony patches can collide with KSPCommunityFixes and other patchers; each patch needs a conflict check.
12. **143 (asteroid proximity physics)** — spawning colliders and asteroid vessels near the active vessel touches KSP physics and persistence.
