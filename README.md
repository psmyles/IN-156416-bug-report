# IN-156416: classic VFX Graph URP outputs skip URP shader keyword prefiltering

Classic Visual Effect Graph outputs, such as "Output Particle URP Lit Quad/Mesh/Octagon/Triangle" and "Output ParticleStrip URP Lit Quad", generate shaders without the `"RenderPipeline"="UniversalPipeline"` SubShader tag. URP only applies its build-time keyword prefiltering ("settings filtering") to shaders with that tag. The forward pass of every URP Lit output therefore enumerates its full 1,966,080-variant space on every player build, even when every variant it keeps is already in the shader cache.

In this sample, that is 19 s of a 24.8 s warm build. In our production project, with 150 such outputs, it was about 30 minutes of a 34-minute warm build.

## Environment

- Unity 6000.3.26f1, URP 17.3.0, Visual Effect Graph 17.3.0
- Windows 11, build target Windows 64-bit (Direct3D 11)
- A new URP project with the Visual Effect Graph **Learning Templates** sample imported. Nothing else was changed.
- Build scene: `Assets/Samples/Visual Effect Graph/17.3.0/Learning Templates/Scenes/URP_VFX_Learning_Templates.unity`. It contains 24 classic URP Lit outputs.

## Branches

| Branch | Contents |
|---|---|
| `main` | Stock packages. Reproduces the issue. Log: `BuildLogs/main-warm-build.log` |
| `workaround` | Visual Effect Graph embedded in `Packages/` with a one-line change that adds the tag. Logs: `BuildLogs/main-warm-build.log` and `BuildLogs/workaround-warm-build.log` |

## Steps to reproduce

1. Clone the repository and stay on `main`.
2. Build the player twice from the command line. The first build compiles every shader variant (about 6 minutes here); measure the second, warm build.

   ```
   "<Unity 6000.3.26f1 install>\Editor\Unity.exe" -batchmode -quit -timestamps ^
     -projectPath <path to this repo> -buildTarget Win64 ^
     -executeMethod BugReportBuild.BuildWindows -logFile <path>\build.log
   ```

   `Assets/Editor/BugReportBuild.cs` does three things:
   - Builds the Build Settings scenes to `Builds/Windows/`.
   - Force-reimports the VFX assets first, so each branch uses its own VFX Graph package when you switch branches in the same `Library`.
   - Writes lines starting with `[IN-156416]` to the log: the build duration, the duration of each build step, and how many generated VFX shaders have the tag.

   A build from **File > Build Profiles > Windows > Build** shows the same shader statistics, without the timestamps.
3. In the log, search for `Compiling shader "Hidden/VFX/`. For each URP Lit output, compare `Full variant space` with `After settings filtering` in the forward fragment pass.

## Expected result

As for every other URP shader, including the Shader Graph based VFX outputs in the same build, "After settings filtering" is much smaller than "Full variant space". Keywords for features the URP asset doesn't use should be removed before enumeration, for example probe volumes, DBuffer decals, rendering layers, and unused shadow and soft-shadow modes.

## Actual result

For all 24 URP Lit outputs, "After settings filtering" equals "Full variant space" (1,966,080). The variants are removed only afterwards, by built-in and scriptable stripping, and enumerating them takes 0.7–0.9 s per output on every build.

`BuildLogs/main-warm-build.log`, lines 9101–9107 (`Hidden/VFX/SampleMesh/Sample Mesh/Output Particle URP Lit Quad`, forward fragment pass):

```
2026-10-21T03:25:00.970Z|0x7cd4|  Pass "" (fp)
2026-10-21T03:25:01.627Z|0x7cd4|    Target graphics API: d3d11
2026-10-21T03:25:01.627Z|0x7cd4|    Full variant space:         1.966.080
2026-10-21T03:25:01.627Z|0x7cd4|    After settings filtering:   1.966.080
2026-10-21T03:25:01.627Z|0x7cd4|    After built-in stripping:   491.520
2026-10-21T03:25:01.627Z|0x7cd4|    After scriptable stripping: 16
2026-10-21T03:25:01.627Z|0x7cd4|    Processed in 0.66 seconds
```

