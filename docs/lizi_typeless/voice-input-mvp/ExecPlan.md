# 语音输入 MVP ExecPlan

> 状态：执行中（完整改名完成，首次 GitHub 发布收口中）
> 风险等级：中风险
> 更新日期：2026-07-11 11:45 CST（Asia/Shanghai）

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
5. [x] 已完成批量与流式模型技术基线，选择 Qwen3-ASR-1.7B/vLLM eager 与 Qwen3-1.7B/Transformers 作为 MVP 候选。
6. [x] 已建立固定语音集清单、流式调用、CER、技术词命中率、哈希和报告工具。
7. [x] 已发布 Windows 本地客户端并完成首条个人录音；音频完整保存，ASR 文本正确。
8. [x] 已修复首条实机录音暴露的 C#/Python HTTP JSON 字段命名问题，并加入合同测试。
9. [x] 已在最新发布版完成失败记录 Retry 和 VS Code 安全输入框人工 smoke test；结果准确且只写入一次。
10. [ ] 建立至少 50 条个人真实语音集并决定候选模型是否转为最终方案；首批已完成 10 条。
11. [x] 已完成模型常驻与应用关闭各 10 分钟的 GPU 利用率、显存和相对功耗验收，量化目标通过。
12. [ ] 完成短长录音各 30 次性能统计、四阶段故障注入和四类应用兼容验收；当前有效短录音为 9 条。
13. [x] 已把仓库目录、解决方案、.NET 项目与命名空间、Python 包与环境变量、Windows UI、文档、发布目录和活动数据目录统一改名为 `lizi_typeless`。
14. [x] 已删除用户确认的 1 条误触失败会话，并把其余 11 条有效会话复制到新目录；33 个会话文件逐一通过 SHA-256 对比，旧目录保留。
15. [x] 已重新安装两套 editable Python 包、完成 .NET/Python/Ruff 门禁、发布并启动 `lizi_typeless.exe`，健康接口确认流式模型服务就绪。
16. [ ] 将应提交文件发布到 `https://github.com/mrlitong/lizi_typeless.git` 的 `main`，并确认模型、环境、个人数据、报告、日志和构建产物不在提交中。

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

7. Decision: 生产候选 ASR 使用 vLLM eager 流式后端，不使用批量主路径或 vLLM compiled 模式。
   Rationale: 真实长音频和首次追加证据排除了另外两条路径。
   Date/Author: 2026-07-11 / Codex

8. Decision: 当前模型组合只作为 MVP 候选，在 50 条个人语音集通过前不标记为最终。
   Rationale: 官方样本和合成重复音频不能代替个人口音、技术词和改口场景。
   Date/Author: 2026-07-11 / Codex

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

## 结果与复盘（Outcomes & Retrospective）

当前已经交付可运行但尚未完整验收的 MVP：Windows 客户端和两个本地模型可同时运行，流式预览与保存主路径打通，首条个人录音在整理失败时没有丢失任何音频或转录，修复版 Retry 成功且未自动写入。后续 10 条个人录音全部完成处理和写入；改名前首批固定集在当时的精确个人词表下达到 CER 0.68%、技术词命中 100% 和整理归一化匹配 100%，证明核心路径和候选模型具备继续扩样验收的基础。

完整改名已经在源码、运行环境和 Windows 发布路径上落地。11 条有效历史会话在新旧目录间逐文件一致，新客户端进程可以响应，并从新仓库路径启动健康的 vLLM/Transformers 服务。改名没有改变会话 schema、HTTP/WebSocket 合同、模型或用户交互，但旧质量集不能替代 `lizi_typeless` 的真实口述样本。

仍不能声称项目完成。质量集还差 40 条；性能统计还差 21 条短录音和 30 条长录音；四类应用矩阵与四阶段各 10 次故障注入也未完成。当前 9 条有效短录音达到 P50 500 ms、P95 800 ms 的目标，但普通长文本能否满足 1200 ms 目标仍缺真实证据。

## 待确认与不确定点

1. 仓库内没有 `./AGENTS.md`，当前只能读取 `~/.codex/AGENTS.md`。新会话必须重新检查仓库级文件是否后来出现，不能假定聊天中粘贴的规则等同于磁盘文件。
2. `lizi_typeless` 尚无真实口述固定集样本；旧称呼专用纠正规则已经移除，在出现实际识别错误前不增加猜测性词表。
3. 四阶段各 10 次故障注入没有确定性测试入口或独立验收账本。新会话在批量执行前应先设计最小、可复现且不删除录音的测试方法，不能从一般 smoke 结果推定通过。
4. 改名前 Windows 数据目录只作为回退副本保留；是否在长期稳定使用后删除，必须由用户另行明确授权。
5. 运行进程、健康接口和 Git 工作树是易变事实，新会话必须现场复查，不能只沿用本文件的时间点结论。

