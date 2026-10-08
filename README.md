# Volumetric Blackhole Fx

Relativistic, ray-marched black holes for Kerbal Space Program 1.12.

Every pixel near a black hole is traced along a curved light path through the hole's gravity. Light bends around the shadow, and a thin photon ring forms at its edge. The disk around it is a thick, turbulent, self-shadowed volume of glowing gas: Doppler beaming makes the side coming toward you blaze while the far side dims and reddens. Relativistic jets run along the spin axis.

Everything is live-editable in game: open the editor with **Ctrl+Alt+V**.

## Features

- **Gravitational lensing.** Light rays are integrated through the Schwarzschild metric, so the far side of the disk shows above and below the shadow, and a photon ring sits at its edge.
- **A volumetric accretion disk.** Puffy, clumpy and turning as a whole. Its temperature falls off outward, shifted by gravitational redshift and Doppler beaming. Sparks of white-hot matter and dark debris ride in it.
- **Relativistic jets.** Spiral-cored columns of gas streaming outward at a set fraction of light speed. The approaching jet is beamed brighter. They can also sweep round the spin axis like a pulsar's beams.
- **Colour.** Real blackbody colours from deep ember to blue-white, or any custom colour. There are presets (Gold, Blue, White-hot, Ember, Violet, Emerald, Crimson, Ice) and RGB sliders for the disk and the jets.
- **The disk is physical.** Ships inside it are dragged toward the gas's orbital speed and buffeted by turbulence, and the disk's glow lights nearby vessels.
- **Planets and moons in front of a black hole hide it properly.**

## Requirements

- KSP 1.12.x on Windows, running **DirectX 11** (KSP's default on Windows; OpenGL and Metal are not supported).
- [ModuleManager](https://github.com/sarbian/ModuleManager).
- A planet pack with a black hole. Configs ship for **Kcalbeloh System** (Kcalbeloh) and **KSS2** (Kerr), and you can add your own (below). [Singularity](https://github.com/LGhassen/Singularity) is recommended for background lensing; this mod switches off Singularity's flat disk on the holes it draws.

## Installing

Use CKAN, or download the latest zip from [Releases](https://github.com/Vihaan2012-cmyk/Volumetric-Blackhole-Fx/releases) and copy its `GameData/VolumetricBlackholeFx` folder into your KSP `GameData` folder.

Don't install it alongside KSPVE: both would draw the same black holes.

## The editor (Ctrl+Alt+V)

- **All black holes:** a master switch, a brightness multiplier for every disk, and the render scale (0.5 draws at half resolution, about 4x faster).
- **Each black hole has four tabs:**
  - **Colour:** presets, temperature, colour strength, brightness, glow, photon ring, and a custom disk colour with RGB sliders.
  - **Disk:** inner and outer edge, thickness, turbulence, sparks, debris, spin speed and direction, tilt, and the hole's size.
  - **Jets:** strength, length, width, speed, spiral, swirl, flow, absorption, colour, and the pulsar-style sweep.
  - **Ships & quality:** light on ships, disk drag (pull, grip, buffeting), and ray-march quality.

Changes apply immediately. **Save** writes them to `GameData/VolumetricBlackholeFx/PluginData/user.cfg`, which is laid over the configs at the next start. **Reset** returns a hole, or everything, to its config.

## Adding a black hole

Add a `VOLUMETRIC_BLACKHOLE` node naming a body; any key you leave out takes its default. Distances are in horizon radii: the innermost stable orbit is 3 and the photon sphere 1.5.

```
VOLUMETRIC_BLACKHOLE
{
    body = MyBlackHole         // the body's name (or set findByMass = True to use the heaviest body)
    horizonRadius = 0          // metres; 0 = the body's own radius
    diskInner = 3
    diskOuter = 14
    diskThickness = 0.12
    peakTemperature = 6000     // K: 3000 ember, 6000 gold, 12000 white, 40000 blue
    colorSaturation = 2.2
    tint = 0                   // 0..1: how much of tintRed/Green/Blue replaces the blackbody colour
    brightness = 0.9
    jets = 1.3                 // 0 = no jets
    jetLength = 60
    normalX = 0                // the disk's axis in the inertial frame (Y = up from the planets' plane)
    normalY = 1
    normalZ = 0
}
```

See `GameData/VolumetricBlackholeFx/Config/VolumetricBlackhole_Kcalbeloh.cfg` for every key. The mod-wide settings are in `VolumetricBlackholeFx.cfg`.

## Building from source

- .NET SDK 8 (the projects target .NET Framework 4.8, KSP's runtime).
- Unity 2019.4.18f1 (KSP's Unity version) to build the shader bundle.

```
tools\build.ps1           # plugin + unit tests (set KSPDIR, or Directory.Build.local.props, to your KSP install)
tools\build-shaders.ps1   # shader bundle (set UNITY_EXE if Unity is elsewhere)
tools\package.ps1         # GameData folder + out\VolumetricBlackholeFx-<version>.zip
tools\deploy.ps1          # copy into your KSP install
```

## Licence

GPL-3.0; see [LICENSE](LICENSE).
