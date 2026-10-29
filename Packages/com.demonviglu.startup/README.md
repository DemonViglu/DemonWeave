# Startup runtime in DemonWeave

Runtime snapshot of Firer `com.demonviglu.startup` 0.1.0, copied on 2026-10-29 without runtime source changes. Original package remains untouched. Only Runtime and the package manifest are included; Firer samples and acceptance menus are deliberately not imported.

AppHost + AppComposition own application startup/shutdown. Scene bindings remain scene-owned. Ordinary behaviours use Unity Update unless ordered loop driving is explicitly needed. Refer to DemonWeave/docs/current-goal.md for integration progress; copying the runtime alone is not proof that the sample is composed through it.