## 上下文与系统导览（Context and Orientation）

### 当前环境

1. 工作目录：`/home/litong/lizi_typeless`。
2. Git：`main`，首次发布目标为 `https://github.com/mrlitong/lizi_typeless.git`；精确 HEAD 与工作树必须用 Git 现场确认。
3. 指令：`~/.codex/AGENTS.md` 存在，仓库内当前没有 `AGENTS.md`。
4. Windows：Windows 11，.NET SDK 10.0.301；客户端发布为 `%LOCALAPPDATA%\lizi_typeless\app\lizi_typeless.exe`。
5. WSL2：Ubuntu 24.04，Python 3.12；推理服务监听 `127.0.0.1:8765`。
6. GPU：NVIDIA GeForce RTX 5090，约 32 GB；两模型共存观察值约 22.4 GB 显存。
7. 模型：`models/Qwen3-ASR-1.7B` 与 `models/Qwen3-1.7B`。
8. Python 环境：`.venv-vllm` 是流式候选运行环境；`.venv` 用于批量基线、测试和 Ruff；两者都以 editable 方式安装 `lizi_typeless_inference`。

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

### 剩余收口

1. 完成 GitHub 首次提交与 `main` 推送，并确认忽略资产没有进入仓库。
2. 再采集 40 条个人语音，加入 `lizi_typeless` 真实口述，运行质量报告并人工审核整理忠实度。
3. 再积累 21 条有效短录音和 30 条长录音，从 Windows 会话元数据汇总 P50/P95，确认或优化长文本整理。
4. 完成故障注入和兼容矩阵验收，并在数日真实使用中观察风扇主观表现。

## 具体步骤（Concrete Steps）

### 新会话冷启动门禁

1. 依次读取 `~/.codex/AGENTS.md`、仓库内 `AGENTS.md`（若存在）、[项目说明](../项目说明.md)、[项目进度追踪](../项目进度追踪.md)、本 ExecPlan、[产品方案](../产品方案.md)、[技术方案](../技术方案.md) 和 [测试验收](../测试验收.md)。
2. 先总结目标、不可违反约束、当前进度、决策、踩坑、风险、最小下一步和需要现场验证的事实；发现代码、测试、文档或运行状态冲突时先报告，不直接实现。
3. 执行 `git status --short --branch`、`git rev-parse HEAD`、服务健康检查、Windows 客户端进程检查和会话数量检查。
4. 若 GitHub 首次发布已经完成，下一步最小用户动作是一条 5–15 秒 Chromium 安全输入框录音；它同时补充当前名称质量样本、短时延和尚未开始的浏览器兼容证据。先验证这一条，再安排批量录音。

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

1. 复制 `inference/benchmarks/manifest.example.jsonl` 的字段格式，在被忽略的 `data/voice-benchmark/manifest.jsonl` 建立至少 50 条记录。
2. 每条记录包含准确人工原文、普通话子集标记、技术词和可选期望整理结果。
3. 执行：

```bash
.venv-vllm/bin/python inference/benchmarks/evaluate_dataset.py \
  data/voice-benchmark/manifest.jsonl \
  --output artifacts/voice-benchmark.json
```

4. 报告必须保留清单哈希、每个音频哈希、流式原文、整理结果、CER、技术词命中和阶段耗时。

### Windows 端到端时延

短长录音各积累 30 条后执行：

```bash
.venv/bin/python inference/benchmarks/summarize_sessions.py \
  /mnt/c/Users/admin/AppData/Local/lizi_typeless/sessions \
  --output artifacts/session-timings.json
```

报告必须分别满足启动响应、短录音、长录音和长短 P50 差值阈值；样本不足时脚本直接失败，不能把小样本报告当成验收结果。

## 验证与验收（Validation and Acceptance）

完整标准见 [测试验收](../测试验收.md)。当前证据：

