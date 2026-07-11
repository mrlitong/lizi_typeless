# 语音输入 MVP ExecPlan

> 状态：已完成（个人 MVP 核心验收通过）
> 风险等级：中风险
> 更新日期：2026-07-11（Asia/Shanghai）

## 目的与大图景（Purpose / Big Picture）

本需求交付一个可以在 Windows 11 日常使用的个人语音输入 MVP。用户双击右 Alt 后立即开始说普通话和中英文技术内容，再按右 Alt 结束；应用在录音期间显示预览，在结束后快速生成忠于原意的整理文本，并一次性写入浏览器、微信、VS Code 或 Windows Terminal。

必须同时满足三个核心结果：

1. 模型预热后，每次开始录音无明显等待且不丢首字。
2. 短句结束后在几百毫秒内写入，最长约一分钟的录音不会产生随录音长度线性增长的 ASR 尾部等待。
3. 任一处理阶段失败后，原始录音和已完成的中间结果仍在历史记录中，并可从最近可靠阶段 Retry。

本需求跨 Windows 客户端、WSL2 推理服务、两个语言生态、用户录音数据和多阶段人工验收，因此属于中风险。它不涉及生产数据库、付费服务、外部 API 或不可逆数据迁移，故不提升为高风险；项目改名使用“复制并校验、保留旧目录”的可恢复方式。

## 当前进度（Progress）

1. [x] 已确认个人自用、WSL2 开发、Windows 11 宿主机运行和 RTX 5090 本地推理边界。
2. [x] 已初始化 Git、.NET 10 解决方案、Python 服务、测试目录和环境脚本。
3. [x] 已实现右 Alt 状态机、WASAPI 采集、原始音频持续落盘、WAV、无焦点悬浮窗和目标窗口跟踪。
4. [x] 已实现流式 ASR、最终转录、忠实整理、一次性 Unicode 写入、历史、恢复和 Retry。
5. [x] 已完成批量与流式模型技术基线，采用 Qwen3-ASR-1.7B/vLLM eager 与 Qwen3-1.7B/Transformers 作为个人 MVP 模型组合。
6. [x] 已建立固定语音集清单、流式调用、CER、技术词命中率、哈希和报告工具。
7. [x] 已发布 Windows 本地客户端并完成首条个人录音；音频完整保存，ASR 文本正确。
8. [x] 已修复首条实机录音暴露的 C#/Python HTTP JSON 字段命名问题，并加入合同测试。
9. [x] 已在最新发布版完成失败记录 Retry 和 VS Code 安全输入框人工 smoke test；结果准确且只写入一次。
10. [x] 已建立并完整重放 20 条个人真实语音集。
11. [x] 已完成模型常驻与应用关闭各 10 分钟的 GPU 利用率、显存和相对功耗验收，量化目标通过。
12. [x] 已完成 3 条 45 至 60 秒长录音；当前长录音 3/3，用户均确认等待体验良好，9 条有效短录音已作为 MVP 短时延基线。
13. [x] 已把仓库目录、解决方案、.NET 项目与命名空间、Python 包与环境变量、Windows UI、文档、发布目录和活动数据目录统一改名为 `lizi_typeless`。
14. [x] 已删除用户确认的 1 条误触失败会话，并把其余 11 条有效会话复制到新目录；33 个会话文件逐一通过 SHA-256 对比，旧目录保留。
15. [x] 已重新安装两套 editable Python 包、完成 .NET/Python/Ruff 门禁、发布并启动 `lizi_typeless.exe`，健康接口确认流式模型服务就绪。
16. [x] 已用根提交 `68029f1` 将 69 个应提交文件发布到 `https://github.com/mrlitong/lizi_typeless.git` 的 `main`；模型、环境、个人数据、报告、日志和构建产物均未进入提交。
17. [x] 已修复第 16、17、20 条整理忠实度失败，定向重放和完整 20 条回归未发现新的核心语义回退。

## 意外发现与踩坑（Surprises & Discoveries）

