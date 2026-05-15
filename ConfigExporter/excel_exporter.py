# -*- coding: utf-8 -*-
"""
Excel 导出层

职责：
- 消费 ExcelLoader 提供的 sheet 列表
- 消费校验后的结果
- 输出 JSON
- 输出 C# 数据类
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from excel_common import HeaderInfo, ValidatedSheetResult, ValidationError, _get_inner_type, _get_tuple_types, _is_list_type, _is_tuple_type


def to_camel_case(name: str) -> str:
    parts = name.split('_')
    return parts[0] + ''.join(word.capitalize() for word in parts[1:])


def type_to_csharp(type_name: str) -> str:
    type_map = {
        'string': 'string',
        'int': 'int',
        'float': 'float',
        'boolean': 'bool',
    }
    if _is_list_type(type_name):
        inner_type = _get_inner_type(type_name)
        cs_inner = type_map.get(inner_type, inner_type)
        return f"{cs_inner}[]"
    if _is_tuple_type(type_name):
        tuple_types = _get_tuple_types(type_name)
        cs_types = ', '.join(type_map.get(t, t) for t in tuple_types)
        return f"({cs_types})"
    return type_map.get(type_name, type_name)


def render_template(template_text: str, replacements: Dict[str, str]) -> str:
    rendered = template_text
    for key, value in replacements.items():
        rendered = rendered.replace(f"${{{key}}}", value)
    return rendered


class ExcelExporter:
    def __init__(
        self,
        excel_path: str | Path,
        valid_sheets: List[str],
        validated_results: Dict[str, ValidatedSheetResult],
    ):
        self.excel_path = Path(excel_path)
        self.excel_name = self.excel_path.stem
        self.valid_sheets = valid_sheets
        # 校验层产出的已验证结果缓存：
        # - key 为 sheet_name
        # - value 包含已通过校验的 headers 和 data
        # exporter 直接消费这份结果，不再重新解析 loader 中的原始行数据
        self.validated_results = validated_results

    def export_to_json(self, data: dict, output_path: str | Path,
                       indent: int = 2, ensure_ascii: bool = False) -> str:
        output_path = Path(output_path)
        json_str = json.dumps(data, indent=indent, ensure_ascii=ensure_ascii)
        with open(output_path, 'w', encoding='utf-8') as f:
            f.write(json_str)
        return json_str

    def excel_to_csharp(self, headers: List[HeaderInfo], sheet_name: str, output_dir: Path) -> Tuple[bool, str, Optional[Path]]:
        try:
            def capitalize(name: str) -> str:
                if not name:
                    return name
                return name[0].upper() + name[1:]

            if sheet_name == self.excel_name:
                class_name = f"{capitalize(to_camel_case(self.excel_name))}DataType"
            else:
                class_name = f"{capitalize(to_camel_case(self.excel_name))}{capitalize(to_camel_case(sheet_name))}DataType"

            json_file_name = sheet_name
            template_path = Path(__file__).parent / 'csharp_data_template.md'
            if not template_path.exists():
                raise FileNotFoundError(f"未找到C#模板文件: {template_path}")
            template_text = template_path.read_text(encoding='utf-8')

            field_lines = []
            for header in headers:
                field_name = header.key
                type_name = header.type
                display_name = header.display_name
                cs_type = type_to_csharp(type_name)
                comment = f"  // {display_name}" if display_name else ""
                field_lines.append(f"    public {cs_type} {field_name};{comment}")

            dict_class_name = class_name.replace("DataType", "") + "Dict"
            cs_code = render_template(
                template_text,
                {
                    "class_name": class_name,
                    "json_file_name": json_file_name,
                    "dict_class_name": dict_class_name,
                    "fields": "\n".join(field_lines),
                }
            )

            output_dir.mkdir(parents=True, exist_ok=True)
            output_path = output_dir / f"{class_name}.cs"
            with open(output_path, 'w', encoding='utf-8') as f:
                f.write(cs_code)

            return True, "", output_path
        except Exception as e:
            return False, str(e), None

    def export_sheet(self, sheet_name: str, output_dir: Optional[Path] = None) -> Tuple[bool, str, Optional[Path], Optional[Path]]:
        try:
            result = self.validated_results.get(sheet_name)
            if result is None or not result.success:
                raise ValidationError(4, 1, f"工作表 {sheet_name} 缺少已校验成功的数据", sheet_name)

            headers = result.headers
            data = result.data
            if headers is None or data is None:
                raise ValidationError(4, 1, f"工作表 {sheet_name} 缺少可导出的缓存数据", sheet_name)

            if output_dir is None:
                script_dir = Path(__file__).parent
                output_dir = script_dir / 'DataJson' / self.excel_name

            output_dir.mkdir(parents=True, exist_ok=True)

            json_path = output_dir / f"{sheet_name}.json"
            self.export_to_json(data, json_path)

            cs_dir = Path(__file__).parent / 'DataType' / self.excel_name
            cs_success, cs_error, cs_path = self.excel_to_csharp(headers, sheet_name, cs_dir)
            if not cs_success:
                return False, cs_error, None, None

            return True, f"共 {len(data)} 条数据", json_path, cs_path
        except ValidationError as e:
            return False, f"校验失败: {e}", None, None
        except FileNotFoundError as e:
            return False, f"文件错误: {str(e)}", None, None
        except Exception as e:
            return False, f"未知错误: {str(e)}", None, None

    def export_all(self, output_dir: Optional[Path] = None) -> list:
        results = []
        for sheet_name in self.valid_sheets:
            success, message, json_path, cs_path = self.export_sheet(sheet_name, output_dir)
            results.append((sheet_name, success, message, json_path, cs_path))
            if not success:
                break
        return results


def check_datajson_link() -> bool:
    import os
    script_dir = Path(__file__).parent
    datajson_path = script_dir / 'DataJson'
    if not datajson_path.exists():
        return False
    try:
        return os.path.islink(datajson_path) or os.path.isdir(datajson_path)
    except:
        return False


def prompt_setup_for_datajson():
    print("""
============================================================
[错误] DataJson 符号链接不存在
============================================================

导出JSON需要将文件保存到 Unity 项目中，但链接尚未创建。
请先运行 setup.bat 创建链接：
  1. 双击运行 setup.bat
  2. 如果提示需要管理员权限，请允许

创建链接后，重新运行导出命令。
============================================================
""")
