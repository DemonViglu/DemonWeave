# Minimal Quest Template

通过 Package Manager 的 Demon Weave Quest → Samples → Minimal Quest → Import 导入后，直接打开同目录 `MinimalQuest.unity` 并按 Play：蓝色方块旋转约 4 秒，任务完成。
双击 `MinimalQuest.asset` 打开任务图：Start → Rotate the cube → Done → Finish。

- Target：必填 Transform，可拖入另一个场景 GO 换绑；框架解析类型，代码直接使用 target。
- Seconds：旋转持续时间。
- Advanced / Degrees Per Second：角速度，默认 90。
- Advanced / Sound (Optional)：可选 AudioSource，未绑定时为 null；若绑定，请使用已配置音频的专用音源。

`RotateLogic.cs` 是唯一的业务代码；不需要 RescueEncounter、Startup 或额外 Gameplay 容器。
OnStart 播放可选音源，OnTick 旋转并提交 Done，OnStop 停止本节点使用的音源。
暂停只停止节点 Tick，不会自动暂停外部音源。

`[ShowInQuest]` 放主面板；未标记的公开参数默认放 Advanced。
`[ShowInQuest(Advanced = true, Required = false)]` 放 Advanced 并允许空对象引用。
已绑定但失效或类型错误仍报错。私有字段不参与作者配置。

Quest JSON 保存参数和引用 ID；场景 Runner 保存 ID 到对象的映射。
模板具有独立的 DemonWeave.MinimalQuest 程序集，核心不依赖模板。
包中的 Samples~ 不自动编译或注册 Logic；导入后才出现 sample.rotate。当前 DemonWeave 工程已有一份导入副本，位于 Assets/Samples/Demon Weave Quest/0.6.0/MinimalQuest，可直接打开，无须再次导入。用于正式内容时复制场景和任务资产，并更新 Runner 的任务引用。
