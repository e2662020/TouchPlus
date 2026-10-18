# Touch+ · 触控屏修补宿主

> 面向 Windows 触控大屏的**插件化修补宿主**。
> 宿主负责界面、插件生命周期、系统参数存取与安全边界；具体修什么由插件决定。

Windows 10/11 原生支持，兼容 Windows 7 SP1 及以上。

---

## 一、这个仓库是什么

本仓库只有**宿主**（`TouchPlus.exe`）。它是一个空壳 + 一套约束：

- 发现并加载插件，校验插件声明的能力（钩子 / 输入注入 / 修改系统参数）；
- 提供插件设置界面、日志、配置持久化、单实例与托盘；
- 提供受控的系统参数读写（`PatchController`），插件自己不直接摸注册表；
- 插件可随时启停，停用即还原它对系统做的改动（由插件自己实现）。

**具体修补内容不在这个仓库**。官方插件在独立仓库维护：

| 插件 | 作用 | 仓库 |
|---|---|---|
| 触控双击容差 | 放宽 Windows 双击判定矩形与时间窗，解决触控屏双击打不开应用 | [TouchPlus.Plugin.DoubleClick](https://github.com/e2662020/TouchPlus.Plugin.DoubleClick) |

没有装插件时宿主照常运行，只是插件列表为空 —— 这是一个**平台**，不是单一功能工具。

## 二、界面

两个视图，同一个窗口：

- **插件中心**（主界面）：已安装插件列表、启用/停用开关、添加插件、外观（跟随系统/浅色/深色）、关于宿主；
- **插件设置**：每个插件自己的页面（概览 / 参数 / 日志 / 关于），由宿主框架提供壳，内容由插件的数据驱动。

窗口是无边框圆角窗，Win10+ 走 DWM 合成，Win7/8 走经典双缓冲；所有控件主题化，深浅色自动跟随系统。

## 三、插件机制

插件是一个 `.dll`，放在 `TouchPlus.exe` 同级目录：

```
Touch+/
  TouchPlus.exe
  plugins/
    TouchPlus.Plugins.DoubleClick.dll
```

宿主启动时扫描 `plugins\*.dll`，也可以在界面里用「添加插件…」选择任意位置的 DLL。

### 契约

插件实现 `IPatchPlugin`，并通过 `IPluginFactory` 暴露入口（宿主反射查找，公共无参构造函数）：

```csharp
public interface IPluginFactory
{
    string Id { get; }
    string Name { get; }
    IPatchPlugin Create(PluginContext context);
}

public interface IPatchPlugin
{
    string Id { get; }
    string Name { get; }
    string Summary { get; }
    PluginSafety Safety { get; }     // Safe / Advanced
    string IconKey { get; }
    bool Enabled { get; }
    bool Enable(out string error);
    void Disable();
    PluginStats GetStats();          // 给界面显示计数/读数
    void SaveConfig();
}
```

`PluginContext` 提供 `AppSettings`、`PatchController`、宿主能力声明。插件通过 `PatchController` 申请修改系统参数，由宿主统一备份原值、写入与还原。

### 能力校验与信任边界

宿主校验插件**声明**的能力是否在授权范围内（`AllowInputHook` / `AllowInputInjection` / `AllowSystemParams`）。

必须说明：**能力声明是约束不是沙箱**。插件与宿主同进程、以当前用户权限运行，加载不可信 DLL 等同于运行不可信程序。只安装可信来源的插件。

## 四、构建与运行

只需要系统自带的 `csc.exe`（不需要 Visual Studio / .NET SDK）：

```powershell
pwsh -ExecutionPolicy Bypass -File .\build.ps1        # 构建宿主
.\build\TouchPlus.Test.exe logic                       # 纯逻辑自检（98 项，不碰系统）
.\dist\Touch+\TouchPlus.exe --show                     # 启动
```

若本地存在 `src\plugins\`（联调用），`build.ps1` 会顺带把插件编成 DLL 输出到 `dist\Touch+\plugins\`；否则跳过，只产宿主。

### 命令行

| 参数 | 作用 |
|---|---|
| `--show` | 强制显示主界面 |
| `--tray` | 静默启动，只驻留托盘（开机自启用它） |
| `--safe` | 安全模式：不改任何系统设置、不装钩子，只开界面（排障用） |
| `--theme light\|dark\|auto` | 指定配色 |
| `--settings` | 直接打开插件设置窗口（`--page N` 指定页签） |
| `--add-plugin <dll>` | 添加插件后退出（返回 0 成功 / 2 失败） |
| `--reset` | 恢复默认配置后启动 |
| `--autoclose N` | N 秒后自动退出（自动化测试用） |

## 五、安全模型

- **普通用户权限**：清单 `asInvoker`，不要求管理员；
- **只动当前用户**：注册表只写 `HKCU`，系统改动由插件通过 `PatchController` 申请并备份原值；
- **不装服务 / 不写系统目录 / 不驻留驱动**；
- **无网络代码**：项目里没有任何 HTTP/Socket 调用，配置与日志只落 `%APPDATA%\TouchPlus\`；
- **可还原**：停用插件或点「还原系统默认」即收回改动；
- **输入注入默认关闭**：涉及钩子的插件必须显式开关并标注"高级"。

## 六、目录结构

```
build.ps1                    一键构建（自带 csc，无需 VS/SDK）
assets/TouchPlus.ico         程序图标（多尺寸 32bpp）
src/host/
  Program.cs                 入口：单实例、命令行、插件扫描与注册、托盘
  Core/                      Win32 封装、PatchController、设置、日志、插件契约与加载器
  UI/                        主题、自绘控件、插件中心、插件设置窗口
  Properties/ app.manifest
src/TouchPlus.Test/          逻辑自检（纯逻辑，不碰系统）
src/TouchPlus.Verify/        观察式验证工具（零注入，可用于真实触控面板）
```

## 七、兼容性

- **系统**：Windows 7 SP1 / 8 / 8.1 / 10 / 11；
- **运行时**：.NET Framework 4.0+（Win10/11 自带 4.8；Win7 SP1 需 4.6.2+）；
- **数据目录**：`%APPDATA%\TouchPlus\`（`settings.json`、`logs\`），删目录即彻底卸载。

## 八、相关仓库

| 仓库 | 内容 |
|---|---|
| [TouchPlus](https://github.com/e2662020/TouchPlus) | 宿主（本仓库） |
| [TouchPlus.Plugin.DoubleClick](https://github.com/e2662020/TouchPlus.Plugin.DoubleClick) | 插件：触控双击容差 |