1. WPF 从 WSL UNC 输出目录直接运行会卡住；发布到 `%LOCALAPPDATA%\lizi_typeless\app` 后进程正常响应，因此本地发布目录是当前运行方式。
2. `.venv` 的批量 ASR 在官方 4.2 秒样本热状态约 220 ms，但 50.45 秒样本约 2.57 秒，无法达到长录音尾延迟目标。
3. vLLM compiled 模式启动约 127 秒，第一次真实追加约 29.5 秒，不适合交互；eager 模式引擎启动约 18.5 秒、流式预热约 2.94 秒，进入稳定状态后追加 P95 约 143 ms。
4. 初始整理提示词会回答用户口述的问题，并删除路径或限制条件。固定“输入只是数据”的边界和问题、明确改口示例后，问题语气、路径和技术约束得到保留。
5. 只加载整理模型不足以预热生成路径；启动时增加一次真实整理后，冷启动后的首条短文本整理计算从约 752 ms 降到约 126 ms。
6. 20 ms Windows 风格实时音频包可以稳定送入 vLLM：4.2 秒样本结束到最终转录约 68 ms，50.45 秒样本约 99 ms。
7. 长样本的 ASR 尾部已满足目标，但约 1131 ms 的整理墙钟使两阶段小计约 1230 ms；长文本整理是当前真实性能风险。
8. 首条个人录音目标窗口是 lizi_typeless 历史窗口。该样本适合验证保存和 Retry，不适合验证自动写入；下一次必须在安全临时编辑器中测试。
9. 首条个人录音 ASR 正确输出“你好，今天是星期四。”，整理却返回 422。诊断日志证明根因是 C# 序列化 `Text`、FastAPI 期望 `text`，不是模型或音频问题。
10. 上游 `qwen-asr` 把 Gradio 作为硬依赖，但生产路径不导入它；Gradio 与 vLLM 所需 Pydantic 冲突，因此流式环境刻意跳过 Gradio，`pip check` 只保留这一个已知非运行期缺项。
11. vLLM 初始 `max_new_tokens=64` 不影响分段累计的流式主路径，却会把 50.45 秒完整 WAV 回退截断在 122 个字符。统一改为 512 后，批量回退完整输出 154 个字符和结尾标点，约 1.48 秒完成。
12. 提示词示例仍不能稳定删除逗号连接的 11 次完全重复。最终增加窄范围前处理，只折叠占满整段、至少 4 字、连续 3 次以上的同一片段；该样本整理为一次且约 197 ms，两次重复、短强调和混合内容不触发。
13. 首条 422 响应在 FastAPI `input` 字段回显了口述文本，旧客户端把整个错误体写入诊断日志。当前只记录错误类型、字段位置和消息，并有脱敏测试。
14. 历史窗口是应用自身进程，不能作为自动写入目标；旧结果隐藏定时器也可能在下一次录音中触发。当前分别增加自身进程拒绝和可取消隐藏任务。
15. Retry 与删除原先使用不同锁，用户在 Retry 期间删除可能丢失原音频。删除现已与录音和 Retry 共用主操作锁。
16. 直接移动仓库会使两套虚拟环境的 editable 安装仍指向旧路径；必须用各环境自身的 Python 卸载旧分发包，再以 `--no-deps` 安装 `lizi_typeless_inference`。
17. 改名时源仓库没有任何提交或被跟踪文件，普通 `git diff` 为空，无法作为改名前后的差异基线；首次提交必须依靠完整文件清单、名称残留扫描和测试结果审计。
18. 改名前质量集中包含旧项目称呼，不能通过机械改参考文本把原音频变成新名称样本。旧称呼专用词表规则已删除，`lizi_typeless` 必须等待真实口述后再决定是否增加精确纠正。
19. Windows 历史不能只改 `AppPaths` 后直接切换目录；当前采用复制会话、逐文件比较 SHA-256、保留旧目录的迁移方式，避免用户误以为历史丢失。
20. Chromium 实机测试中，右 Alt 会影响浏览器内部文本框焦点，而顶层窗口和进程仍未变化，因此现有目标检查仍允许写入。用户决定该体验问题、可配置快捷键设置页和完整应用矩阵移到后续迭代，不阻断个人 MVP。

