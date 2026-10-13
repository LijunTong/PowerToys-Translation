# PowerToys Run 翻译插件

在 PowerToys Run（`Alt+Space`）里输入 `tr <文本>` 即可翻译，回车把译文复制到剪贴板。

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

- **翻译后端** —— 百度翻译开放平台（通用文本翻译）；另附免密钥的 Google 端点，可作备选
- **语言方向** —— 默认自动判断：中文译英文，其余译简体中文，也可显式指定
- **依赖** —— 无第三方 NuGet 包，只用 .NET 基类库

---

## 快速开始

```powershell
# 安装（首次运行会引导你填入 API 密钥）
powershell -ExecutionPolicy Bypass -File install.ps1

# 以后修改密钥或切换后端
powershell -ExecutionPolicy Bypass -File install.ps1 -Reconfigure
```

安装完成后按 `Alt+Space`，输入 `tr hello`，回车即可。

---

## 环境要求

| 项 | 版本 |
| --- | --- |
| PowerToys | 0.64.1 或更高 |
| .NET SDK | 6.0.x（仅编译时需要） |
| 操作系统 | Windows |

> 插件目标框架为 `net6.0-windows`，与 PowerToys 0.64 的 `PowerToys.PowerLauncher` 保持一致。
> 编译时引用本机 PowerToys 安装目录下的 `Wox.Plugin.dll`，因此需要先安装 PowerToys。
> PowerToys 装在非默认位置时，用 `-PowerToysPath <路径>` 指定。

---

## 获取 API 密钥

1. 前往 <https://fanyi-api.baidu.com/> 注册并完成实名认证
2. 开通「**通用文本翻译**」服务
3. 在**管理控制台 → 开发者信息**中获取 `appid` 与 `密钥`
4. 写入配置：

   ```powershell
   powershell -ExecutionPolicy Bypass -File install.ps1 -Reconfigure
   ```

   或直接传参：

   ```powershell
   powershell -ExecutionPolicy Bypass -File install.ps1 -Reconfigure -Provider baidu -AppId 你的appid -SecretKey 你的密钥
   ```

查看当前配置（密钥打码）：`powershell -ExecutionPolicy Bypass -File install.ps1 -Show`

> [!IMPORTANT]
> `appid` 是**一串数字**（形如 `20210101000000000`）。填入占位值会返回 `52003 appid 未授权`。
> `appid` 与密钥是**配对**的 —— 密钥填错会返回 `54001 签名错误`，两个值需一起替换。
>
> 若在百度控制台开启了 IP 白名单，记得把本机公网 IP 加入，否则返回 `58000`。

> [!WARNING]
> 密钥以**明文**保存在
> `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\Translate\config.json`。
> 该文件已列入 `.gitignore`，**请勿分享或提交**。

---

## 用法

在 PowerToys Run 中输入 `tr` 加一个空格，再输入内容：

| 输入 | 结果 |
| --- | --- |
| `tr hello` | 非中文 → 默认目标语言（简体中文） |
| `tr 今天天气很好` | 中文 → 英文 |
| `tr en:ja good morning` | 指定源与目标 → `おはよう` |
| `tr :ja hello` | 只指定目标语言，源自动检测 |
| `tr ja: こんにちは` | 只指定源语言，目标用默认值 |
| `tr`（留空） | 显示用法提示 |

冒号支持**全角 `：`**，中文输入法直接输入即可。语言代码会经过校验，
因此 `tr 3:00 pm`、`tr http://a/b` 会被当作普通文本，不会误判成语言对。

### 快捷键

| 按键 | 动作 |
| --- | --- |
| `Enter` | 复制译文（翻译失败时为重试） |
| `Ctrl+C` | 复制译文 |
| `Ctrl+Shift+C` | 复制原文 |
| `Ctrl+S` | 交换语言并重译 |
| `Ctrl+O` | 打开百度翻译网页 |
| `Ctrl+Shift+O` | 打开配置文件夹 |

### 语言代码

`zh`(简体) `zh-tw`(繁体) `en` `ja` `ko` `fr` `es` `de` `ru` `pt` `it` `th` `ar` `vi` `nl` `pl`
`el` `hu` `sv` `da` `fi` `cs` `ro` `sk` `et` `bg` `yue`(粤语) `wyw`(文言文)

也接受各家的常见写法（`jp`、`kor`、`fra`、`spa`、`cht`、`zh-CN`、`zh-TW`、`eng` …），
内部会映射到当前后端所需的代码 —— 百度与 Google 对同一门语言的写法并不一致。

### 自动方向规则

- 文本以**中文**为主（汉字占比 ≥ 30%，且不含假名/谚文）→ 译为英文
- 其余文本 → 译为 `defaultTarget`（默认简体中文）
- 显式写了源语言时（`tr ja: …`），说明语言已知，直接用 `defaultTarget`

> 日文和韩文同样使用汉字，所以中文判定会排除含假名或谚文的文本 ——
> 否则日文会被误判为中文，触发一次无意义的「日文 → 英文」。

---

## 配置

配置文件位置：

```
%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\Translate\config.json
```

首次运行会自动生成；改坏了也不影响插件加载，会回退默认值并在结果中提示。
**修改后无需重启**，插件会在下次查询时自动重新读取。