The same pass on the `workaround` branch (`BuildLogs/workaround-warm-build.log`, lines 9102–9108):

```
2026-10-21T03:28:24.136Z|0x74f0|  Pass "" (fp)
2026-10-21T03:28:24.136Z|0x74f0|    Target graphics API: d3d11
2026-10-21T03:28:24.136Z|0x74f0|    Full variant space:         1.966.080
2026-10-21T03:28:24.136Z|0x74f0|    After settings filtering:   32
2026-10-21T03:28:24.136Z|0x74f0|    After built-in stripping:   8
2026-10-21T03:28:24.136Z|0x74f0|    After scriptable stripping: 8
2026-10-21T03:28:24.136Z|0x74f0|    Processed in 0.00 seconds
```

## Results: warm builds, same machine and Library

| | `main` (stock) | `workaround` (tag added) |
|---|---|---|
| Player build (`[IN-156416] Player build finished`) | **24.8 s** | **5.9 s** |
| Build step "Writing asset files" | 21.1 s | 2.1 s |
| Generated VFX shaders with the `RenderPipeline` tag | 15 of 100 | 100 of 100 |
| 24 URP Lit outputs: full variant space (all passes) | 47,961,776 | 47,961,776 |
| 24 URP Lit outputs: after settings filtering | 47,961,776 | 1,616 |
| 24 URP Lit outputs: variant preparation time ("Processed in", summed) | 19.0 s | 0.0 s |
| 24 URP Lit outputs: variants kept after stripping | 1,136 | 752 |
| Variants compiled / local cache hits (all shaders) | 0 / 4,250 | 0 / 3,616 |

Both builds compiled nothing: every kept variant came from the shader cache. The whole difference is time spent enumerating and stripping variants that settings filtering would have removed.

## Cause

- `com.unity.render-pipelines.universal/Runtime/Data/UniversalRenderPipelineAsset.cs`, line 456: URP's filter rules are restricted to tagged shaders:
  `[ShaderKeywordFilter.ApplyRulesIfTagsEqual("RenderPipeline", "UniversalPipeline")]`
- `com.unity.visualeffectgraph/Editor/Models/Contexts/VFXAbstractParticleOutput.cs`, line 725 (`additionalReplacements`): the SubShader tags for classic outputs are written without the pipeline tag:
  `Tags { "Queue"="..." "IgnoreProjector"="..." "RenderType"="..." }`
- Shader Graph based VFX outputs get the tag from the Shader Graph URP target, so they are filtered normally. They are the 15 tagged shaders on `main`.
- 6000.3 made the cost eight times larger. `VFXPassForwardLitAdditionalPragma` in `com.unity.render-pipelines.universal/Editor/VFXGraph/Shaders/VFXPasses.template` added `_DBUFFER_MRT1/2/3` and `RenderingLayers.hlsl` (`_WRITE_RENDERING_LAYERS`). That took the forward fragment space from 245,760 (6000.0.67f1) to 1,966,080.

## Workaround (branch `workaround`)

The branch embeds Visual Effect Graph 17.3.0, copied from the 6000.3.26f1 editor without `Documentation~` and `Samples~`. It changes only line 725 of `VFXAbstractParticleOutput.cs`: under URP, the generated tags now begin with `"RenderPipeline"="UniversalPipeline"`. The existing URP filter rules then apply to all classic VFX outputs.

Suggested fix: have the SRP binder supply the pipeline tag (`VFXURPBinder` → `UniversalPipeline`) and write it into `${VFXShaderTags}`.

## Notes

- `BuildLogs/*.log` are the second (warm) builds. The first build after a clone compiles all variants; it took about 6 minutes on this machine.
- Every build rewrites the URP asset's `m_Prefilter*` fields and a few other settings. That is normal URP behaviour. Those changes were not committed.