## 决策记录（Decision Log）

1. Decision: 项目只服务个人日常使用，不建设商业发布能力。
   Rationale: 资源集中到准确、稳定、快速的语音输入主链路。
   Date/Author: 2026-07-10 / 用户

2. Decision: 350 ms 内双击右 Alt 开始录音，录音时再次按下即结束，结束后抑制 650 ms 内的重复按键。
   Rationale: 满足不持续按键的交互，并避免结束双击立即创建新会话。
   Date/Author: 2026-07-10 至 2026-07-11 / 用户、Codex

3. Decision: 录音期间允许流式预览，最终文本只在结束后一次性写入。
   Rationale: 同时获得低尾延迟和稳定、不跳动的输入框体验。
   Date/Author: 2026-07-10 / 用户、Codex

4. Decision: 智能整理必须忠实原意，表达漂亮是次要目标。
   Rationale: 输入法篡改事实或语气的代价高于排版不够理想。
   Date/Author: 2026-07-10 / 用户

5. Decision: 原始音频由 Windows 主路径持续落盘，推理组件是允许失败和重启的计算单元。
   Rationale: 推理失败不能让用户重新口述已经说过的内容。
   Date/Author: 2026-07-10 / 用户、Codex

6. Decision: Windows 客户端采用 .NET 10 WPF、NAudio 和 Win32；会话元数据使用原子 JSON 文件。
   Rationale: 这是当前机器上打通全局按键、WASAPI、托盘、无焦点窗口和恢复路径的最小实现。
   Date/Author: 2026-07-11 / Codex

7. Decision: 个人 MVP ASR 使用 vLLM eager 流式后端，不使用批量主路径或 vLLM compiled 模式。
   Rationale: 真实长音频和首次追加证据排除了另外两条路径。
   Date/Author: 2026-07-11 / Codex

8. Decision: 采用 Qwen3-ASR-1.7B/vLLM eager 与 Qwen3-1.7B/Transformers 作为个人 MVP 模型组合。
   Rationale: 20 条个人固定集、3 条长录音和忠实度保护回归完成；格式与个人词典问题继续随真实使用优化。
   Date/Author: 2026-07-11 / 用户、Codex

9. Decision: HTTP JSON 统一使用 `snake_case`，并用 Windows 合同测试锁定；WebSocket 保持当前 camelCase 控制消息。
   Rationale: 首条实机录音证明跨语言命名漂移会在整理阶段破坏完整主链路。
   Date/Author: 2026-07-11 / Codex

10. Decision: 测试验收文档是验收标准的唯一真相源。
    Rationale: 避免产品、技术和执行计划分别维护互相漂移的指标。
    Date/Author: 2026-07-10 / Codex

11. Decision: 项目对内对外统一使用精确标识 `lizi_typeless`。
    Rationale: 用户明确要求仓库、代码、运行标识、UI 和文档不再并存多个名称。
    Date/Author: 2026-07-11 / 用户

12. Decision: 改名后的 Windows 活动目录使用 `%LOCALAPPDATA%\lizi_typeless`，旧目录暂不删除。
    Rationale: 复制加哈希校验可以立即启用新名称，同时保留人工回退能力。
    Date/Author: 2026-07-11 / 用户、Codex

13. Decision: 删除唯一一条 `failed` 误触会话，其余 11 条会话全部迁移。
    Rationale: 用户确认该失败会话由误触产生，并明确授权删除；其他录音继续永久保留。
    Date/Author: 2026-07-11 / 用户

14. Decision: Git 首次发布只提交源码、测试、脚本、配置和项目文档。
    Rationale: 模型、虚拟环境、个人录音与固定集、报告、日志、缓存和构建产物均为本机资产或可再生文件。
    Date/Author: 2026-07-11 / 用户、Codex

