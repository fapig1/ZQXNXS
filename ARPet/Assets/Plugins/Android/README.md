# Plugins/Android — 原生插件

**当前已有 `settingsTemplate.gradle`**，用于 Google Maven 访问不稳定时的镜像回退；见 [SETUP 构建说明](../../../SETUP.md)。本目录没有相册保存插件，不能把构建模板当成拍照功能已实现。

## 什么时候才需要它

除现有 Gradle 模板外，以下需求按实际需要接入：

- 需要访问系统相册并写入照片（拍照留念）——可能需要 `AndroidJavaObject` 之外的原生调用；
- 需要自定义 `AndroidManifest.xml` 覆盖（权限声明、屏幕方向、`android:exported` 等）；
- 需要接入第三方的 `.aar` / `.jar`。

按 [AGENTS.md](../../../../AGENTS.md) 的约定：**原生插件按实际需要接入**，
不在首版预先堆一批用不上的库。Android Studio 与 adb 的职责是**调试**（Logcat、性能、崩溃），
应用主体仍然在 Unity 里开发与打包。

## 常见放置位置

| 文件 | 位置 | 说明 |
| --- | --- | --- |
| `settingsTemplate.gradle` | `Assets/Plugins/Android/` | 已存在，Google Maven 镜像回退 |
| `AndroidManifest.xml` | `Assets/Plugins/Android/AndroidManifest.xml` | 与 Unity 生成的清单合并；只在确有必要时覆盖 |
| `.aar` / `.jar` | `Assets/Plugins/Android/` | 第三方原生库 |
| `gradleTemplate.properties` | `Assets/Plugins/Android/` | 需要改 Gradle 属性时 |

## 后续维护

保留现有 Gradle 模板。它只作用于 Gradle 依赖下载，不能解决 Unity 的 sdkmanager 远程清单检测；该问题的已验证处理见 SETUP。相册、权限或第三方库按实际需求加入，并同步平台实现和对应文档；当前 PhotoCapture 仍是明确失败的占位接口。
