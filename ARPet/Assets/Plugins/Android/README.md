# Plugins/Android — 原生插件

**当前目录为空，而且首版很可能一直保持为空。**

## 什么时候才需要它

只有出现以下需求时才在这里放东西：

- 需要访问系统相册并写入照片（拍照留念）——可能需要 `AndroidJavaObject` 之外的原生调用；
- 需要自定义 `AndroidManifest.xml` 覆盖（权限声明、屏幕方向、`android:exported` 等）；
- 需要接入第三方的 `.aar` / `.jar`。

按 [AGENTS.md](../../../../AGENTS.md) 的约定：**原生插件按实际需要接入**，
不在首版预先堆一批用不上的库。Android Studio 与 adb 的职责是**调试**（Logcat、性能、崩溃），
应用主体仍然在 Unity 里开发与打包。

## 常见放置位置

| 文件 | 位置 | 说明 |
| --- | --- | --- |
| `AndroidManifest.xml` | `Assets/Plugins/Android/AndroidManifest.xml` | 与 Unity 生成的清单合并；只在确有必要时覆盖 |
| `.aar` / `.jar` | `Assets/Plugins/Android/` | 第三方原生库 |
| `gradleTemplate.properties` | `Assets/Plugins/Android/` | 需要改 Gradle 属性时 |

## 顺序建议

**先不要建这个目录里的任何文件。** 先用 Unity 默认设置构建出一个能跑的 APK，
确认真机跟踪可用之后，再按实际报错与需求逐项添加。
提前覆盖 `AndroidManifest.xml` 会让后续排查变得更难。