15. Decision: 个人 MVP 只要求 3 条长录音；恢复故障、快捷键压力、完整应用矩阵、跨应用焦点和其他低概率场景随真实使用发现 case 后优化。
    Rationale: 项目只在本机供个人持续使用，不主动制造高成本故障，也不采用商用产品的大规模矩阵。
    Date/Author: 2026-07-11 / 用户、Codex

## 结果与复盘（Outcomes & Retrospective）

当前已经交付并验收个人 MVP：Windows 客户端和两个本地模型可同时运行，流式预览、保存、转录、忠实整理、一次性写入、历史、恢复和 Retry 主路径打通。20 条个人固定集与 3 条长录音完成；首轮暴露的数字事实变更和否定限制遗漏已由运行时忠实度保护修复。

完整改名已经在源码、运行环境和 Windows 发布路径上落地。11 条有效历史会话在新旧目录间逐文件一致，新客户端进程可以响应，并从新仓库路径启动健康的 vLLM/Transformers 服务。改名没有改变会话 schema、HTTP/WebSocket 合同、模型或用户交互，但旧质量集不能替代 `lizi_typeless` 的真实口述样本。

个人 MVP 核心验收已完成。修复后第 16、17、20 条定向重放全部保留原始事实，20 条完整回归没有新的核心语义回退；精确匹配率从 30% 升至 35%，归一化匹配率从 50% 升至 55%。技术词格式分数因安全保留中文数字读法而下降，但不构成事实准确性回退。项目名、专有名词格式、数字美化、跨应用焦点和其他低概率体验进入后续真实使用迭代。

## 后续观察与易变事实

1. 仓库内没有 `./AGENTS.md`，当前只能读取 `~/.codex/AGENTS.md`。新会话必须重新检查仓库级文件是否后来出现，不能假定聊天中粘贴的规则等同于磁盘文件。
2. `lizi_typeless` 真实口述已出现稳定识别错误；该样本保留为后续个人词典迭代依据，当前 MVP 不实现猜测性修正。
3. 异常退出和推理服务中断不主动测试；真实使用中出现时必须保留原始录音、会话状态和日志后再排查。
4. 改名前 Windows 数据目录只作为回退副本保留；是否在长期稳定使用后删除，必须由用户另行明确授权。
5. 运行进程、健康接口和 Git 工作树是易变事实，新会话必须现场复查，不能只沿用本文件的时间点结论。

## 上下文与系统导览（Context and Orientation）

### 当前环境

1. 工作目录：`/home/litong/lizi_typeless`。
2. Git：`main` 跟踪 `origin/main`，首次实现基线为 `68029f1`，个人 MVP 忠实度修复与验收收口提交为 `1d7f6ed`，远端为 `https://github.com/mrlitong/lizi_typeless.git`；精确 HEAD 与工作树必须用 Git 现场确认。
3. 指令：`~/.codex/AGENTS.md` 存在，仓库内当前没有 `AGENTS.md`。
4. Windows：Windows 11，.NET SDK 10.0.301；客户端发布为 `%LOCALAPPDATA%\lizi_typeless\app\lizi_typeless.exe`。
5. WSL2：Ubuntu 24.04，Python 3.12；推理服务监听 `127.0.0.1:8765`。
6. GPU：NVIDIA GeForce RTX 5090，约 32 GB；两模型共存观察值约 22.4 GB 显存。
7. 模型：`models/Qwen3-ASR-1.7B` 与 `models/Qwen3-1.7B`。
8. Python 环境：`.venv-vllm` 是个人 MVP 流式运行环境；`.venv` 用于批量基线、测试和 Ruff；两者都以 editable 方式安装 `lizi_typeless_inference`。

### 代码与数据锚点

