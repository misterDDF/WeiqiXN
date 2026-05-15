# -*- coding: utf-8 -*-
"""
表头校验器
"""

from __future__ import annotations

import re
from typing import Any, Dict, List, Tuple

from checker import ColumnChecker, parse_extra_checkers
from excel_common import BASIC_TYPES, HeaderInfo, ValidationError, _is_list_type, _is_tuple_type, col_num_to_excel


class HeaderChecker:
    def __init__(self, max_header_rows: int = 4):
        self.max_header_rows = max_header_rows

    def _is_valid_identifier(self, name: str) -> Tuple[bool, str]:
        if not name:
            return False, "key名不能为空"
        pattern = r'^[a-zA-Z_][a-zA-Z0-9_]*$'
        if not re.match(pattern, name):
            if name[0].isdigit():
                return False, f"key名'{name}' 不能以数字开头"
            return False, f"key名'{name}' 包含非法字符"
        return True, ""

    def _is_valid_type(self, type_name: str) -> bool:
        return type_name in BASIC_TYPES or _is_list_type(type_name) or _is_tuple_type(type_name)

    def _get_supported_types(self) -> List[str]:
        return list(BASIC_TYPES) + ['list(...)', 'tuple(...)']

    def parse_headers(self, sheet_name: str, rows: List[List[Any]]) -> List[HeaderInfo]:
        if len(rows) < self.max_header_rows:
            raise ValidationError(1, 1, "Excel 文件表头行数不足", sheet_name)

        header_rows = rows[:self.max_header_rows]
        max_col = max((len(r) for r in header_rows), default=0)
        headers: List[HeaderInfo] = []
        seen_keys: Dict[str, int] = {}

        for col_idx in range(max_col):
            col = col_idx + 1
            display_name = header_rows[0][col_idx] if col_idx < len(header_rows[0]) else None
            key = header_rows[1][col_idx] if col_idx < len(header_rows[1]) else None
            type_name = header_rows[2][col_idx] if col_idx < len(header_rows[2]) else None
            extra = header_rows[3][col_idx] if col_idx < len(header_rows[3]) else None

            if extra is None:
                raise ValidationError(4, col, "第4行不能为空", sheet_name)
            extra_str = str(extra).strip()
            if not extra_str.startswith('#'):
                raise ValidationError(4, col, "第4行必须以#开头", sheet_name)

            if key is None or str(key).strip() == '':
                raise ValidationError(2, col, "key名不能为空", sheet_name)
            key_str = str(key).strip()
            if key_str.startswith('#'):
                continue

            is_valid, error_msg = self._is_valid_identifier(key_str)
            if not is_valid:
                raise ValidationError(2, col, error_msg, sheet_name)

            type_str = str(type_name).strip().lower() if type_name else ''
            if not self._is_valid_type(type_str):
                raise ValidationError(
                    3,
                    col,
                    f"不支持的数据类型 '{type_name}'，支持 {self._get_supported_types()}",
                    sheet_name
                )

            checkers = parse_extra_checkers(extra_str)
            for func_name, args in checkers:
                checker = ColumnChecker.get_checker(func_name)
                if checker is None:
                    raise ValidationError(4, col, f"不支持的特殊检查 #{func_name}", sheet_name)
                if func_name == 'enum' and not args.strip():
                    raise ValidationError(4, col, "enum() 缺少参数，格式 #enum(a,b,c)", sheet_name)

            if key_str in seen_keys:
                first_col = seen_keys[key_str]
                raise ValidationError(
                    2,
                    col,
                    f"key名'{key_str}' 与第{col_num_to_excel(first_col)}列重复，请使用不同的key名",
                    sheet_name
                )
            seen_keys[key_str] = col
            headers.append(
                HeaderInfo(
                    display_name=display_name,
                    key=key_str,
                    type=type_str,
                    extra=extra_str,
                    col=col,
                    checkers=checkers,
                )
            )

        if headers:
            first_col = headers[0]
            first_col_checker_names = {name for name, _ in first_col.checkers}
            missing_checkers = []
            if 'require' not in first_col_checker_names:
                missing_checkers.append('#require')
            if 'unique' not in first_col_checker_names:
                missing_checkers.append('#unique')
            if missing_checkers:
                raise ValidationError(
                    4,
                    first_col.col,
                    f"第一列（作为主键）必须包含 {' 和 '.join(missing_checkers)} 检查器",
                    sheet_name
                )

        return headers
