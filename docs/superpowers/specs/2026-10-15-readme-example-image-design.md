# README 示例图片设计

## 目标

将用户提供的 Deadline Widget 截图原样加入仓库，并在 README 的“截图”章节展示实际桌面效果。

## 文件与展示方式

- 将原始 PNG 保存为 `docs/images/deadline-widget-example.png`，不裁剪、不模糊、不重新编码。
- 将 README 当前“暂无截图”的占位文字替换为简短说明和图片。
- 使用 HTML `img` 标签限制显示宽度并居中，避免窄长截图在 GitHub 页面中过度放大。
- 图片替代文本和说明使用“Deadline Widget 桌面效果示例”。

## 边界

- 保留截图中的课程编号、任务名称和日期，视为用户明确批准公开的示例内容。
- 不修改应用代码、构建流程、测试或其他文档。
- 验证图片路径存在、README 引用正确，且 Git diff 仅包含设计文档、图片和 README 变更。