```json
{
  "provider": "baidu",
  "fallbackProvider": "none",
  "defaultTarget": "zh",
  "autoDirection": true,
  "requestTimeoutSeconds": 8,
  "minIntervalMs": 1100,
  "debounceMs": 400,
  "maxCharsPerRequest": 1900,
  "baidu": { "appid": "", "secretKey": "" }
}
```

| 字段 | 说明 |
| --- | --- |
| `provider` | `baidu` / `google` / `none` |
| `fallbackProvider` | 主后端失败时再试的后端，`none` 表示不兜底 |
| `defaultTarget` | 无法自动判断方向时使用的目标语言 |
| `autoDirection` | 是否启用上面的自动方向规则 |
| `requestTimeoutSeconds` | 单次请求超时（2–60） |
| `minIntervalMs` | 两次出站请求的最小间隔（默认 1100ms，对应百度免费版约 1 QPS） |
| `debounceMs` | 打字停止多久后才真正发出请求（默认 400ms，`0` 关闭防抖，范围 0–3000） |
| `maxCharsPerRequest` | 单次请求文本上限（UTF-8 字节，超出会截断并在副标题标注「已截断」） |

> [!NOTE]
> 密钥不走 PowerToys 设置界面 —— 0.64 的 `ISettingProvider` 只支持布尔开关，无法提供文本输入框。

### 配额保护

PowerToys Run 每敲一个键都会查询一次插件。为避免浪费免费额度，插件内建五层保护：

1. **防抖** —— 打字停止 `debounceMs` 后才发请求，连续输入只产生一次请求
2. **取消过期请求** —— 继续打字时立刻放弃上一次未完成的请求
3. **相同请求合并** —— 在途的相同请求复用同一个 HTTP 调用
4. **结果缓存** —— 相同文本与语言直接命中，不发请求
5. **限速** —— 两次出站请求最小间隔 `minIntervalMs`

### Google 备选后端

无需密钥，可用于先体验效果：

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1 -Reconfigure -Provider google
```

它调用 `translate.googleapis.com` 的免密钥网页端点。这是**未公开接口**，可能被限流或随时变更，
因此不作为默认后端。也可将 `fallbackProvider` 设为 `google` 作为兜底。

> 粤语（`yue`）与文言文（`wyw`）仅百度支持，Google 端点不可用。

---

## 错误码

百度返回的错误会带上原始错误码与中文建议：

| 错误码 | 含义 / 处理 |
| --- | --- |
| `52001` / `52002` | 超时 / 服务端错误，插件会自动重试一次 |
| `52003` | appid 未授权或不存在 —— 检查是否为数字、有无开通「通用文本翻译」 |
| `54001` | 签名错误，检查密钥是否与 appid 匹配 |
| `54003` / `54005` | 访问频率受限，稍后重试（免费版约 1 QPS） |
| `54004` | 余额或免费额度不足 |
| `58000` | 客户端 IP 非法，检查百度控制台的 IP 白名单 |
| `58001` | 该语言方向不被支持 |
| `90107` | 账号认证未通过或未生效 |

---

## 卸载

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall
```

默认使用 PowerToys 自带机制：写入 `NeedDelete.txt` 后由 PowerToys 删除插件目录。
加 `-Immediate` 直接删除，加 `-Yes` 跳过确认。`settings.json` 中的残留条目会在
PowerToys 下次启动时依据实际扫描结果自动清理，无需手动处理。

---

## 常见问题

**插件没有出现在 PowerToys Run 里**

插件目录必须包含 `plugin.json`、插件 DLL 和 `Images\*.png`。若 `IcoPathDark` /
`IcoPathLight` / `ExecuteFileName` 指向的文件缺失，PowerToys 会**静默跳过**该插件。
可查看日志确认：

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run\Logs" -Recurse -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
    ForEach-Object { Select-String -Path $_.FullName -Pattern 'Translate|FailedToInitialize' }
```

正常应看到 `Load cost for <Translate>` 与 `Total initialize cost for <Translate>`。

**提示「尚未配置 API 密钥」** → 运行 `install.ps1 -Reconfigure` 填写。
若密钥已填好却仍提示，检查是否在插件启动**之后**才修改的 `config.json` ——
重新运行一次 `install.ps1` 即可（脚本会输出 `PASS` 表示注册成功）。

**提示「访问频率受限」** → 把 `minIntervalMs` 调大。

**结果比以前慢一点** → 这是防抖的正常代价。单个词输完停顿时基本无感，
只在连续打字时才会等待；可调小 `debounceMs` 或设为 `0` 关闭。

**`Alt+Space` 打不开** → PowerToys Run 模块未在运行：

```powershell
Start-Process 'C:\Program Files\PowerToys\modules\launcher\PowerToys.PowerLauncher.exe'
```

---

## 开发

```powershell
# 编译
dotnet build src\Community.PowerToys.Run.Plugin.Translate -c Release

# PowerToys 装在别处时
dotnet build src\Community.PowerToys.Run.Plugin.Translate -c Release -p:PowerToysPath="D:\PowerToys"
```

`install.ps1` 是安装、更新、配置、查看、卸载的唯一入口，支持 `-Show`、`-Reconfigure`、
`-SkipBuild`、`-SkipRestart`、`-NonInteractive` 等参数。

图标可用 `tools/make-icons.py`（需要 Pillow）重新生成。

---

## License

[MIT](LICENSE) © Joont