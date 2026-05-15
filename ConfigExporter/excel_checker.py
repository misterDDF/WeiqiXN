# -*- coding: utf-8 -*-
"""
Excel 数据校验层
"""

from __future__ import annotations

from typing import Dict, List, Optional, Tuple

from data_checker import DataChecker
from excel_common import HeaderInfo, ValidatedSheetResult, ValidationError
from excel_loader import ExcelLoader
from header_checker import HeaderChecker


class ExcelChecker:
    def __init__(self, loader: ExcelLoader):
        self.loader = loader
        self.header_checker = HeaderChecker()
        self.data_checker = DataChecker()
        self._sheet_results: Dict[str, ValidatedSheetResult] = {}

    def get_valid_sheets(self) -> List[str]:
        return self.loader.get_valid_sheets()

    def parse_headers(self, sheet_name: str = "") -> List[HeaderInfo]:
        sheet = self.loader.get_sheet_cache(sheet_name)
        return self.header_checker.parse_headers(sheet_name, sheet.rows)

    def validate_data(self, headers: List[HeaderInfo], sheet_name: str = "") -> Dict[str, Dict[str, object]]:
        sheet = self.loader.get_sheet_cache(sheet_name)
        return self.data_checker.validate_data(sheet_name, headers, sheet.rows[4:])

    def check_sheet(self, sheet_name: str) -> Tuple[bool, str, int]:
        try:
            headers = self.parse_headers(sheet_name)
            data = self.validate_data(headers, sheet_name)
            result = ValidatedSheetResult(
                sheet_name=sheet_name,
                success=True,
                message=f"[{sheet_name}] 检查通过，共 {len(data)} 条数据",
                count=len(data),
                headers=headers,
                data=data,
            )
            self._sheet_results[sheet_name] = result
            return True, result.message, result.count
        except ValidationError as e:
            result = ValidatedSheetResult(
                sheet_name=sheet_name,
                success=False,
                message=f"校验失败: {e}",
                count=0,
                headers=None,
                data=None,
            )
            self._sheet_results[sheet_name] = result
            return False, result.message, result.count
        except Exception as e:
            result = ValidatedSheetResult(
                sheet_name=sheet_name,
                success=False,
                message=f"[{sheet_name}] 未知错误: {str(e)}",
                count=0,
                headers=None,
                data=None,
            )
            self._sheet_results[sheet_name] = result
            return False, result.message, result.count

    def check_all(self) -> List[Tuple[str, bool, str, int]]:
        results = []
        for sheet_name in self.get_valid_sheets():
            success, message, count = self.check_sheet(sheet_name)
            results.append((sheet_name, success, message, count))
            if not success:
                break
        return results

    def get_sheet_result(self, sheet_name: str) -> Optional[ValidatedSheetResult]:
        return self._sheet_results.get(sheet_name)

    def get_validated_results(self) -> Dict[str, ValidatedSheetResult]:
        return self._sheet_results.copy()
