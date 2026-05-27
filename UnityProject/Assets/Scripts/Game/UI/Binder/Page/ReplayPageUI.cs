// This file was manually created for the first replay page implementation.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ReplayPageUI : UIBinderBase
{
    public TextMeshProUGUI txt_title;
    public TextMeshProUGUI txt_summary;
    public TextMeshProUGUI txt_status;
    public TextMeshProUGUI txt_board;
    public TextMeshProUGUI txt_move_cursor;
    public TextMeshProUGUI txt_move_detail;
    public TextMeshProUGUI txt_analysis_placeholder;
    public TextMeshProUGUI txt_scrub_preview;
    public RectTransform rect_chart_area;
    public Image img_move_scrubber_hit;
    public Image img_chart_cursor;
    public ReplayAnalysisChartGraphic chart_analysis;
    public Button btn_close;
    public Button btn_first;
    public Button btn_prev;
    public Button btn_next;
    public Button btn_last;
    public Button btn_try_mode;
    public Button btn_ai_analysis;
}