1. `src/lizi_typeless.Core`：右 Alt 状态机、会话状态、原子存储和 Retry 计划。
2. `src/lizi_typeless.Windows`：录音、UI、目标窗口、写入、服务管理和主流程。
3. `inference/src/lizi_typeless_inference`：FastAPI、流式 PCM 转换、模型运行时和整理提示词。
4. `inference/benchmarks`：模型直测、真实服务尾延迟和固定数据集评测。
5. `%LOCALAPPDATA%\lizi_typeless\sessions`：用户真实录音与会话元数据，不能由开发流程清理。
6. `artifacts/`：被 Git 忽略的样本和验证报告。
7. `%LOCALAPPDATA%` 下改名前的数据目录：当前只作为回退副本，不是活动写入位置，也不能在没有用户明确授权时删除。

### Working tree 与提交边界

1. 改名前仓库没有 HEAD，69 个项目文件全部未跟踪，因此不存在可用的历史 diff；首次提交就是当前项目基线。
2. `.gitignore` 必须继续排除 `.venv*`、`models/`、`data/`、`artifacts/`、`inference/logs/`、Python 缓存与所有 `bin/obj`。
3. 提交前必须同时检查 `git status --short --ignored`、`git diff --cached --stat` 和 `git diff --cached --name-only`，发现本机资产时停止提交。
4. 不运行 `git clean`、`git reset --hard` 或会删除用户录音、模型、报告和环境的清理命令。
5. 根提交 `68029f1` 是首个可比较代码基线；后续变更必须使用正常 Git diff，不再依赖文件时间或聊天记录判断修改范围。

### 必须遵守的约束

1. 已录音频优先于推理结果，任何模型错误都不能删除录音。
2. 用户结束录音前不向目标输入框写临时文字。
3. 运行期语音和文本不发送到外部 API。
4. 最大常见录音约一分钟，不为无限长会议转录建设复杂系统。
5. Retry 永不自动写回旧目标，避免重复或误投。
6. 不为商业化、通用插件体系或未来扩展提前增加抽象。

## 实施方案（Plan of Work）

### 已完成主链路

1. Windows 创建会话并在推理前开始写 `audio.raw` 与 `audio.wav`。
2. WASAPI 音频通过 WebSocket 送到 WSL2；服务解码、混音、重采样并返回预览。
3. 结束录音后，服务返回流式最终文本；连接失败时使用完整 WAV 批量转录。
4. 原始转录写盘后调用整理模型；最终结果写盘后验证目标窗口并一次性 `SendInput`。
5. 任一阶段失败记录 `FailureStage`；应用重启恢复未完成会话，历史 Retry 从最近可靠阶段继续。

### 完成状态

1. 当前没有个人 MVP 阻断项。
2. 异常退出、推理服务中断、项目名、专有名词格式、数字美化和其他体验问题随真实使用发现 case 后迭代。

## 具体步骤（Concrete Steps）

### 新会话冷启动门禁

1. 依次读取 `~/.codex/AGENTS.md`、仓库内 `AGENTS.md`（若存在）、[项目说明](../项目说明.md)、[项目进度追踪](../项目进度追踪.md)、本 ExecPlan、[产品方案](../产品方案.md)、[技术方案](../技术方案.md) 和 [测试验收](../测试验收.md)。
2. 先总结目标、不可违反约束、当前进度、决策、踩坑、风险、最小下一步和需要现场验证的事实；发现代码、测试、文档或运行状态冲突时先报告，不直接实现。
3. 执行 `git status --short --branch`、`git rev-parse HEAD`、服务健康检查、Windows 客户端进程检查和会话数量检查。
4. 若当前自动门禁、客户端和服务健康，下一步最小用户动作是第一条 45 至 60 秒普通长段落录音；先核对文本和耗时，再继续另外两条长录音。

### 自动验证

```bash
dotnet.exe restore lizi_typeless.slnx
dotnet.exe build lizi_typeless.slnx --configuration Debug --no-restore
dotnet.exe test lizi_typeless.slnx --configuration Release --no-restore
.venv/bin/python -m pytest inference/tests
.venv/bin/ruff check inference
```

### 启动推理服务

```bash
./scripts/start-inference.sh
curl --fail http://127.0.0.1:8765/v1/health
```

