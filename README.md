# StarField

确定性、程序化星空生成：网格切分 → 逐格哈希 → 星星掩码 → 逐星闪烁，外加流式视口（StarView）与平滑巡航的 WinForms 演示查看器。

全部算法均为**纯函数且确定性**：相同参数永远产生相同输出——无 RNG 状态、无纹理、无引擎依赖（纯 C#）。

## 处理管线

| 步骤 | 函数 | 输出 |
|---|---|---|
| ① 网格切分 | `StarCut.Value` / `StarCut.Render` | 均匀方形单元格，每个单元格内含方向一致的一条线性渐变。任意横跨切线的两个采样点差值恒为 0.5（永不相邻同色）。 |
| ② 逐格哈希 | `StarCut.CellHash` | 按单元格坐标计算的确定性哈希（0..1）。 |
| ③ 星星资格 | `StarCut.RandomBW` / 资格阈值 | 哈希超过 `qualifyThreshold` 的单元格才有资格容纳星星（阈值越高 → 星越少）。 |
| ④ 星星掩码 | `StarCut.StarMask` | 单元格内归一化坐标 + step 函数；哈希决定星星半径门槛。`Square` = 方块像素，`Circle` = 圆点。 |
| ⑤ 完整星场 | `StarField.Generate` | 整合 ①–④；非星像素保持黑色。 |
| ⑥ 闪烁 | `StarField.Twinkle*` | 每颗星星的亮度按三角波变化，周期/相位由该格哈希推导（默认 0.8–2.6 秒）。 |
| ⑦ 流式视口 | `StarView` | 常驻 4 格（当前/上/左/左上）；向左上移动时晋升原左上格为当前格；增量 3 载入 / 3 卸载。 |
| ⑧ 种子编解码 | `SeedCodec` | 13 位种子编码（字母 + 数字）。相同编码 → 相同确定性哈希 → 完全相同的星场。 |

## 目录结构

```
StarField/
├── src/                  # 核心库（纯 C#，无引擎依赖）
│   ├── StarCut.cs        # 切分、哈希、星星掩码
│   ├── StarField.cs      # 集成生成器 + 闪烁 + StarAt 查询
│   ├── StarView.cs       # 流式视口算法
│   └── SeedCodec.cs      # 13 位种子编码校验 + 确定性哈希
├── tests/                # 控制台验证测试套件
├── examples/             # PNG 输出示例
└── viewer/               # WinForms 平滑巡航演示（1080P、5px/s、闪烁、种子输入）
```

## 构建与运行（Windows，.NET Framework）

**一键启动查看器** — 双击根目录的 `run_viewer.bat`：自动编译源码并启动交互演示。也可以使用下方的原始命令：

```bat
rem 测试
csc /nologo /out:StarFieldTests.exe src\StarCut.cs src\StarField.cs src\StarView.cs src\SeedCodec.cs tests\StarFieldTests.cs
StarFieldTests.exe

rem PNG 示例
csc /nologo /r:System.Drawing.dll /out:StarFieldExample.exe src\StarCut.cs src\StarField.cs examples\Program.cs
StarFieldExample.exe output

rem 交互查看器（输入 13 位种子编码 → 重新生成底图）
csc /nologo /target:winexe /out:StarFieldViewer.exe src\StarCut.cs src\StarField.cs src\StarView.cs src\SeedCodec.cs viewer\StarFieldViewer.cs
StarFieldViewer.exe
```

## 种子编码

种子是 13 位字符串，每位为字母或数字（`0-9A-Za-z`，共 36^13 种可能）。`SeedCodec.Hash` 将任意合法编码映射为确定性的 32 位哈希，作为生成器的种子——**相同编码永远产生完全相同的星空**。在查看器中输入编码并点「生成底图」（或按回车）。

## 关键参数（`StarField.Default`）

| 参数 | 默认值 | 含义 |
|---|---|---|
| `cellW/cellH` | 40 | 单元格尺寸（星像素） |
| `qualifyThreshold` | 0.987 | 哈希高于此值的单元格容纳星星（越高 → 星越少） |
| `minRadius/maxRadius` | 0.02 / 0.05 | 星星半径门槛范围（归一化到单元格半宽） |
| `shape` | Square | 星星形状 |
| `seed` | — | 确定性种子（相同种子 → 相同星场） |

## 查看器控件与参数

窗口顶部参数栏（HUD，均为黑色提示文本）：

| 控件 | 说明 |
|---|---|
| 种子(13位) | 输入 13 位编码，点「生成底图」重建星场 |
| 呼吸系数 | 闪烁幅度（1 = 原闪烁，0 = 常亮） |
| 偏移系数 | 星星在格内二维全向偏移量（0–0.45，永不越格） |
| 哈希阈值 | 资格阈值，越高星越少；=1 时全灭 |
| 抑制系数 | 亮格对周围 3×3 邻域的闪烁抑制强度 |
| 帧率 | 目标帧率（5–60，默认 30），平滑巡航 5px/s |

> 画布上叠加的 anchor/seed/render 标签为浅色（叠加于半透明黑底、黑画布之上，黑字不可见）。

## 许可证

MIT — 见 [LICENSE](LICENSE)。
