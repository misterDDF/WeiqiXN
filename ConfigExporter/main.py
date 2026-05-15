#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Excel 配置导出工具
检查 Excel 数据并导出为 JSON 和 C#
"""

import sys
from pathlib import Path

try:
    import openpyxl
except ImportError:
    print("错误: 缺少必要的依赖库，请先运行 setup.bat")
    input("按回车键退出...")
    sys.exit(1)

from excel_checker import ExcelChecker
from excel_exporter import ExcelExporter, check_datajson_link, prompt_setup_for_datajson
from excel_loader import ExcelLoader


def main():
    if len(sys.argv) < 2:
        print("用法: python main.py <excel文件>")
        print("示例: python main.py test.xlsx")
        sys.exit(1)

    input_name = sys.argv[1]
    if not input_name.lower().endswith('.xlsx'):
        input_name += '.xlsx'

    xlsx_file = Path(__file__).parent / 'xlsx' / input_name
    if not xlsx_file.exists():
        print(f"错误: 未找到文件 '{input_name}'")
        sys.exit(1)

    print(f"正在检查并导出: {input_name}")
    print("-" * 50)

    loader = ExcelLoader(xlsx_file)
    try:
        loader.load()
        checker = ExcelChecker(loader)

        valid_sheets = checker.get_valid_sheets()
        if not valid_sheets:
            print("错误: 没有找到有效的sheet")
            sys.exit(1)

        print(f"找到 {len(valid_sheets)} 个有效sheet")
        print("-" * 50)

        check_results = checker.check_all()
        failed = [item for item in check_results if not item[1]]
        for sheet_name, success, message, count in check_results:
            status = "通过" if success else "失败"
            print(f"[{sheet_name}] {status}: {message}")

        if failed:
            print("-" * 50)
            print("检查未通过，终止导出")
            sys.exit(1)

        if not check_datajson_link():
            prompt_setup_for_datajson()
            sys.exit(1)

        print("检查通过，开始导出...")
        exporter = ExcelExporter(xlsx_file, valid_sheets, checker.get_validated_results())
        export_results = exporter.export_all()

        success_count = 0
        fail_count = 0
        for sheet_name, success, message, json_path, cs_path in export_results:
            if success:
                print(f"[{sheet_name}] 导出成功")
                print(f"         JSON: {json_path}")
                print(f"         C#:   {cs_path}")
                success_count += 1
            else:
                print(f"[{sheet_name}] 导出失败: {message}")
                fail_count += 1

        print("-" * 50)
        print(f"完成: {success_count} 成功, {fail_count} 失败")
        sys.exit(0 if fail_count == 0 else 1)
    finally:
        loader.close()


if __name__ == '__main__':
    main()