健康结果必须包含当前两个模型、RTX 5090 和 `"streaming":true`。

### 发布 Windows 客户端

从 WSL 仓库根目录获取 `%LOCALAPPDATA%` 路径，使用 `dotnet.exe publish` 输出到 `lizi_typeless\app`，再从 Windows 本地路径启动 `lizi_typeless.exe`。发布前先确认没有 `recording` 或 `processing` 会话，不能覆盖正在运行的客户端。

### 固定语音集

1. 复制 `inference/benchmarks/manifest.example.jsonl` 的字段格式，在被忽略的 `data/voice-benchmark/manifest.jsonl` 建立至少 20 条记录。
2. 每条记录包含准确人工原文、普通话子集标记、技术词和可选期望整理结果。
3. 执行：

```bash
.venv-vllm/bin/python inference/benchmarks/evaluate_dataset.py \
  data/voice-benchmark/manifest.jsonl \
  --minimum-samples 20 \
  --output artifacts/voice-benchmark.json
```

4. 报告必须保留清单哈希、每个音频哈希、流式原文、整理结果、CER、技术词命中和阶段耗时。

### Windows 端到端时延

长录音过程中可以随时生成部分报告。脚本只统计成功自动写入的 `completed` 会话；因非核心焦点问题保存为 `ready` 的样本，使用 `endToOrganizationMilliseconds` 和用户体验单独计入人工验收：

```bash
.venv/bin/python inference/benchmarks/summarize_sessions.py \
  /mnt/c/Users/admin/AppData/Local/lizi_typeless/sessions \
  --allow-partial \
  --output artifacts/session-timings.json
```

报告保留启动响应、短录音和长录音的阶段耗时。3 条长录音用于发现明显等待和性能回退，不宣称具有商用统计显著性。

## 验证与验收（Validation and Acceptance）

完整标准见 [测试验收](../测试验收.md)。当前证据：

