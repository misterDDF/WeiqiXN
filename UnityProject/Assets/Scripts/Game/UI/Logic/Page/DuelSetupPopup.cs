using UnityEngine.UI;
using System;
using System.Collections.Generic;
using TMPro;

public class DuelSetupPopup : UIPageWithBinder<DuelSetupPopupUI>
{
    public override string pageName => UIPage.GetPageName<DuelSetupPopup>();
    private const string InfiniteHoldTimeCfgId = "infinite";
    private const string ByoyomiOffCfgId = "off";
    private const string DefaultAiDifficultyCfgId = "k20_k15";

    private static bool pendingOpenAiDuel;
    private static Action<DuelSceneCreateParamas> pendingConfirmHandler;

    private string selectedBoardCfgId = "9x9";
    private string selectedHoldTimeCfgId = "5m";
    private string selectedByoyomiCountCfgId = ByoyomiOffCfgId;
    private string selectedByoyomiTimeCfgId = "30s";
    private bool isAiDuel;
    private Action<DuelSceneCreateParamas> confirmHandler;
    private string selectedAiDifficultyCfgId = DefaultAiDifficultyCfgId;
    private readonly List<string> aiDifficultyCfgIds = new List<string>();

    public static void Open(bool isAiDuel)
    {
        pendingOpenAiDuel = isAiDuel;
        pendingConfirmHandler = null;
        Global.Instance.uiManager.ShowPage<DuelSetupPopup>();
    }

    public static void OpenForLanRoom(Action<DuelSceneCreateParamas> onConfirmed)
    {
        pendingOpenAiDuel = false;
        pendingConfirmHandler = onConfirmed;
        Global.Instance.uiManager.ShowPage<DuelSetupPopup>();
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        AddButtonListener(binder.btn_9x9, () => SelectBoard("9x9"));
        AddButtonListener(binder.btn_13x13, () => SelectBoard("13x13"));
        AddButtonListener(binder.btn_19x19, () => SelectBoard("19x19"));
        BindTimeControlButtons();
        BindAiDifficultyDropdown();
        AddButtonListener(binder.btn_start, OnClickBtnStart);
        AddButtonListener(binder.btn_close, OnClickBtnClose);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        isAiDuel = pendingOpenAiDuel;
        confirmHandler = pendingConfirmHandler;
        pendingConfirmHandler = null;
        RefreshAiDifficultyDropdown();
        RefreshSelectionState();
    }

    public void OnClickBtn9x9()
    {
        SelectBoard("9x9");
    }

    public void OnClickBtn13x13()
    {
        SelectBoard("13x13");
    }

    public void OnClickBtn19x19()
    {
        SelectBoard("19x19");
    }

    public void OnClickBtnStart()
    {
        NormalizeTimeControlSelection();

        DuelSceneCreateParamas duelParams = new DuelSceneCreateParamas()
        {
            boardCfgId = selectedBoardCfgId,
            holdTimeCfgId = selectedHoldTimeCfgId,
            byoyomiCountCfgId = selectedByoyomiCountCfgId,
            byoyomiTimeCfgId = selectedByoyomiTimeCfgId,
            isAiDuel = isAiDuel,
            aiDifficultyCfgId = isAiDuel ? selectedAiDifficultyCfgId : string.Empty,
        };

        if (confirmHandler != null) {
            Action<DuelSceneCreateParamas> handler = confirmHandler;
            confirmHandler = null;
            ClosePage();
            handler.Invoke(duelParams);
            return;
        }

        SceneCreateParams sceneCreateParams = new SceneCreateParams()
        {
            duelSceneCreateParamas = duelParams
        };
        Global.Instance.sceneManager.EnterMainScene(SceneConfig.DUEL_SCENE_TYPE_ID, sceneCreateParams);
    }

    public void OnClickBtnClose()
    {
        ClosePage();
    }

