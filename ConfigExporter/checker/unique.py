# -*- coding: utf-8 -*-
"""
唯一性检查器
检查该列所有非空值是否唯一
"""

from typing import Any, List, Tuple

from checker.base import BaseChecker, ColumnChecker


class UniqueChecker(BaseChecker):
    """#unique - 检查该列所有非空值是否唯一"""

    name = 'unique'

    @classmethod
    def check(cls, values: List[Any], col: int, key: str, sheet_name: str,
              col_type: str = 'string', args: str = '') -> Tuple[bool, str]:
        seen = {}
        for i, value in enumerate(values):
            if value is None or str(value).strip() == '':
                continue

            value_str = str(value).strip()
            if value_str in seen:
                return False, f"值 '{value_str}' 与第{seen[value_str]}行重复"
            seen[value_str] = i + 5  # 数据从第5行开始
        return True, ""


ColumnChecker.register(UniqueChecker)