1. Debug 构建 0 警告、0 错误。
2. Core 测试 10 项、Windows 合同与目标安全测试 5 项、Python 测试 17 项和 Ruff 已通过。
3. 健康接口确认 vLLM、整理模型和 RTX 5090 共存且支持流式。
4. 官方 4.2 秒样本在 20 ms 实时发送时，结束到最终转录约 68 ms，整理墙钟约 190 ms。
5. 合成 50.45 秒样本的转录收尾约 99 ms，整理墙钟约 1131 ms；该早期合成结果用于定位长文本整理成本，最终个人体验以后续 3 条真实长录音为准。
6. 首条个人录音原始 WAV、raw、元数据和正确 ASR 结果均保留；422 整理合同根因已修复并加入测试。
7. 单条官方短样本评测报告 CER 为 0、整理完全匹配，但不能替代 20 条个人样本。
8. 50.45 秒 WAV 批量回退在 512 token 上限下输出完整；提高上限后短样本流式收尾仍约 76 ms，没有性能回退。
9. 修复版人工 smoke test 通过：旧失败记录 Retry 后为 `ready` 且不自动插入；约 6.18 秒真实短录音在 VS Code 中准确写入一次。录音就绪 98.8 ms、结束到转录 98.0 ms、结束到整理 232.5 ms、结束到写入 246.1 ms。
10. 模型常驻与应用关闭各完成 600 秒空闲采样：功耗中位数增量 7.70 W、平均增量 8.93 W，均低于 10 W；GPU 利用率中位数和 P95 增量均为 0，量化资源目标通过。
11. 首批 10 条个人固定集的原始候选为 CER 6.16%、技术词命中 75%；扩大 ASR 上下文后为 CER 6.51%、技术词命中 83.3%，且把正确的 `Kubernetes` 回退为 `Typeless`，因此该方案已撤销。
12. 恢复稳定上下文并只保留三个当时已观察的精确词表修正后，同批重放为 CER 0.68%、原始与最终技术词命中 100%、整理归一化匹配 100%；严格字符串匹配 60%，其余差异为标点、空格或合理分段。该报告生成于改名前，旧项目称呼样本不再代表当前名称。
13. 当前 9 条有效短录音结束到写入 P50 466.6 ms、P95 582.3 ms；10 条录音就绪 P50 46.8 ms、P95 110.2 ms。110.2 ms 来自客户端重启后的首条冷设备录音；该批记录作为个人 MVP 短录音基线。
14. 完整改名后重新执行 restore、Debug build、Release test、Python pytest 和 Ruff：构建 0 警告、0 错误，Core 10 项、Windows 5 项、Python 14 项全部通过。
15. 两套 Python 环境都能从 `/home/litong/lizi_typeless/inference/src/lizi_typeless_inference` 导入新包；旧 editable 分发已卸载，新控制台入口为 `lizi_typeless_inference`。
16. 11 条有效会话复制到新活动目录后，源目标 33 个文件的相对路径和 SHA-256 全部一致；新目录含 11 份 raw、11 份 WAV 和 11 份元数据，状态为 10 条 `completed`、1 条 `ready`。
17. `%LOCALAPPDATA%\lizi_typeless\app\lizi_typeless.exe` 启动后进程正常响应，并从 `/home/litong/lizi_typeless` 拉起服务；健康接口返回 `Qwen3-ASR-1.7B`、`Qwen3-1.7B`、RTX 5090 和 `streaming: true`。
18. Chromium 实机录音完成处理并自动写入，但右 Alt 影响了网页文本框内部焦点；该问题已由用户明确降为后续体验优化，不阻断个人 MVP。
19. 首条有效长录音为 54.6 秒，结束到转录约 211 ms、结束到写入约 4.76 秒，用户确认等待体验良好。项目名识别错误被保留为真实质量样本，其余中文和四个技术词正确；该录音计入固定集第 11 条和长录音 1/3。
20. 第二条长录音为 58.9 秒，结束到转录约 155 ms、结束到整理约 3.33 秒，用户认为体验较快。数字、路径、版本、列表和明确改口正确；目标窗口变化使结果保存为 `ready`，不影响固定集第 12 条和长录音 2/3 的计数。
21. 第三条长录音为 55.7 秒，结束到转录约 166 ms、结束到整理约 3.34 秒，用户认为等待较短。问题语气、否定条件和失败恢复规则完整，模型没有回答问题或改变含义；该录音计入固定集第 13 条，长录音 3/3 完成。
22. 用户决定跳过客户端异常退出和推理服务中断的主动恢复测试；这些低概率问题改为在个人日常使用中发现 case、排查和修复，不再阻断 MVP。
23. 第 16 条样本中，ASR 原始转录正确保留 `127.0.0.1:8765` 的口述信息，整理模型却输出 `127.0.0.0.0:8765`。该事实变更违反忠实整理核心规则，列为 MVP 完成前必须修复并重放的阻断项。
24. 第 17 条样本中，ASR 两次正确保留“一点三四点二”，整理模型却两次输出 `1.3.2`，进一步证明中文数字到结构化字面量的模型转换存在系统性风险。
25. 第 18 条样本原样保留百分比和毫秒的中文数字读法，没有改变事实。该对照说明忠实保留优于不可靠的结构化数字转换。
26. 第 20 条样本的 ASR 原始转录完整包含两项否定限制，整理模型却删除整句，构成新的核心忠实度阻断。
27. 完成 20 条完整重放：原始 CER 8.55%、最终技术词命中率 75%、整理归一化匹配率 50%。人工复核确认第 16、17、20 条必须修复；其他旧称呼、数字格式和专有名词空格差异按既定范围后置。
28. 增加数字序列、否定限制和内容长度保护后，第 16、17、20 条定向重放全部保留正确原始信息。修复后 20 条完整重放没有新的核心语义回退，精确匹配率升至 35%、归一化匹配率升至 55%；安全保留中文数字读法导致技术词格式分数下降，不影响人工忠实度通过。

个人 MVP 没有剩余阻断项。

## 幂等与恢复（Idempotence and Recovery）

