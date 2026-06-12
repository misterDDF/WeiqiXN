public static class ChineseNumberText
{
    private static readonly string[] Digits =
    {
        "零", "一", "二", "三", "四", "五", "六", "七", "八", "九"
    };

    public static string FormatInteger(int value)
    {
        if (value < 0) {
            return $"负{FormatInteger(-value)}";
        }

        if (value < 10) {
            return Digits[value];
        }

        if (value < 100) {
            int tens = value / 10;
            int ones = value % 10;
            string tensText = tens == 1 ? "十" : $"{Digits[tens]}十";
            return ones == 0 ? tensText : $"{tensText}{Digits[ones]}";
        }

        if (value < 1000) {
            int hundreds = value / 100;
            int remainder = value % 100;
            if (remainder == 0) {
                return $"{Digits[hundreds]}百";
            }

            string separator = remainder < 10 ? "零" : string.Empty;
            return $"{Digits[hundreds]}百{separator}{FormatInteger(remainder)}";
        }

        return value.ToString();
    }
}