    private void BindTimeControlButtons()
    {
        AddButtonListener(binder.btn_hold_time_2m, () => SelectHoldTime("2m"));
        AddButtonListener(binder.btn_hold_time_5m, () => SelectHoldTime("5m"));
        AddButtonListener(binder.btn_hold_time_10m, () => SelectHoldTime("10m"));
        AddButtonListener(binder.btn_hold_time_20m, () => SelectHoldTime("20m"));
        AddButtonListener(binder.btn_hold_time_infinite, () => SelectHoldTime("infinite"));

        AddButtonListener(binder.btn_byoyomi_count_off, () => SelectByoyomiCount("off"));
        AddButtonListener(binder.btn_byoyomi_count_1, () => SelectByoyomiCount("1"));
        AddButtonListener(binder.btn_byoyomi_count_3, () => SelectByoyomiCount("3"));
        AddButtonListener(binder.btn_byoyomi_count_5, () => SelectByoyomiCount("5"));

        AddButtonListener(binder.btn_byoyomi_time_10s, () => SelectByoyomiTime("10s"));
        AddButtonListener(binder.btn_byoyomi_time_20s, () => SelectByoyomiTime("20s"));
        AddButtonListener(binder.btn_byoyomi_time_30s, () => SelectByoyomiTime("30s"));
        AddButtonListener(binder.btn_byoyomi_time_60s, () => SelectByoyomiTime("60s"));
    }

    private void BindAiDifficultyDropdown()
    {
        if (binder.dropdown_ai_difficulty != null) {
            binder.dropdown_ai_difficulty.onValueChanged.AddListener(OnAiDifficultyDropdownValueChanged);
        }
    }

    private void SelectBoard(string boardCfgId)
    {
        selectedBoardCfgId = boardCfgId;
        RefreshSelectionState();
        if (binder.btn_start == null) {
            OnClickBtnStart();
        }
    }

    private void SelectHoldTime(string holdTimeCfgId)
    {
        selectedHoldTimeCfgId = holdTimeCfgId;
        NormalizeTimeControlSelection();
        RefreshSelectionState();
    }

    private void SelectByoyomiCount(string byoyomiCountCfgId)
    {
        if (IsInfiniteHoldTimeSelected()) {
            selectedByoyomiCountCfgId = ByoyomiOffCfgId;
            RefreshSelectionState();
            return;
        }

        selectedByoyomiCountCfgId = byoyomiCountCfgId;
        RefreshSelectionState();
    }

    private void SelectByoyomiTime(string byoyomiTimeCfgId)
    {
        if (IsInfiniteHoldTimeSelected()) {
            selectedByoyomiCountCfgId = ByoyomiOffCfgId;
            RefreshSelectionState();
            return;
        }

        selectedByoyomiTimeCfgId = byoyomiTimeCfgId;
        RefreshSelectionState();
    }

    private void NormalizeTimeControlSelection()
    {
        if (IsInfiniteHoldTimeSelected()) {
            selectedByoyomiCountCfgId = ByoyomiOffCfgId;
        }
    }

    private bool IsInfiniteHoldTimeSelected()
    {
        return selectedHoldTimeCfgId == InfiniteHoldTimeCfgId;
    }

    private void RefreshSelectionState()
    {
        SetButtonInteractable(binder.btn_9x9, selectedBoardCfgId != "9x9");
        SetButtonInteractable(binder.btn_13x13, selectedBoardCfgId != "13x13");
        SetButtonInteractable(binder.btn_19x19, selectedBoardCfgId != "19x19");

        SetButtonInteractable(binder.btn_hold_time_2m, selectedHoldTimeCfgId != "2m");
        SetButtonInteractable(binder.btn_hold_time_5m, selectedHoldTimeCfgId != "5m");
        SetButtonInteractable(binder.btn_hold_time_10m, selectedHoldTimeCfgId != "10m");
        SetButtonInteractable(binder.btn_hold_time_20m, selectedHoldTimeCfgId != "20m");
        SetButtonInteractable(binder.btn_hold_time_infinite, !IsInfiniteHoldTimeSelected());

        bool infiniteHoldTime = IsInfiniteHoldTimeSelected();
        SetButtonInteractable(binder.btn_byoyomi_count_off, !infiniteHoldTime && selectedByoyomiCountCfgId != ByoyomiOffCfgId);
        SetButtonInteractable(binder.btn_byoyomi_count_1, !infiniteHoldTime && selectedByoyomiCountCfgId != "1");
        SetButtonInteractable(binder.btn_byoyomi_count_3, !infiniteHoldTime && selectedByoyomiCountCfgId != "3");
        SetButtonInteractable(binder.btn_byoyomi_count_5, !infiniteHoldTime && selectedByoyomiCountCfgId != "5");

        bool byoyomiEnabled = !infiniteHoldTime && selectedByoyomiCountCfgId != ByoyomiOffCfgId;
        SetButtonInteractable(binder.btn_byoyomi_time_10s, byoyomiEnabled && selectedByoyomiTimeCfgId != "10s");
        SetButtonInteractable(binder.btn_byoyomi_time_20s, byoyomiEnabled && selectedByoyomiTimeCfgId != "20s");
        SetButtonInteractable(binder.btn_byoyomi_time_30s, byoyomiEnabled && selectedByoyomiTimeCfgId != "30s");
        SetButtonInteractable(binder.btn_byoyomi_time_60s, byoyomiEnabled && selectedByoyomiTimeCfgId != "60s");

        if (binder.panel_ai_difficulty != null) {
            binder.panel_ai_difficulty.SetActive(isAiDuel);
        }
    }

