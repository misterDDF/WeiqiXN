# -*- coding: utf-8 -*-
"""
Excel 校验相关的公共类型与工具函数
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, List, Optional, Tuple


class Highlight:
    RED = '\033[91m'
    YELLOW = '\033[93m'
    GREEN = '\033[92m'
    CYAN = '\033[96m'
    BOLD = '\033[1m'
    END = '\033[0m'

    @classmethod
    def red(cls, text: str) -> str:
        return f"{cls.RED}{text}{cls.END}"

    @classmethod
    def yellow(cls, text: str) -> str:
        return f"{cls.YELLOW}{text}{cls.END}"

    @classmethod
    def green(cls, text: str) -> str:
        return f"{cls.GREEN}{text}{cls.END}"

    @classmethod
    def cyan(cls, text: str) -> str:
        return f"{cls.CYAN}{text}{cls.END}"

    @classmethod
    def bold(cls, text: str) -> str:
        return f"{cls.BOLD}{text}{cls.END}"


def col_num_to_excel(col_num: int) -> str:
    result = ""
    while col_num > 0:
        col_num -= 1
        result = chr(65 + (col_num % 26)) + result
        col_num //= 26
    return result


def _validate_float(v: Any) -> bool:
    if isinstance(v, bool):
        return False
    if isinstance(v, (int, float)):
        return True
    try:
        float(v)
        return True
    except (ValueError, TypeError):
        return False


def _validate_int(v: Any) -> bool:
    if isinstance(v, bool):
        return False
    if isinstance(v, int):
        return True
    try:
        float_val = float(v)
        return float_val == int(float_val)
    except (ValueError, TypeError):
        return False


def _convert_float(v: Any) -> float:
    return float(v)


def _convert_int(v: Any) -> int:
    return int(float(v))


def _convert_string(v: Any) -> str:
    return str(v)


def _validate_boolean(v: Any) -> bool:
    if isinstance(v, bool):
        return True
    if isinstance(v, str):
        return v.strip().lower() in ('true', 'false')
    return False


def _convert_boolean(v: Any) -> bool:
    if isinstance(v, bool):
        return v
    val_str = str(v).lower().strip()
    if val_str == 'true':
        return True
    if val_str == 'false':
        return False
    raise ValueError("布尔值只支持 True/False")


BASIC_TYPES = {'string', 'int', 'float', 'boolean'}

BASIC_VALIDATORS = {
    'string': lambda v: True,
    'float': _validate_float,
    'int': _validate_int,
    'boolean': _validate_boolean,
}

BASIC_CONVERTERS = {
    'string': _convert_string,
    'float': _convert_float,
    'int': _convert_int,
    'boolean': _convert_boolean,
}


def _is_list_type(type_name: str) -> bool:
    if not type_name.startswith('list(') or not type_name.endswith(')'):
        return False
    inner = type_name[5:-1]
    return inner in BASIC_TYPES


def _get_inner_type(type_name: str) -> str:
    return type_name[5:-1]


def _is_tuple_type(type_name: str) -> bool:
    if not type_name.startswith('tuple(') or not type_name.endswith(')'):
        return False
    inner = type_name[6:-1]
    if not inner.strip():
        return False
    inner_types = [t.strip() for t in inner.split(',')]
    return all(t in BASIC_TYPES for t in inner_types)


def _get_tuple_types(type_name: str) -> List[str]:
    inner = type_name[6:-1]
    return [t.strip() for t in inner.split(',')]


def _parse_tuple_value(value: str) -> List[str]:
    value = value.strip()
    if not value.startswith('[') or not value.endswith(']'):
        raise ValueError("元组必须用 [] 包裹")
    content = value[1:-1].strip()
    if not content:
        return []
    return [elem.strip() for elem in content.split(',') if elem.strip()]


def _parse_list_value(value: str) -> List[str]:
    value = value.strip()
    if not value.startswith('[') or not value.endswith(']'):
        raise ValueError("列表必须用 [] 包裹")
    content = value[1:-1].strip()
    if not content:
        return []
    return [elem.strip() for elem in content.split(',') if elem.strip()]


class ValidationError(Exception):
    def __init__(self, row: int, col: int, message: str, sheet_name: str = ""):
        self.row = row
        self.col = col
        self.col_letter = col_num_to_excel(col)
        self.message = message
        self.sheet_name = sheet_name
        self.formatted = (
            f"[{Highlight.cyan(sheet_name)}] "
            f"第{Highlight.yellow(str(row))}行第{Highlight.yellow(self.col_letter)}列: "
            f"{Highlight.red(message)}"
        )
        super().__init__(self.formatted)


@dataclass
class SheetCache:
    sheet_name: str
    rows: List[List[Any]]
    max_col: int
    max_row: int


@dataclass
class HeaderInfo:
    display_name: Any
    key: str
    type: str
    extra: str
    col: int
    checkers: List[Tuple[str, str]]


@dataclass
class ValidatedSheetResult:
    sheet_name: str
    success: bool
    message: str
    count: int
    headers: Optional[List[HeaderInfo]]
    data: Optional[Dict[str, Dict[str, Any]]]
