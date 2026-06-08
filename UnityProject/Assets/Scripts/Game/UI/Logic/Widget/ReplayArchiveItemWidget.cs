using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public class ReplayArchiveItemWidget : UIWidgetWithBinder<ReplayArchiveItemWidgetUI>
{
    public const float ItemHeight = 88f;
    public const float ItemSpacing = 8f;

    private DuelReplayIndexItem replayItem;
    private Action<DuelReplayIndexItem> clickHandler;

    public override string widgetName => UIWidget.GetWidgetName<ReplayArchiveItemWidget>();

    protected override void OnLoaded()
    {
        base.OnLoaded();

        if (binder.btn_item != null) {
            binder.btn_item.onClick.AddListener(OnClickItem);
        }
    }

    protected override void OnClose()
    {
        if (binder != null && binder.btn_item != null) {
            binder.btn_item.onClick.RemoveListener(OnClickItem);
        }

        clickHandler = null;
        base.OnClose();
    }

    public void SetData(DuelReplayIndexItem item, Action<DuelReplayIndexItem> onClick)
    {
        replayItem = item;
        clickHandler = onClick;

        if (binder.txt_title != null) {
            binder.txt_title.text = BuildTitle(item);
        }

        if (binder.txt_meta != null) {
            binder.txt_meta.text = BuildMeta(item);
        }

        if (binder.txt_result != null) {
            binder.txt_result.text = string.IsNullOrEmpty(item?.finalScore) ? "未结算" : item.finalScore;
        }

        if (binder.txt_status != null) {
            binder.txt_status.text = FormatWinner(item);
        }

        LayoutElement layoutElement = gameObject != null ? gameObject.GetComponent<LayoutElement>() : null;
        if (layoutElement != null) {
            layoutElement.minHeight = ItemHeight;
            layoutElement.preferredHeight = ItemHeight;
        }
    }

    private string BuildTitle(DuelReplayIndexItem item)
    {
        if (item == null) {
            return "空记录";
        }

        return $"黑：{FormatPlayerName(item.blackPlayerName, "黑方")}  vs  白：{FormatPlayerName(item.whitePlayerName, "白方")}";
    }

    private string BuildMeta(DuelReplayIndexItem item)
    {
        if (item == null) {
            return string.Empty;
        }

        string timeText = FormatTime(item.lastUpdatedAtUtc);
        string boardText = item.boardSize > 0 ? $"{item.boardSize} 路" : "未知棋盘";
        string sourceText = FormatSourceType(item.sourceType);
        return $"{timeText}  ·  {item.moveCount} 手  ·  {boardText}  ·  {sourceText}  ·  {FormatResultType(item.resultType)}";
    }

    private string FormatPlayerName(string playerName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(playerName)) {
            return fallback;
        }

        return playerName.Trim();
    }

    private string FormatWinner(DuelReplayIndexItem item)
    {
        if (item == null || !item.isCompleted) {
            return "进行中";
        }

        switch (item.winnerFlag) {
            case "Player1":
                return "黑胜";
            case "Player2":
                return "白胜";
            default:
                return "已完结";
        }
    }

    private string FormatResultType(string resultType)
    {
        switch (resultType) {
            case DuelGameEndReason.Score:
                return "数子终局";
            case DuelGameEndReason.ConsecutivePass:
                return "双方虚手";
            case DuelGameEndReason.Resign:
                return "认输";
            case DuelGameEndReason.Timeout:
                return "超时";
            default:
                return "对局中";
        }
    }

    private string FormatSourceType(string sourceType)
    {
        switch (sourceType) {
            case "ai":
                return "电脑对局";
            case "lan":
                return "局域网";
            case "ogs":
                return "OGS 对局";
            case "local":
                return "本地对局";
            default:
                return "对局";
        }
    }

    private string FormatTime(string utcTimeText)
    {
        if (DateTime.TryParse(
            utcTimeText,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out DateTime utcTime)) {
            return utcTime.ToLocalTime().ToString("MM-dd HH:mm");
        }

        return "--";
    }

    private void OnClickItem()
    {
        clickHandler?.Invoke(replayItem);
    }
}
