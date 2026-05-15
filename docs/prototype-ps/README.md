# Screenshot Tool (热键 + 桌面悬浮按钮)

两个独立脚本，按需选用：

| 脚本 | 入口 | 适用场景 |
|------|------|---------|
| `screenshot-hotkey.ps1` | 全局热键 `Ctrl+Alt+Shift+S` | **全屏 devbox / RDP 时唯一可用方案** |
| `screenshot-floating.ps1` | 桌面悬浮按钮 + 同样的热键 | 本地桌面或**窗口化** devbox 时点按钮即可 |

## 关于「全屏 devbox 时悬浮按钮可见」

像 PixPin 那样在全屏 devbox 上悬浮，**是可以做到的**，需要两个技术点配合：

| 挑战 | 解决方法 | 本工具是否已实现 |
|------|---------|----------------|
| 全屏 RDP/W365 客户端会"抢顶"，把本地 TopMost 窗口压下去 | 用定时器每 500ms 调用 `SetWindowPos(HWND_TOPMOST)` 把自己挤回最顶层 | ✅ `screenshot-floating.ps1` 已内置 |
| 虚拟桌面隔离（Win+Tab "New desktop" 创建的独立桌面，本地窗口默认不会出现在 devbox 那个桌面） | 让窗口"在所有虚拟桌面上显示" | ⚠️ 需手动操作一次，见下方 |

### 让悬浮按钮跨虚拟桌面显示（一次性手动操作）

1. 启动 `screenshot-floating.ps1`，确认按钮出现在桌面上
2. 按 `Win + Tab` 打开 Task View
3. 在缩略图里找到按钮所在的窗口缩略图（很小，大概在右下角的"Screenshot"窗口）
4. **右键** → 选 **「在所有桌面上显示此窗口」**（Show this window on all desktops）

之后无论你切到哪个虚拟桌面（包括 devbox 所在的那个），按钮都会浮在最上层。这跟 PixPin 的"图钉"原理一样。

> 为什么不让脚本自动做这一步？因为微软官方的「跨虚拟桌面 Pin」API（`IVirtualDesktopPinnedApps`）是未公开 COM 接口，**GUID 在每个 Win11 版本都会变**，硬编码极易失效。手动右键一次最稳。

### 仍然做不到的场景

- DirectX 独占全屏（fullscreen exclusive）的游戏 — 极少数 RDP 客户端会用这种模式，本工具压不住
- devbox 客户端启用了「在远端处理 Windows 键组合」并截获了 Esc 等按键 — 不影响截图，但可能让你的鼠标操作行为异常

## 文件

| 文件 | 说明 |
|------|------|
| `screenshot-hotkey.ps1` | 仅托盘 + 全局热键 |
| `screenshot-floating.ps1` | 桌面悬浮按钮 + 托盘 + 全局热键（功能全集） |
| `start-hidden.vbs` | 隐藏窗口启动 hotkey 版 |
| `start-floating-hidden.vbs` | 隐藏窗口启动 floating 版 |

## 默认行为

- **热键**: `Ctrl + Alt + Shift + S`
- **保存目录**: `%USERPROFILE%\Pictures\Screenshots`
- **截取范围**: 所有显示器组成的虚拟屏幕 (`-PrimaryOnly` 只截主屏；floating 版可在右键菜单切换)
- **文件名**: `shot_yyyyMMdd_HHmmss_fff.png`

## 悬浮按钮版用法 (`screenshot-floating.ps1`)

启动后桌面右下角出现一个深色圆角小窗，里面是蓝色相机按钮 📷：

| 操作 | 效果 |
|------|------|
| 左键单击按钮 | 立即截图（截图前自动隐藏按钮，避免拍到自己） |
| 鼠标**中键**按住拖动 | 把按钮拖到任意位置 |
| 在按钮/窗体上**右键** | 弹出菜单：截图 / 切换主屏全屏 / 打开目录 / 隐藏 / 退出 |
| 双击托盘图标 | 重新显示按钮 |
| `Ctrl+Alt+Shift+S` | 全局热键（全屏 devbox 下唯一可用） |

启动：
```powershell
powershell -ExecutionPolicy Bypass -File .\screenshot-floating.ps1
```
或双击 `start-floating-hidden.vbs` 后台静默启动。

## 使用方式

### 1. 临时运行 (前台)
```powershell
powershell -ExecutionPolicy Bypass -File .\screenshot-hotkey.ps1
# 或
powershell -ExecutionPolicy Bypass -File .\screenshot-floating.ps1
```

### 2. 后台静默运行
双击 `start-hidden.vbs` 或 `start-floating-hidden.vbs`，托盘出现图标 + 气泡提示。

### 3. 自定义保存目录
```powershell
powershell -ExecutionPolicy Bypass -File .\screenshot-hotkey.ps1 -SaveFolder D:\Shots
```

### 4. 开机自启 (推荐)
- `Win + R` 输入 `shell:startup` 打开启动文件夹
- 把 `start-hidden.vbs` 的**快捷方式**拖进去

### 5. 退出
托盘图标右键 → **Exit**

## ⚠️ 关于 devbox 全屏的关键点

`Ctrl + Alt + Shift + S` 是通过 Win32 `RegisterHotKey` 注册的**系统级热键**。Windows 在分发按键给焦点窗口（即 RDP/W365 客户端）**之前**会先匹配已注册的全局热键，所以本脚本在以下场景**都能工作**：

- ✅ devbox 在独立虚拟桌面 + 全屏（你截图里的场景）
- ✅ Windows 365 / AVD 客户端全屏
- ✅ mstsc.exe RDP 全屏

唯一**不工作**的情况：
- ❌ 热键里包含 `Win` 键，且 RDP 客户端「Apply Windows key combinations」设置为「On the remote computer」——这种情况 Win 组合键会被直接转发到远端。本脚本默认热键不含 Win 键，已规避此问题。
- ❌ 远端有应用使用了**低级键盘钩子（WH_KEYBOARD_LL）**抢在系统热键之前——极罕见，普通使用不会遇到。

如果某次按下没截图，先做两件事：
1. 看本地任务栏托盘的脚本图标是否还在（没在就是脚本已退出）
2. 切回本地桌面再按一次确认；若本地能截、devbox 全屏不能，说明远端有钩子拦截，换别的修饰键组合再试

## 修改热键

打开 `screenshot-hotkey.ps1` 找到这一段：
```powershell
$modifiers = $MOD_CONTROL -bor $MOD_ALT -bor $MOD_SHIFT -bor $MOD_NOREPEAT
$VK_S         = 0x53
$hotkeyDisplay = 'Ctrl+Alt+Shift+S'
```
- 修饰键: `$MOD_CONTROL` / `$MOD_ALT` / `$MOD_SHIFT` / `$MOD_WIN`（不推荐 Win）
- 主键: 用 [Virtual-Key Codes](https://learn.microsoft.com/windows/win32/inputdev/virtual-key-codes) 替换 `$VK_S`，例如 `0x70` = F1、0x2C = PrintScreen
- 同步改 `$hotkeyDisplay` 字符串以便托盘提示正确

> 提示：`PrintScreen` (0x2C) 在 Win11 默认会被「截图工具」抢占，使用前需在 设置 → 辅助功能 → 键盘 里关闭「使用 PrtScn 按钮打开屏幕截图」。