    private void RefreshAiDifficultyDropdown()
    {
        if (binder.dropdown_ai_difficulty == null) {
            return;
        }

        EnsureAiDifficultyOptions();
        binder.dropdown_ai_difficulty.ClearOptions();

        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        foreach (string cfgId in aiDifficultyCfgIds) {
            DuelAiDifficultyDataType data = DuelAiDifficultyDataType.GetConfigData(cfgId);
            options.Add(new TMP_Dropdown.OptionData(data != null ? data.name : cfgId));
        }

        binder.dropdown_ai_difficulty.AddOptions(options);
        int selectedIndex = aiDifficultyCfgIds.IndexOf(selectedAiDifficultyCfgId);
        if (selectedIndex < 0) {
            selectedIndex = 0;
            selectedAiDifficultyCfgId = aiDifficultyCfgIds.Count > 0 ? aiDifficultyCfgIds[0] : DefaultAiDifficultyCfgId;
        }

        binder.dropdown_ai_difficulty.SetValueWithoutNotify(selectedIndex);
        binder.dropdown_ai_difficulty.RefreshShownValue();
    }

    private void EnsureAiDifficultyOptions()
    {
        if (aiDifficultyCfgIds.Count > 0) {
            return;
        }

        DuelAiDifficultyDataType.GetConfigData(DefaultAiDifficultyCfgId);
        if (DuelAiDifficultyDataType.DuelAiDifficultyDict != null) {
            foreach (string cfgId in DuelAiDifficultyDataType.DuelAiDifficultyDict.Keys) {
                aiDifficultyCfgIds.Add(cfgId);
            }
        }

        aiDifficultyCfgIds.Sort(CompareAiDifficultyId);
        if (aiDifficultyCfgIds.Count == 0) {
            aiDifficultyCfgIds.Add(DefaultAiDifficultyCfgId);
        }
    }

    private int CompareAiDifficultyId(string a, string b)
    {
        return GetAiDifficultyOrder(a).CompareTo(GetAiDifficultyOrder(b));
    }

    private int GetAiDifficultyOrder(string cfgId)
    {
        switch (cfgId) {
            case "k20_k15": return 0;
            case "k14_k10": return 1;
            case "k9_k7": return 2;
            case "k6_k4": return 3;
            case "k3_k1": return 4;
            case "d1_d3": return 5;
            case "d4_d5": return 6;
            case "d6": return 7;
            case "pro_1p_2p": return 8;
            case "pro_3p_4p": return 9;
            case "pro_5p_6p": return 10;
            case "pro_7p_8p": return 11;
            case "pro_9p": return 12;
            default: return 1000;
        }
    }

    private void OnAiDifficultyDropdownValueChanged(int index)
    {
        if (index >= 0 && index < aiDifficultyCfgIds.Count) {
            selectedAiDifficultyCfgId = aiDifficultyCfgIds[index];
        }
    }

    private void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }

    private void SetButtonInteractable(Button button, bool interactable)
    {
        if (button != null) {
            button.interactable = interactable;
        }
    }
}
