# -*- coding: utf-8 -*-
"""
数据区校验器
"""

from __future__ import annotations

from typing import Any, Dict, List, Tuple

from checker import ColumnChecker
from excel_common import (
    BASIC_CONVERTERS,
    BASIC_VALIDATORS,
    HeaderInfo,
    ValidationError,
    _get_inner_type,
    _get_tuple_types,
    _is_list_type,
    _is_tuple_type,
    _parse_list_value,
    _parse_tuple_value,
)


class DataChecker:
    def _validate_type(self, value: Any, type_name: str) -> Tuple[bool, str]:
        if value is None or value == '':
            return False, "值不能为空"

        if _is_list_type(type_name):
            return self._validate_list(value, type_name)
        if _is_tuple_type(type_name):
            return self._validate_tuple(value, type_name)
        if type_name not in BASIC_VALIDATORS:
            return False, f"不支持的数据类型: {type_name}"

        try:
            validator = BASIC_VALIDATORS[type_name]
            if not validator(value):
                return False, f"值'{value}' 类型不匹配，期望 {type_name}"
            return True, ""
        except (ValueError, TypeError) as e:
            return False, f"值'{value}' 无法转换为 {type_name}: {str(e)}"

    def _validate_list(self, value: Any, type_name: str) -> Tuple[bool, str]:
        if not isinstance(value, str):
            return False, f"值'{value}' 类型不匹配，期望 {type_name}"
        try:
            elements = _parse_list_value(value)
        except ValueError as e:
            return False, f"列表格式错误: {str(e)}"

        inner_type = _get_inner_type(type_name)
        for i, elem in enumerate(elements):
            is_valid, error_msg = self._validate_type(elem, inner_type)
            if not is_valid:
                return False, f"列表第{i + 1}个元素: {error_msg}"
        return True, ""

    def _validate_tuple(self, value: Any, type_name: str) -> Tuple[bool, str]:
        if not isinstance(value, str):
            return False, f"值'{value}' 类型不匹配，期望 {type_name}"
        try:
            elements = _parse_tuple_value(value)
        except ValueError as e:
            return False, f"元组格式错误: {str(e)}"

        tuple_types = _get_tuple_types(type_name)
        if len(elements) != len(tuple_types):
            return False, f"元组元素个数不匹配，期望{len(tuple_types)}个，实际{len(elements)}个"

        for i, (elem, expected_type) in enumerate(zip(elements, tuple_types)):
            is_valid, error_msg = self._validate_type(elem, expected_type)
            if not is_valid:
                return False, f"元组第{i + 1}个元素: {error_msg}"
        return True, ""

    def _convert_value(self, value: Any, type_name: str) -> Any:
        if _is_list_type(type_name):
            return self._convert_list(value, type_name)
        if _is_tuple_type(type_name):
            return self._convert_tuple(value, type_name)
        if type_name not in BASIC_CONVERTERS:
            return value
        return BASIC_CONVERTERS[type_name](value)

    def _convert_list(self, value: Any, type_name: str) -> List[Any]:
        if not isinstance(value, str):
            raise ValueError("值必须是字符串类型")
        elements = _parse_list_value(value)
        inner_type = _get_inner_type(type_name)
        return [self._convert_value(elem, inner_type) for elem in elements]

    def _convert_tuple(self, value: Any, type_name: str) -> List[Any]:
        if not isinstance(value, str):
            raise ValueError("值必须是字符串类型")
        elements = _parse_tuple_value(value)
        tuple_types = _get_tuple_types(type_name)
        return [self._convert_value(elem, t) for elem, t in zip(elements, tuple_types)]

    def validate_data(
        self,
        sheet_name: str,
        headers: List[HeaderInfo],
        data_rows: List[List[Any]],
        start_row: int = 5,
    ) -> Dict[str, Dict[str, Any]]:
        if not headers:
            return {}

        data_dict: Dict[str, Dict[str, Any]] = {}
        col_values: Dict[int, List[Any]] = {header.col: [] for header in headers}
        first_col = headers[0].col

        for row_offset, row_values in enumerate(data_rows, start=start_row):
            row_data: Dict[str, Any] = {'_row': row_offset}
            is_empty_row = True

            first_value = row_values[first_col - 1] if first_col - 1 < len(row_values) else None
            if first_value is None or str(first_value).strip() == '':
                raise ValidationError(row_offset, first_col, "第一列（作为主键）不能为空", sheet_name)

            first_value_str = str(first_value).strip()
            if first_value_str in data_dict:
                raise ValidationError(row_offset, first_col, f"键'{first_value_str}' 与之前行重复", sheet_name)

            for header in headers:
                col = header.col
                value = row_values[col - 1] if col - 1 < len(row_values) else None
                col_values[col].append(value)

                if value is None or str(value).strip() == '':
                    continue

                is_empty_row = False
                is_valid, error_msg = self._validate_type(value, header.type)
                if not is_valid:
                    raise ValidationError(row_offset, col, error_msg, sheet_name)

                try:
                    row_data[header.key] = self._convert_value(value, header.type)
                except (ValueError, TypeError) as e:
                    raise ValidationError(row_offset, col, f"值转换失败: {str(e)}", sheet_name)

            if not is_empty_row:
                del row_data['_row']
                data_dict[first_value_str] = row_data

        self._run_extra_checkers(sheet_name, headers, col_values, start_row)
        return data_dict

    def _run_extra_checkers(
        self,
        sheet_name: str,
        headers: List[HeaderInfo],
        col_values: Dict[int, List[Any]],
        start_row: int,
    ) -> None:
        for header in headers:
            if not header.checkers:
                continue

            values = col_values.get(header.col, [])
            for func_name, args in header.checkers:
                checker_cls = ColumnChecker.get_checker(func_name)
                if checker_cls is None:
                    raise ValidationError(start_row - 1, header.col, f"不支持的特殊检查 #{func_name}", sheet_name)

                passed, error_msg = checker_cls.check(
                    values,
                    header.col,
                    header.key,
                    sheet_name,
                    col_type=header.type,
                    args=args,
                )
                if not passed:
                    if checker_cls.name == 'unique':
                        seen: Dict[str, int] = {}
                        dup_row = None
                        for i, v in enumerate(values, start=start_row):
                            v_str = str(v).strip()
                            if v_str in seen:
                                dup_row = i
                                break
                            seen[v_str] = i
                        if dup_row is not None:
                            raise ValidationError(dup_row, header.col, error_msg, sheet_name)
                    else:
                        raise ValidationError(start_row - 1, header.col, error_msg, sheet_name)
