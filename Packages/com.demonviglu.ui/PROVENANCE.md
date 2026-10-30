# 来源

2026-10-30 从本机 Firer/Packages/com.demonviglu.ui 0.1.0 选择性引入 Runtime、package.json 和说明，保留源 GUID。没有复制依赖 Firer Startup 测试工具的 Editor 验收器；原 README 中该验收菜单在此项目不存在。来源项目只读。首次引入 Runtime 未修改，后续适配写在 DemonWeave 项目层。

输入检查边界：保留原有 Cancel Action → Back 实现。隔离编辑器下的物理键盘/注入事件路由没有验证通过，不宣称 Esc 已可用；产品场景提供明确的 CLOSE / RETURN 鼠标入口，堆栈和关闭语义已独立验证。调查期间的替代键盘实现已撤回，Runtime 与来源保持一致。