1. 环境检查、模型基准和固定测试集可以重复执行，不修改 Windows 用户历史。
2. 每次录音使用唯一会话 ID；Retry 复用原会话，增加 `RetryCount`，不删除原音频。
3. 没有原始转录时从 WAV 重做 ASR；已有原始转录时只重做整理。
4. 推理服务重启不删除 Windows 会话；流式失败使用同一会话的完整 WAV 回退。
5. 已标记完成的会话 Retry 时不自动插入，文本写入不确定时也不循环重试。
6. 应用退出或崩溃后，遗留录音会话在下次启动时恢复为可 Retry 状态，并尽可能从 raw 重建 WAV。
7. 改名迁移只复制活动会话并比较哈希，不修改会话内容和 schema；新客户端异常时可以停止新进程并从保留的旧目录人工恢复，禁止自动删除任一数据副本。
8. Python editable 安装可以用各环境的 `python -m pip uninstall` 与 `python -m pip install --no-deps -e inference` 重建；不需要重下模型或依赖。

## 证据与备注（Artifacts and Notes）

1. 服务日志：`inference/logs/service.log`，被 Git 忽略。
2. Windows 日志：`%LOCALAPPDATA%\lizi_typeless\diagnostics.log`。
3. 单样本评测自测：`artifacts/validation-report.json`，被 Git 忽略。
4. 官方音频样本：`artifacts/samples/`，被 Git 忽略。
5. GPU 空闲报告：`artifacts/gpu-idle-models-resident.json`、`gpu-idle-application-stopped.json` 和 `gpu-idle-comparison.json`，被 Git 忽略。
6. 首批质量对比报告：`artifacts/voice-benchmark-batch-01.json` 至 `voice-benchmark-batch-04.json`；当前采用第四批结果，均被 Git 忽略。
7. 当前部分时延报告：`artifacts/session-timings-batch-01.json`，明确标记 `partialReport: true`，被 Git 忽略。
8. 修复前后 20 条完整质量报告：`artifacts/voice-benchmark-mvp-20.json` 与 `artifacts/voice-benchmark-mvp-20-fixed.json`，均被 Git 忽略。
9. Windows 活动数据目录当前保留 27 条会话：23 条 `completed`、3 条 `ready` 和 1 条已保存原始录音、可 Retry 的 `failed`；改名前目录保留迁移时 11 条会话的同哈希回退副本，均不得由开发流程删除。
10. 改名验证时间点为 2026-07-11 11:45 CST；客户端进程和服务健康是易变事实，后续会话必须重新检查。
11. GitHub 仓库：`https://github.com/mrlitong/lizi_typeless.git`；根提交 `68029f1` 包含首次实现，提交 `1d7f6ed` 包含个人 MVP 忠实度保护、回归测试和验收收口；模型、个人数据、报告、日志和构建产物均未进入提交。

## 接口与依赖（Interfaces and Dependencies）

1. HTTP：`GET /v1/health`、`POST /v1/transcribe`、`POST /v1/organize`、`POST /v1/shutdown`，JSON 为 `snake_case`。
2. WebSocket：`/v1/stream`，控制消息为 camelCase，音频为有序二进制 PCM 帧。
3. Windows：`.NET 10.0.301`、解决方案 `lizi_typeless.slnx`、程序集 `lizi_typeless`、低级键盘 hook、NAudio/WASAPI、前台窗口 API、`SendInput`、托盘与 WPF。
4. Python：包与入口 `lizi_typeless_inference`，FastAPI 0.139、qwen-asr 0.0.6、vLLM 0.14、Transformers 4.57.6、SoXR 和 CUDA PyTorch。
5. 环境变量统一使用 `LIZI_TYPELESS_*`；WebSocket 控制字段和 HTTP JSON 字段属于接口合同，不随项目改名改变。
6. 外部网络只用于首次安装依赖和下载模型；运行期通过离线环境变量禁止外部模型访问。
7. 不使用付费服务、云端数据库、远程遥测或自动清理任务。
