# StarField

Deterministic, procedural star-field generation: grid cutting → per-cell hashing →
star masking → per-star twinkle, plus a streaming viewport (StarView) and a
smooth-cruise WinForms demo viewer.

All algorithms are pure and deterministic: the same parameters always produce the
same output — no RNG state, no textures, no engine dependencies (plain C#).

## Pipeline

| Step | Function | Output |
|---|---|---|
| ① Grid cutting | `StarCut.Value` / `StarCut.Render` | Uniform square cells, each with a linear gradient in one consistent direction. Any two samples across a cut line differ by exactly 0.5 (never equal). |
| ② Per-cell hash | `StarCut.CellHash` | Deterministic hash per cell coordinate (0..1). |
| ③ Star qualification | `StarCut.RandomBW` / qualify threshold | Cells whose hash exceeds `qualifyThreshold` may host a star. |
| ④ Star mask | `StarCut.StarMask` | In-cell normalized coordinates + step function; hash decides the star radius gate. `Square` = pixel block, `Circle` = dot. |
| ⑤ Full field | `StarField.Generate` | Integrates ①–④; non-star pixels stay black. |
| ⑥ Twinkle | `StarField.Twinkle*` | Each star's brightness follows a triangle wave whose period/phase derive from its cell hash (0.8–2.6 s default). |
| ⑦ Streaming viewport | `StarView` | 4 cells resident (current/up/left/up-left); moving NW promotes the former up-left cell; incremental 3 load / 3 unload. |

## Layout

```
StarField/
├── src/                  # core library (plain C#, no engine dependency)
│   ├── StarCut.cs        # cutting, hashing, star masking
│   ├── StarField.cs      # integrated generator + twinkle + StarAt query
│   └── StarView.cs       # streaming viewport arithmetic
├── tests/                # console verification suite
├── examples/             # PNG output example
└── viewer/               # WinForms smooth-cruise demo (1080P, 5px/s, twinkle)
```

## Build & run (Windows, .NET Framework)

```bat
rem Tests
csc /nologo /out:StarFieldTests.exe src\StarCut.cs src\StarField.cs src\StarView.cs tests\StarFieldTests.cs
StarFieldTests.exe

rem PNG example
csc /nologo /r:System.Drawing.dll /out:StarFieldExample.exe src\StarCut.cs src\StarField.cs examples\Program.cs
StarFieldExample.exe output

rem Interactive viewer
csc /nologo /target:winexe /out:StarFieldViewer.exe src\StarCut.cs src\StarField.cs src\StarView.cs viewer\StarFieldViewer.cs
StarFieldViewer.exe
```

## Key parameters (`StarField.Default`)

| Param | Default | Meaning |
|---|---|---|
| `cellW/cellH` | 40 | Cell size in star pixels |
| `qualifyThreshold` | 0.987 | Cells with hash above this host stars (higher → fewer stars) |
| `minRadius/maxRadius` | 0.02 / 0.05 | Star radius gate range (normalized to cell half-size) |
| `shape` | Square | Star shape |
| `seed` | — | Determinism seed (same seed → identical field) |

## License

MIT — see [LICENSE](LICENSE).