1. Debug 构建 0 警告、0 错误。
2. Core 测试 10 项、Windows 合同与目标安全测试 5 项、Python 测试 14 项和 Ruff 已通过。
3. 健康接口确认 vLLM、整理模型和 RTX 5090 共存且支持流式。
4. 官方 4.2 秒样本在 20 ms 实时发送时，结束到最终转录约 68 ms，整理墙钟约 190 ms。
5. 合成 50.45 秒样本的转录收尾约 99 ms，整理墙钟约 1131 ms；当前完整尾延迟尚未证明达标。
6. 首条个人录音原始 WAV、raw、元数据和正确 ASR 结果均保留；422 整理合同根因已修复并加入测试。
7. 单条官方短样本评测报告 CER 为 0、整理完全匹配，但不能替代 50 条个人样本。
8. 50.45 秒 WAV 批量回退在 512 token 上限下输出完整；提高上限后短样本流式收尾仍约 76 ms，没有性能回退。
9. 修复版人工 smoke test 通过：旧失败记录 Retry 后为 `ready` 且不自动插入；约 6.18 秒真实短录音在 VS Code 中准确写入一次。录音就绪 98.8 ms、结束到转录 98.0 ms、结束到整理 232.5 ms、结束到写入 246.1 ms。
10. 模型常驻与应用关闭各完成 600 秒空闲采样：功耗中位数增量 7.70 W、平均增量 8.93 W，均低于 10 W；GPU 利用率中位数和 P95 增量均为 0，量化资源目标通过。
11. 首批 10 条个人固定集的原始候选为 CER 6.16%、技术词命中 75%；扩大 ASR 上下文后为 CER 6.51%、技术词命中 83.3%，且把正确的 `Kubernetes` 回退为 `Typeless`，因此该方案已撤销。
12. 恢复稳定上下文并只保留三个当时已观察的精确词表修正后，同批重放为 CER 0.68%、原始与最终技术词命中 100%、整理归一化匹配 100%；严格字符串匹配 60%，其余差异为标点、空格或合理分段。该报告生成于改名前，旧项目称呼样本不再代表当前名称。
13. 当前 9 条有效短录音结束到写入 P50 466.6 ms、P95 582.3 ms；10 条录音就绪 P50 46.8 ms、P95 110.2 ms。110.2 ms 来自客户端重启后的首条冷设备录音，热状态完整样本仍待补齐。
14. 完整改名后重新执行 restore、Debug build、Release test、Python pytest 和 Ruff：构建 0 警告、0 错误，Core 10 项、Windows 5 项、Python 14 项全部通过。
15. 两套 Python 环境都能从 `/home/litong/lizi_typeless/inference/src/lizi_typeless_inference` 导入新包；旧 editable 分发已卸载，新控制台入口为 `lizi_typeless_inference`。
16. 11 条有效会话复制到新活动目录后，源目标 33 个文件的相对路径和 SHA-256 全部一致；新目录含 11 份 raw、11 份 WAV 和 11 份元数据，状态为 10 条 `completed`、1 条 `ready`。
17. `%LOCALAPPDATA%\lizi_typeless\app\lizi_typeless.exe` 启动后进程正常响应，并从 `/home/litong/lizi_typeless` 拉起服务；健康接口返回 `Qwen3-ASR-1.7B`、`Qwen3-1.7B`、RTX 5090 和 `streaming: true`。

仍需留下：完整 50 条个人质量报告、短长各 30 次端到端统计、连续右 Alt 操作、四阶段各 10 次故障注入和四类应用矩阵。

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
8. Windows 活动数据目录保留 1 条已 Retry 为 `ready` 的首条个人会话和 10 条 `completed` 会话；改名前目录保留相同哈希的回退副本，均不得由开发流程删除。
9. 改名验证时间点为 2026-07-11 11:45 CST；客户端进程和服务健康是易变事实，后续会话必须重新检查。
10. GitHub 目标仓库：`https://github.com/mrlitong/lizi_typeless.git`；首次提交只包含未被 `.gitignore` 排除的项目文件。

## 接口与依赖（Interfaces and Dependencies）

1. HTTP：`GET /v1/health`、`POST /v1/transcribe`、`POST /v1/organize`、`POST /v1/shutdown`，JSON 为 `snake_case`。
2. WebSocket：`/v1/stream`，控制消息为 camelCase，音频为有序二进制 PCM 帧。
3. Windows：`.NET 10.0.301`、解决方案 `lizi_typeless.slnx`、程序集 `lizi_typeless`、低级键盘 hook、NAudio/WASAPI、前台窗口 API、`SendInput`、托盘与 WPF。
4. Python：包与入口 `lizi_typeless_inference`，FastAPI 0.139、qwen-asr 0.0.6、vLLM 0.14、Transformers 4.57.6、SoXR 和 CUDA PyTorch。
5. 环境变量统一使用 `LIZI_TYPELESS_*`；WebSocket 控制字段和 HTTP JSON 字段属于接口合同，不随项目改名改变。
6. 外部网络只用于首次安装依赖和下载模型；运行期通过离线环境变量禁止外部模型访问。
7. 不使用付费服务、云端数据库、远程遥测或自动清理任务。
