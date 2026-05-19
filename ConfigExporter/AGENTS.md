# AGENTS.md

## 1. 导表工具用法

### 正式入口

当前导表工具的唯一正式入口是：

- `main.py`

推荐命令：

```bash
python main.py scene.xlsx
```

也可以省略后缀：

```bash
python main.py scene
```

### 依赖与目录

首次使用前先运行：

```bat
setup.bat
```

作用：

- 安装 `openpyxl`
- 创建 `DataJson` 符号链接
- 创建 `DataType` 符号链接

Excel 文件统一放在：

```text
xlsx/
```

### 导表示例

假设存在：

```text
xlsx/scene.xlsx
```

执行：

```bash
python main.py scene.xlsx
```

流程会：

1. 加载 `xlsx/scene.xlsx`
2. 校验所有不以 `#` 开头的 sheet
3. 全部通过后导出：
   - `DataJson/scene/scene.json`
   - `DataType/scene/SceneDataType.cs`

### Excel 约定

每个有效 sheet：

1. 第 1 行：显示名
2. 第 2 行：字段名 `key`
3. 第 3 行：字段类型
4. 第 4 行：额外检查器
5. 第 5 行开始：数据

支持类型：

- `string`
- `int`
- `float`
- `boolean`
- `list(...)`
- `tuple(...)`

规则补充：

- `boolean` 只接受 `True/False`，大小写不敏感
- 第一列是主键列，不能为空，且必须带 `#require#unique`
- 数据区不允许空行

---

## 2. 当前代码模块

当前采用三层结构：

1. `ExcelLoader`
2. `ExcelChecker`
3. `ExcelExporter`

### 读取层

文件：

- `excel_loader.py`

职责：

- `load_workbook()`
- 一次性读取每个 sheet 的全部行
- 把 sheet 缓存成内存二维数组

说明：

- 后续校验和导出尽量不要再直接调用 `openpyxl` 单元格读取接口

### 校验层

文件：

- `excel_checker.py`
- `header_checker.py`
- `data_checker.py`
- `checker/`

职责：

- `ExcelChecker`：调度校验流程
- `HeaderChecker`：校验前 4 行表头
- `DataChecker`：校验数据区并做类型转换
- `checker/`：列级检查器

当前内置检查器：

- `#require`
- `#unique`
- `#enum(a,b,c)`

校验结果通过 `ValidatedSheetResult` 传递，定义在：

- `excel_common.py`

### 导出层

文件：

- `excel_exporter.py`
- `csharp_data_template.md`

职责：

- 消费校验后的 `validated_results`
- 导出 JSON
- 导出 C#

输出位置：

- `DataJson/<excel_name>/<sheet_name>.json`
- `DataType/<excel_name>/<ClassName>.cs`

C# 导出通过模板文件完成，占位符包括：

- `${class_name}`
- `${json_file_name}`
- `${dict_class_name}`
- `${fields}`

### 入口层

文件：

- `main.py`

职责：

- 串联三层流程
- 管理生命周期

执行顺序：

1. `loader.load()`
2. `checker.check_all()`
3. `exporter.export_all()`
4. `finally: loader.close()`

---

## 3. 后续开发注意事项

### Agent 编码规范

- 代码结构必须简洁明确，优先沿用当前 `ExcelLoader`、`ExcelChecker`、`ExcelExporter` 三层结构。
- 不做过度设计，不为了未来可能出现的需求提前增加抽象层、适配层或包装层。
- 不做过度封装；只有当封装能减少真实复杂度、消除重复、稳定边界或提升可读性时才新增公共方法、类或模块。
- 同一逻辑出现超过 3 行重复代码时，必须进行必要的封装复用；封装后仍应保持调用关系直观。
- 新增逻辑必须放在对应职责层内，不因复用而打破读取、校验、导出三层边界。
- 修改必须聚焦当前任务范围，不做无关重构、无关格式化或无关文件整理。
- 不为小范围需求引入新依赖、新框架或新全局工具；确实需要时必须说明现有能力为什么不足。
- 错误信息必须能定位到 Excel 文件、sheet、行列或字段；不要吞掉异常后只输出笼统失败。
- 不手改 `DataJson/` 或 `DataType/` 下的导出结果来绕过导表逻辑；需要改变输出时优先修改模板、校验或导出代码。
- 修改字段类型、检查器或 C# 生成规则时，需要同步判断 Excel 约定、类型转换、C# 类型映射和编译检查是否受影响。

### 编译检查

新增或修改 Python 代码后，必须至少通过一次编译检查：

```bash
python -m py_compile main.py excel_common.py excel_loader.py header_checker.py data_checker.py excel_checker.py excel_exporter.py checker\base.py checker\require.py checker\unique.py checker\enum.py
```

如果新增了新的 `.py` 文件，也要加入检查。

### 尽量少调用 openpyxl 接口

这是当前工具的核心约束：

- 不要在校验层或导出层反复调用 `ws.cell(...)`
- 不要在新增逻辑里重新 `load_workbook()`
- 优先基于 `ExcelLoader` 缓存出来的 `rows` 处理数据

### 保持三层职责边界

- `ExcelLoader` 只负责读取和缓存
- `ExcelChecker` 只负责校验
- `ExcelExporter` 只负责导出

避免以下做法：

- 在 exporter 中重新校验 Excel
- 在 checker 中写 JSON/C# 文件
- 在 loader 中加入业务校验逻辑

### 扩展规则

如果新增字段类型：

- 优先修改 `excel_common.py`
- 保持 validator 和 converter 规则一致
- 同步检查 C# 类型映射

如果新增列检查器：

- 在 `checker/` 下新增文件
- 继承 `BaseChecker`
- 使用 `ColumnChecker.register(...)` 注册
- 尽量基于整列数组处理

### 修改 C# 生成逻辑

优先修改：

- `csharp_data_template.md`

如需新增模板变量，再同步修改：

- `excel_exporter.py`
