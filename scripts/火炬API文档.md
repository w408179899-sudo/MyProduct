### 初始化模块

| 函数 | 返回 | 说明 |
|------|------|------|
| `init.InitGameinfo(pid, mode, LICENSE_KEY)` | bool, err | 初始化游戏信息：pid=进程ID，mode=模式(如"driver" or "api") | 驱动key |

### 玩家信息操作

| 函数 | 返回 | 说明 |
|------|------|------|
| `M.GetPlayerAddr()` | number/nil, err | 获取玩家对象内存地址 |
| `M.GetPlayerinfo()` | table/nil, err | 获取玩家完整信息（血量/蓝量/坐标等） |

### 场景对象遍历

| 函数 | 返回 | 说明 |
|------|------|------|
| `M.EnumMonster()` | table/nil, err | 遍历周围怪物，返回怪物信息数组 |
| `M.EnumGroundItem()` | table/nil, err | 遍历地面掉落物，返回物品信息数组 |
| `M.EnumPortal()` | table/nil, err | 遍历周围传送门，返回传送门信息数组 |
| `M.EnumNPC()` | table/nil, err | 遍历周围战斗NPC，返回NPC信息数组 |
| `M.EnumInteractiveItem()` | table/nil, err | 遍历周围交互物品，返回交互物品信息数组 |

### UI控件遍历

| 函数 | 返回 | 说明 |
|------|------|------|
| `M.EnumCButton()` | table | 遍历所有可见按钮，返回按钮信息数组 |
| `M.EnumCText()` | table | 遍历所有可见文本控件，返回文本信息数组 |
| `M.EnumCImage()` | table | 遍历所有可见图片控件，返回图片信息数组 |

### 交互操作

| 函数 | 返回 | 说明 |
|------|------|------|
| `M.control_click(obj_rcx)` | any | 点击UI控件，obj_rcx=控件对象地址 |
| `M.MoveTo(x, y)` | any/nil, err | 寻路到目标坐标，x/y=目标浮点坐标 |
| `M.IsMainInterface()` | bool | 判断当前是否为游戏主界面 |
| `M.GetCurrentSelected()` | table,nil | 获取当前鼠标选中按钮 |
| `M.Isloading()` | bool | 获取是否过图中状态 1-过图  0-未过图 |
| `M.GetMainTaskPos()` | table | x,y,z | 获取当前主任务目的地坐标点(需要手动点击一下主任务的按钮控件) |
| `M.GetMainTaskPath()` | table | {x,y,z} | 获取当前主任务到目的地的路径点数组(需要手动点击一下主任务的按钮控件) |




### 注意事项
1. 偏移量需与游戏版本匹配，否则会导致内存读取失败或游戏崩溃
2. 所有API调用前必须先执行`init.InitGameinfo`完成初始化
3. 驱动加载需要有效的LICENSE_KEY
4. 坐标、血量等返回值为原生数值，可直接用于逻辑判断或界面绘制
