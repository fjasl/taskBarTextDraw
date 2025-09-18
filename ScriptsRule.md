# 任务栏文本脚本（Python）速查规则

照着它写，基本不踩坑。

## 1) 基本约定（Spec v2）

* **输出通道**：只看 `stdout`。**一行 = 一帧**。每打印一行，前端就尝试更新一次。
* **字符清洗**：`\r`/`\n` 会被替换为空格；C0 控制字符会被丢弃（TAB→空格）。
* **长度限制**：单帧按 UTF-8 **最大字节数**（`MaxBytesPerFrame`，默认 16KB）截断，尾部加 `…`。
* **节流**：前端按 `MinIntervalMs`（默认 250ms）节流；如果你输出更快，会只取**最新**一行。
* **去重**：如果前端设置里开启了 *Dedupe*，与上次相同的内容会被忽略（你无需处理）。
* **stderr**：只做调试记录，不影响显示。别把要显示的文本写到 `stderr`。

## 2) 两种输出形态

### 2.1 普通文本（单色）

* 直接 `print("文本", flush=True)`。
* **建议**：脚本开头明确声明单色模式：
  `print("##PLAIN", flush=True)`

### 2.2 多色文本（富文本）

* 用指令行：`##RICH { "segments":[ {"t":"字","fg":"#AARRGGBB"}, ... ] }`
* 每个片段 `t` 为字符串，`fg` 为颜色（可省略，省略则用全局颜色）。**最多 500 段**。
* **模式切换**：发过 `##RICH` 后进入多色模式；恢复单色请再发一次 `##PLAIN`。
* **建议**：脚本一开始就**声明你要的模式**（`##PLAIN` 或直接发 `##RICH`）以免受上一个脚本的遗留影响。

## 3) 控制指令（写设置）

* 指令行以 `##SET ` 开头，后面跟 JSON 对象，**部分更新**设置：

  ```json
  { 
    "Text": "任意字符串",
    "FontFamily": "Segoe UI",
    "FontSize": 18.0,
    "IsBold": true,
    "ForegroundHex": "#AARRGGBB",
    "Alignment": "Left|Center|Right|Justify",
    "ShadowOpacity": 0.0~1.0,
    "ShadowBlur": 0~30
  }
  ```
* 例子：

  ```python
  print('##SET {"Alignment":"Center","FontSize":20,"ForegroundHex":"#FFFF3366"}', flush=True)
  ```

## 4) 颜色格式

* 推荐 `#AARRGGBB`（如 `#FF00AAFF`）。也接受 `#RRGGBB`（自动补 `FF` 透明度）。
* 颜色无效则忽略该字段，不会报错或中断。

## 5) 环境变量（读设置）

脚本启动时，前端会把设置“喂”给你：

* `TTO_API="1"`：协议版本标记。
* `TTO_SETTINGS_JSON`：启动瞬间的设置快照（JSON 字符串）。
* `TTO_SETTINGS_FILE`：一个**临时 JSON 文件路径**，前端在设置变化时会刷新它。你要读最新设置就读这个文件。

示例：

```python
import os, json
snap = json.loads(os.environ.get("TTO_SETTINGS_JSON", "{}"))
path = os.environ.get("TTO_SETTINGS_FILE")
def get_settings():
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return snap
```

## 6) 推荐脚本模板

### 6.1 单色轮询（每 500ms 拉歌词）

```python
import json, time
from urllib.request import Request, urlopen
from urllib.error import URLError, HTTPError

print("##PLAIN", flush=True)                               # 明确单色
print('##SET {"Alignment":"Center"}', flush=True)          # 居中（可加 FontSize/ForegroundHex）

URL = "http://localhost:5005/api/lyric/current"
INTERVAL = 0.5
next_at = time.perf_counter()

while True:
    next_at += INTERVAL
    try:
        with urlopen(Request(URL, headers={"Accept":"application/json"}), timeout=0.4) as r:
            if r.status == 200:
                obj = json.loads(r.read().decode("utf-8","replace"))
                if obj.get("code")==0 and isinstance(obj.get("data"), dict):
                    print(obj["data"].get("text","") or "", flush=True)
    except (URLError, HTTPError, TimeoutError, OSError, json.JSONDecodeError):
        pass
    now = time.perf_counter()
    if next_at > now:
        time.sleep(next_at - now)
    else:
        next_at = now
```

### 6.2 多色（逐字符彩虹）

```python
import time, json, colorsys
from datetime import datetime
print('##SET {"Alignment":"Center"}', flush=True)          # 多色也会遵循 Alignment

def argb(h):
    r,g,b = [int(round(c*255)) for c in colorsys.hsv_to_rgb(h/360,1,1)]
    return f"#FF{r:02X}{g:02X}{b:02X}"

base = 0
while True:
    t = datetime.now().strftime("%H:%M:%S")
    segs = [{"t":ch, "fg":argb((base+i*360/max(1,len(t)))%360)} for i,ch in enumerate(t)]
    print("##RICH " + json.dumps({"segments":segs}, ensure_ascii=False), flush=True)
    base = (base + 15) % 360
    time.sleep(1)
```

### 6.3 动态改设置（示例：每秒换字号/颜色）

```python
import time, json
palette = ["#FFFF3B30","#FFFF9500","#FFFFCC00","#FF34C759","#FF5AC8FA","#FF007AFF","#FFAF52DE"]
print("##PLAIN", flush=True); print('##SET {"Alignment":"Center"}', flush=True)
i=0
while True:
    print(f'##SET {{"FontSize":{16+4*(i%4)},"ForegroundHex":"{palette[i%len(palette)]}"}}', flush=True)
    print(f"第{i}秒", flush=True)
    i+=1; time.sleep(1)
```

## 7) 常见问题速查

* **歌词脚本不更新但预览在变**：上一个脚本启用了多色模式。脚本开头加 `print("##PLAIN")`。
* **文本被截断并出现 `…`**：单帧字节超过 `MaxBytesPerFrame`。缩短文本或在设置里调大。
* **我打印很频繁但刷新跟不上**：受 `MinIntervalMs` 节流影响。要么拉大间隔，要么在设置里减小该值。
* **内容没变时也想刷新**：关掉设置里的 *Dedupe*，或确保打印不同的字符串（比如加空格或零宽字符不建议）。
* **网络异常时闪烁/清空**：不要打印任何东西即可保持上一帧；异常写到 `stderr` 而不是 `stdout`。
* **对齐在多色模式无效**：前端已把 `Alignment` 映射到了多色容器的 `HorizontalAlignment`。用 `##SET {"Alignment":"Center"}` 等即可。

## 8) 写脚本的小建议

* **始终 `flush=True`**，避免缓冲迟到。
* 避免多线程/子进程把日志写到 `stdout`（会干扰显示）。
* 需要稳定节奏时用 `time.perf_counter()` 做“节拍睡眠”，而不是简单 `time.sleep()` 累加。
* 如果脚本会结束，最后可 `print("##PLAIN")` 复位为单色。

> 一句话：**脚本开头先宣告模式/对齐**，之后“每行一帧”，需要多色就用 `##RICH`，需要改样式就发 `##SET`。其他都不用管。
