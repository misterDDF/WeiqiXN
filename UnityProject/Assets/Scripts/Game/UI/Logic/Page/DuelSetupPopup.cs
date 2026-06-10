using UnityEngine.UI;
using UnityEngine;
using System;
using System.Collections.Generic;
using TMPro;

public class DuelSetupPopup : UIPageWithBinder<DuelSetupPopupUI>
{
    private enum SetupOpenMode
    {
        Local,
        Ai,
        Lan,
        Ogs,
        OgsFriend,
    }

    public override string pageName => UIPage.GetPageName<DuelSetupPopup>();
    private const string InfiniteHoldTimeCfgId = "infinite";
    private const string ByoyomiOffCfgId = "off";
    private const string DefaultOgsHoldTimeCfgId = "10m";
    private const string DefaultAiDifficultyCfgId = "k20_k18";
    private const string PlayerSideGuess = "guess";
    private const string PlayerSideBlack = "black";
    private const string PlayerSideWhite = "white";

    private static bool pendingOpenAiDuel;
    private static SetupOpenMode pendingOpenMode = SetupOpenMode.Local;
    private static Action<DuelSceneCreateParamas> pendingConfirmHandler;

    private bool hasAppliedLayoutState;
    private bool lastPortraitLayout;

    private string selectedBoardCfgId = "9x9";
    private string selectedHoldTimeCfgId = InfiniteHoldTimeCfgId;
    private string selectedByoyomiCountCfgId = ByoyomiOffCfgId;
    private string selectedByoyomiTimeCfgId = "30s";
    private string selectedPlayerSideCfgId = PlayerSideGuess;
    private string selectedHandicapCfgId = DuelHandicapPlacement.GetDefaultCfgId("9x9");
    private SetupOpenMode setupMode;
    private bool isAiDuel;
    private Action<DuelSceneCreateParamas> confirmHandler;
    private DuelSetupPreferenceMode setupPreferenceMode;
    private string selectedAiDifficultyCfgId = DefaultAiDifficultyCfgId;
    private readonly List<string> aiDifficultyCfgIds = new List<string>();
    private readonly List<string> playerSideCfgIds = new List<string> { PlayerSideGuess, PlayerSideBlack, PlayerSideWhite };
    private readonly List<string> handicapCfgIds = new List<string>();
    private readonly List<string> holdTimeCfgIds = new List<string>();
    private readonly List<string> byoyomiCountCfgIds = new List<string>();
    private readonly List<string> byoyomiTimeCfgIds = new List<string>();
    private readonly List<string> ogsAutomatchTimeOptionCfgIds = new List<string>();
    private bool isRefreshingTimeDropdowns;

    public static void Open(bool isAiDuel)
    {
        pendingOpenAiDuel = isAiDuel;
        pendingOpenMode = isAiDuel ? SetupOpenMode.Ai : SetupOpenMode.Local;
        pendingConfirmHandler = null;
        Global.Instance.uiManager.ShowPage<DuelSetupPopup>();
    }

    public static void OpenForLanRoom(Action<DuelSceneCreateParamas> onConfirmed)
    {
        pendingOpenAiDuel = false;
        pendingOpenMode = SetupOpenMode.Lan;
        pendingConfirmHandler = onConfirmed;
        Global.Instance.uiManager.ShowPage<DuelSetupPopup>();
    }

    public static void OpenForOgs(Action<DuelSceneCreateParamas> onConfirmed)
    {
        pendingOpenAiDuel = false;
        pendingOpenMode = SetupOpenMode.Ogs;
        pendingConfirmHandler = onConfirmed;
        Global.Instance.uiManager.ShowPage<DuelSetupPopup>();
    }

    public static void OpenForOgsFriend(Action<DuelSceneCreateParamas> onConfirmed)
    {
        pendingOpenAiDuel = false;
        pendingOpenMode = SetupOpenMode.OgsFriend;
        pendingConfirmHandler = onConfirmed;
        Global.Instance.uiManager.ShowPage<DuelSetupPopup>();
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        ApplyCurrentLayoutState(true);

        AddButtonListener(binder.btn_9x9, () => SelectBoard("9x9"));
        AddButtonListener(binder.btn_13x13, () => SelectBoard("13x13"));
        AddButtonListener(binder.btn_19x19, () => SelectBoard("19x19"));
        BindTimeControlDropdowns();
        BindPlayerColorDropdown();
        BindHandicapDropdown();
        BindAiDifficultyDropdown();
        AddButtonListener(binder.btn_start, OnClickBtnStart);
        AddButtonListener(binder.btn_close, OnClickBtnClose);
    }

    protected override void OnOpen()
    {
        base.OnOpen();

        ApplyCurrentLayoutState(false);

        setupMode = pendingOpenMode;
        isAiDuel = setupMode == SetupOpenMode.Ai || pendingOpenAiDuel;
        confirmHandler = pendingConfirmHandler;
        pendingConfirmHandler = null;
        setupPreferenceMode = ResolvePreferenceMode();
        LoadPreference();
        NormalizeSetupSelection();
        RefreshTimeControlDropdowns();
        RefreshPlayerColorDropdown();
        RefreshHandicapDropdown();
        RefreshAiDifficultyDropdown();
        RefreshSelectionState();
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        ApplyCurrentLayoutState(false);
    }

    protected override void OnClose()
    {
        NormalizeSetupSelection();
        SavePreference();
        base.OnClose();
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
        NormalizeSetupSelection();
        PlayerFlag localPlayerFlag = ResolveLocalPlayerFlag();
        PlayerFlag lanHostPlayerFlag = IsLanRoomSetup() ? ResolveLocalPlayerFlag() : PlayerFlag.Player1;

        DuelSceneCreateParamas duelParams = new DuelSceneCreateParamas()
        {
            boardCfgId = selectedBoardCfgId,
            holdTimeCfgId = selectedHoldTimeCfgId,
            byoyomiCountCfgId = selectedByoyomiCountCfgId,
            byoyomiTimeCfgId = selectedByoyomiTimeCfgId,
            handicapCfgId = selectedHandicapCfgId,
            isAiDuel = isAiDuel,
            aiDifficultyCfgId = isAiDuel ? selectedAiDifficultyCfgId : string.Empty,
            playerSideCfgId = selectedPlayerSideCfgId,
            localPlayerFlag = isAiDuel ? localPlayerFlag : 0,
            localPlayerProfile = User.Instance.compUserInfo.BuildProfileData(),
            lanHostPlayerFlag = IsLanRoomSetup() ? lanHostPlayerFlag : PlayerFlag.Player1,
            lanHostPlayerSideCfgId = IsLanRoomSetup() ? selectedPlayerSideCfgId : PlayerSideBlack,
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

    private void BindTimeControlDropdowns()
    {
        if (binder.dropdown_ogs_time_option != null) {
            binder.dropdown_ogs_time_option.onValueChanged.AddListener(OnOgsTimeOptionDropdownValueChanged);
        }
        if (binder.dropdown_hold_time != null) {
            binder.dropdown_hold_time.onValueChanged.AddListener(OnHoldTimeDropdownValueChanged);
        }
        if (binder.dropdown_byoyomi_count != null) {
            binder.dropdown_byoyomi_count.onValueChanged.AddListener(OnByoyomiCountDropdownValueChanged);
        }
        if (binder.dropdown_byoyomi_time != null) {
            binder.dropdown_byoyomi_time.onValueChanged.AddListener(OnByoyomiTimeDropdownValueChanged);
        }
    }

    private void ApplyCurrentLayoutState(bool force)
    {
        if (binder.sr_platform == null) {
            return;
        }

        bool isPortrait = UIUtils.IsPortrait(rectTransform);
        if (!force && hasAppliedLayoutState && isPortrait == lastPortraitLayout) {
            return;
        }

        binder.SetSrPlatformState(ResolveLayoutState(), force);
        hasAppliedLayoutState = true;
        lastPortraitLayout = isPortrait;
    }

    private DuelSetupPopupUI.SrPlatformState ResolveLayoutState()
    {
        return UIUtils.IsPortrait(rectTransform)
            ? DuelSetupPopupUI.SrPlatformState.Portrait
            : DuelSetupPopupUI.SrPlatformState.Landscape;
    }

    private void BindAiDifficultyDropdown()
    {
        if (binder.dropdown_ai_difficulty != null) {
            binder.dropdown_ai_difficulty.onValueChanged.AddListener(OnAiDifficultyDropdownValueChanged);
        }
    }

    private void BindPlayerColorDropdown()
    {
        if (binder.dropdown_player_color != null) {
            binder.dropdown_player_color.onValueChanged.AddListener(OnPlayerColorDropdownValueChanged);
        }
    }

    private void BindHandicapDropdown()
    {
        if (binder.dropdown_handicap != null) {
            binder.dropdown_handicap.onValueChanged.AddListener(OnHandicapDropdownValueChanged);
        }
    }

    private void SelectBoard(string boardCfgId)
    {
        if (!CanUseBoardCfg(boardCfgId)) {
            return;
        }

        selectedBoardCfgId = boardCfgId;
        NormalizeTimeControlSelection();
        RefreshTimeControlDropdowns();
        NormalizeHandicapSelection();
        RefreshHandicapDropdown();
        RefreshSelectionState();
        if (binder.btn_start == null) {
            OnClickBtnStart();
        }
    }

    private void SelectHoldTime(string holdTimeCfgId)
    {
        if (!CanUseHoldTimeCfg(holdTimeCfgId)) {
            return;
        }

        selectedHoldTimeCfgId = holdTimeCfgId;
        NormalizeTimeControlSelection();
        RefreshTimeControlDropdowns();
        RefreshSelectionState();
    }

    private void SelectByoyomiCount(string byoyomiCountCfgId)
    {
        if (IsInfiniteHoldTimeSelected()) {
            selectedByoyomiCountCfgId = ByoyomiOffCfgId;
            RefreshSelectionState();
            return;
        }
        if (!CanUseByoyomiCountCfg(byoyomiCountCfgId)) {
            return;
        }

        selectedByoyomiCountCfgId = byoyomiCountCfgId;
        NormalizeTimeControlSelection();
        RefreshTimeControlDropdowns();
        RefreshSelectionState();
    }

    private void SelectByoyomiTime(string byoyomiTimeCfgId)
    {
        if (IsInfiniteHoldTimeSelected()) {
            selectedByoyomiCountCfgId = ByoyomiOffCfgId;
            RefreshSelectionState();
            return;
        }
        if (!CanUseByoyomiTimeCfg(byoyomiTimeCfgId)) {
            return;
        }

        selectedByoyomiTimeCfgId = byoyomiTimeCfgId;
        NormalizeTimeControlSelection();
        RefreshTimeControlDropdowns();
        RefreshSelectionState();
    }

    private void NormalizeTimeControlSelection()
    {
        if (IsOgsAutomatchSetup()) {
            NormalizeOgsAutomatchTimeSelection();
            return;
        }

        if (IsInfiniteHoldTimeSelected()) {
            selectedByoyomiCountCfgId = ByoyomiOffCfgId;
        }
    }

    private void NormalizeSetupSelection()
    {
        NormalizeBoardSelection();
        NormalizeAiDifficultySelection();
        NormalizePlayerSideSelection();
        NormalizeHoldTimeSelection();
        NormalizeByoyomiCountSelection();
        NormalizeTimeControlSelection();
        NormalizeByoyomiTimeSelection();
        NormalizeHandicapSelection();
    }

    private void NormalizeBoardSelection()
    {
        if (!CanUseBoardCfg(selectedBoardCfgId)) {
            selectedBoardCfgId = ResolveDefaultBoardCfgId();
        }
    }

    private void NormalizeHandicapSelection()
    {
        if (ShouldForceEvenGameHandicap()) {
            selectedHandicapCfgId = ResolveDefaultHandicapCfgId(selectedBoardCfgId);
            return;
        }

        selectedHandicapCfgId = DuelHandicapPlacement.GetValidCfgId(selectedHandicapCfgId, selectedBoardCfgId);
        if (!CanUseHandicapCfg(selectedHandicapCfgId)) {
            selectedHandicapCfgId = ResolveDefaultHandicapCfgId(selectedBoardCfgId);
        }
    }

    private void NormalizeAiDifficultySelection()
    {
        EnsureAiDifficultyOptions();
        if (!aiDifficultyCfgIds.Contains(selectedAiDifficultyCfgId)) {
            selectedAiDifficultyCfgId = aiDifficultyCfgIds.Count > 0 ? aiDifficultyCfgIds[0] : DefaultAiDifficultyCfgId;
        }
    }

    private void NormalizePlayerSideSelection()
    {
        if (!playerSideCfgIds.Contains(selectedPlayerSideCfgId)) {
            selectedPlayerSideCfgId = PlayerSideGuess;
        }
    }

    private void NormalizeHoldTimeSelection()
    {
        if (!CanUseHoldTimeCfg(selectedHoldTimeCfgId)) {
            selectedHoldTimeCfgId = ResolveDefaultHoldTimeCfgId();
        }
    }

    private void NormalizeByoyomiCountSelection()
    {
        if (!CanUseByoyomiCountCfg(selectedByoyomiCountCfgId)) {
            selectedByoyomiCountCfgId = ByoyomiOffCfgId;
        }
    }

    private void NormalizeByoyomiTimeSelection()
    {
        if (!CanUseByoyomiTimeCfg(selectedByoyomiTimeCfgId)) {
            selectedByoyomiTimeCfgId = "30s";
        }
    }

    private bool IsInfiniteHoldTimeSelected()
    {
        return selectedHoldTimeCfgId == InfiniteHoldTimeCfgId;
    }

    private string ResolveDefaultBoardCfgId()
    {
        if (CanUseBoardCfg("9x9")) {
            return "9x9";
        }
        if (CanUseBoardCfg("13x13")) {
            return "13x13";
        }
        if (CanUseBoardCfg("19x19")) {
            return "19x19";
        }
        return "9x9";
    }

    private string ResolveDefaultHoldTimeCfgId()
    {
        if (IsAnyOgsSetup()) {
            if (CanUseHoldTimeCfg(DefaultOgsHoldTimeCfgId)) {
                return DefaultOgsHoldTimeCfgId;
            }
            if (CanUseHoldTimeCfg("5m")) {
                return "5m";
            }
            if (CanUseHoldTimeCfg("2m")) {
                return "2m";
            }
            if (CanUseHoldTimeCfg("20m")) {
                return "20m";
            }
        }

        return CanUseHoldTimeCfg(InfiniteHoldTimeCfgId) ? InfiniteHoldTimeCfgId : DefaultOgsHoldTimeCfgId;
    }

    private string ResolveDefaultHandicapCfgId(string boardCfgId)
    {
        string cfgId = DuelHandicapPlacement.GetDefaultCfgId(boardCfgId);
        if (CanUseHandicapCfg(cfgId)) {
            return cfgId;
        }

        foreach (string candidateCfgId in DuelHandicapPlacement.GetCfgIdsForBoard(boardCfgId)) {
            if (CanUseHandicapCfg(candidateCfgId)) {
                return candidateCfgId;
            }
        }

        return cfgId;
    }

    private bool CanUseBoardCfg(string cfgId)
    {
        ChessBoardDataType data = ChessBoardDataType.GetConfigData(cfgId);
        return data != null && (!IsAnyOgsSetup() || data.ogsEnabled);
    }

    private bool CanUseHoldTimeCfg(string cfgId)
    {
        DuelHoldTimeDataType data = DuelHoldTimeDataType.GetConfigData(cfgId);
        if (data == null) {
            return false;
        }
        if (IsOgsAutomatchSetup()) {
            foreach (OgsAutomatchTimeOptionDataType option in GetOgsAutomatchTimeOptionsForBoard(selectedBoardCfgId)) {
                if (option.holdTimeCfgId == cfgId) {
                    return true;
                }
            }
            return false;
        }

        return !IsAnyOgsSetup() || data.ogsEnabled;
    }

    private bool CanUseByoyomiCountCfg(string cfgId)
    {
        DuelByoyomiCountDataType data = DuelByoyomiCountDataType.GetConfigData(cfgId);
        if (data == null) {
            return false;
        }
        if (IsOgsAutomatchSetup()) {
            foreach (OgsAutomatchTimeOptionDataType option in GetOgsAutomatchTimeOptionsForBoard(selectedBoardCfgId)) {
                if (option.byoyomiCountCfgId == cfgId) {
                    return true;
                }
            }
            return false;
        }

        return !IsAnyOgsSetup() || data.ogsEnabled;
    }

    private bool CanUseByoyomiTimeCfg(string cfgId)
    {
        DuelByoyomiTimeDataType data = DuelByoyomiTimeDataType.GetConfigData(cfgId);
        if (data == null) {
            return false;
        }
        if (IsOgsAutomatchSetup()) {
            foreach (OgsAutomatchTimeOptionDataType option in GetOgsAutomatchTimeOptionsForBoard(selectedBoardCfgId)) {
                if (option.byoyomiTimeCfgId == cfgId) {
                    return true;
                }
            }
            return false;
        }

        return !IsAnyOgsSetup() || data.ogsEnabled;
    }

    private bool CanUseHandicapCfg(string cfgId)
    {
        DuelHandicapDataType data = DuelHandicapDataType.GetConfigData(cfgId);
        if (data == null || data.boardCfgId != selectedBoardCfgId) {
            return false;
        }
        if (IsOgsAutomatchSetup()) {
            return cfgId == DuelHandicapPlacement.GetDefaultCfgId(selectedBoardCfgId) && data.ogsEnabled;
        }
        if (IsOgsFriendSetup()) {
            return data.ogsFriendEnabled;
        }
        return true;
    }

    private void RefreshTimeControlDropdowns()
    {
        isRefreshingTimeDropdowns = true;
        try {
            if (IsOgsAutomatchSetup()) {
                RefreshOgsAutomatchTimeOptionDropdown();
            }
            else {
                RefreshStandardTimeDropdowns();
            }
        }
        finally {
            isRefreshingTimeDropdowns = false;
        }
    }

    private void RefreshStandardTimeDropdowns()
    {
        FillStandardHoldTimeOptions();
        if (!holdTimeCfgIds.Contains(selectedHoldTimeCfgId)) {
            selectedHoldTimeCfgId = ResolveDefaultHoldTimeCfgId();
        }

        FillStandardByoyomiCountOptions();
        if (!byoyomiCountCfgIds.Contains(selectedByoyomiCountCfgId)) {
            selectedByoyomiCountCfgId = ByoyomiOffCfgId;
        }

        FillStandardByoyomiTimeOptions();
        if (!byoyomiTimeCfgIds.Contains(selectedByoyomiTimeCfgId)) {
            selectedByoyomiTimeCfgId = byoyomiTimeCfgIds.Count > 0 ? byoyomiTimeCfgIds[0] : "30s";
        }

        RefreshDropdownOptions(binder.dropdown_hold_time, holdTimeCfgIds, selectedHoldTimeCfgId, GetHoldTimeDisplayName);
        RefreshDropdownOptions(binder.dropdown_byoyomi_count, byoyomiCountCfgIds, selectedByoyomiCountCfgId, GetByoyomiCountDisplayName);
        RefreshDropdownOptions(binder.dropdown_byoyomi_time, byoyomiTimeCfgIds, selectedByoyomiTimeCfgId, GetByoyomiTimeDisplayName);
    }

    private void RefreshOgsAutomatchTimeOptionDropdown()
    {
        NormalizeOgsAutomatchTimeSelection();
        List<OgsAutomatchTimeOptionDataType> boardOptions = GetOgsAutomatchTimeOptionsForBoard(selectedBoardCfgId);

        ogsAutomatchTimeOptionCfgIds.Clear();
        foreach (OgsAutomatchTimeOptionDataType option in boardOptions) {
            AddUnique(ogsAutomatchTimeOptionCfgIds, option.id);
        }

        OgsAutomatchTimeOptionDataType selectedOption = FindOgsAutomatchTimeOption(
            selectedBoardCfgId,
            selectedHoldTimeCfgId,
            selectedByoyomiCountCfgId,
            selectedByoyomiTimeCfgId);
        if (selectedOption == null) {
            selectedOption = ResolveDefaultOgsAutomatchTimeOption(selectedBoardCfgId);
        }
        if (selectedOption != null) {
            ApplyOgsAutomatchTimeOption(selectedOption);
        }

        string selectedOptionCfgId = selectedOption != null ? selectedOption.id : string.Empty;
        RefreshDropdownOptions(binder.dropdown_ogs_time_option, ogsAutomatchTimeOptionCfgIds, selectedOptionCfgId, GetOgsAutomatchTimeOptionDisplayName);
    }

    private void FillStandardHoldTimeOptions()
    {
        holdTimeCfgIds.Clear();
        DuelHoldTimeDataType.GetConfigData(string.Empty);
        if (DuelHoldTimeDataType.DuelHoldTimeDict == null) {
            return;
        }

        List<DuelHoldTimeDataType> options = new List<DuelHoldTimeDataType>();
        foreach (DuelHoldTimeDataType data in DuelHoldTimeDataType.DuelHoldTimeDict.Values) {
            if (data != null && CanUseHoldTimeCfg(data.id)) {
                options.Add(data);
            }
        }
        options.Sort(CompareHoldTimeData);
        foreach (DuelHoldTimeDataType data in options) {
            holdTimeCfgIds.Add(data.id);
        }
    }

    private void FillStandardByoyomiCountOptions()
    {
        byoyomiCountCfgIds.Clear();
        DuelByoyomiCountDataType.GetConfigData(string.Empty);
        if (DuelByoyomiCountDataType.DuelByoyomiCountDict == null) {
            return;
        }

        List<DuelByoyomiCountDataType> options = new List<DuelByoyomiCountDataType>();
        foreach (DuelByoyomiCountDataType data in DuelByoyomiCountDataType.DuelByoyomiCountDict.Values) {
            if (data != null && CanUseByoyomiCountCfg(data.id)) {
                options.Add(data);
            }
        }
        options.Sort((a, b) => a.count.CompareTo(b.count));
        foreach (DuelByoyomiCountDataType data in options) {
            byoyomiCountCfgIds.Add(data.id);
        }
    }

    private void FillStandardByoyomiTimeOptions()
    {
        byoyomiTimeCfgIds.Clear();
        DuelByoyomiTimeDataType.GetConfigData(string.Empty);
        if (DuelByoyomiTimeDataType.DuelByoyomiTimeDict == null) {
            return;
        }

        List<DuelByoyomiTimeDataType> options = new List<DuelByoyomiTimeDataType>();
        foreach (DuelByoyomiTimeDataType data in DuelByoyomiTimeDataType.DuelByoyomiTimeDict.Values) {
            if (data != null && CanUseByoyomiTimeCfg(data.id)) {
                options.Add(data);
            }
        }
        options.Sort((a, b) => a.seconds.CompareTo(b.seconds));
        foreach (DuelByoyomiTimeDataType data in options) {
            byoyomiTimeCfgIds.Add(data.id);
        }
    }

    private void NormalizeOgsAutomatchTimeSelection()
    {
        if (FindOgsAutomatchTimeOption(
                selectedBoardCfgId,
                selectedHoldTimeCfgId,
                selectedByoyomiCountCfgId,
                selectedByoyomiTimeCfgId) != null) {
            return;
        }

        OgsAutomatchTimeOptionDataType defaultOption = ResolveNearestOgsAutomatchTimeOption();
        if (defaultOption == null) {
            defaultOption = ResolveDefaultOgsAutomatchTimeOption(selectedBoardCfgId);
        }
        if (defaultOption == null) {
            return;
        }

        selectedHoldTimeCfgId = defaultOption.holdTimeCfgId;
        selectedByoyomiCountCfgId = defaultOption.byoyomiCountCfgId;
        selectedByoyomiTimeCfgId = defaultOption.byoyomiTimeCfgId;
    }

    private void SelectOgsAutomatchTimeOption(string optionCfgId)
    {
        OgsAutomatchTimeOptionDataType option = OgsAutomatchTimeOptionDataType.GetConfigData(optionCfgId);
        if (option == null || !option.enabled || option.boardCfgId != selectedBoardCfgId) {
            return;
        }

        ApplyOgsAutomatchTimeOption(option);
        RefreshTimeControlDropdowns();
        RefreshSelectionState();
    }

    private void ApplyOgsAutomatchTimeOption(OgsAutomatchTimeOptionDataType option)
    {
        if (option == null) {
            return;
        }

        selectedHoldTimeCfgId = option.holdTimeCfgId;
        selectedByoyomiCountCfgId = option.byoyomiCountCfgId;
        selectedByoyomiTimeCfgId = option.byoyomiTimeCfgId;
    }

    private OgsAutomatchTimeOptionDataType ResolveNearestOgsAutomatchTimeOption()
    {
        OgsAutomatchTimeOptionDataType holdAndCountMatch = null;
        OgsAutomatchTimeOptionDataType holdMatch = null;
        foreach (OgsAutomatchTimeOptionDataType option in GetOgsAutomatchTimeOptionsForBoard(selectedBoardCfgId)) {
            if (option.holdTimeCfgId != selectedHoldTimeCfgId) {
                continue;
            }
            holdMatch = holdMatch ?? option;
            if (option.byoyomiCountCfgId == selectedByoyomiCountCfgId) {
                holdAndCountMatch = option;
                break;
            }
        }

        return holdAndCountMatch ?? holdMatch;
    }

    private OgsAutomatchTimeOptionDataType ResolveDefaultOgsAutomatchTimeOption(string boardCfgId)
    {
        List<OgsAutomatchTimeOptionDataType> options = GetOgsAutomatchTimeOptionsForBoard(boardCfgId);
        foreach (OgsAutomatchTimeOptionDataType option in options) {
            if (option.speed == "rapid" && option.system == "byoyomi") {
                return option;
            }
        }

        return options.Count > 0 ? options[0] : null;
    }

    private OgsAutomatchTimeOptionDataType FindOgsAutomatchTimeOption(string boardCfgId, string holdTimeCfgId, string byoyomiCountCfgId, string byoyomiTimeCfgId)
    {
        foreach (OgsAutomatchTimeOptionDataType option in GetOgsAutomatchTimeOptionsForBoard(boardCfgId)) {
            if (option.holdTimeCfgId == holdTimeCfgId
                && option.byoyomiCountCfgId == byoyomiCountCfgId
                && option.byoyomiTimeCfgId == byoyomiTimeCfgId) {
                return option;
            }
        }

        return null;
    }

    private List<OgsAutomatchTimeOptionDataType> GetOgsAutomatchTimeOptionsForBoard(string boardCfgId)
    {
        List<OgsAutomatchTimeOptionDataType> options = new List<OgsAutomatchTimeOptionDataType>();
        OgsAutomatchTimeOptionDataType.GetConfigData(string.Empty);
        if (OgsAutomatchTimeOptionDataType.OgsAutomatchTimeOptionDict == null) {
            return options;
        }

        foreach (OgsAutomatchTimeOptionDataType option in OgsAutomatchTimeOptionDataType.OgsAutomatchTimeOptionDict.Values) {
            if (option != null && option.enabled && option.boardCfgId == boardCfgId) {
                options.Add(option);
            }
        }
        options.Sort(CompareOgsAutomatchTimeOption);
        return options;
    }

    private int CompareOgsAutomatchTimeOption(OgsAutomatchTimeOptionDataType a, OgsAutomatchTimeOptionDataType b)
    {
        int orderCompare = a.sortOrder.CompareTo(b.sortOrder);
        return orderCompare != 0 ? orderCompare : string.CompareOrdinal(a.id, b.id);
    }

    private int CompareHoldTimeData(DuelHoldTimeDataType a, DuelHoldTimeDataType b)
    {
        int aSeconds = a.isInfinite ? int.MaxValue : a.holdSeconds;
        int bSeconds = b.isInfinite ? int.MaxValue : b.holdSeconds;
        int secondsCompare = aSeconds.CompareTo(bSeconds);
        return secondsCompare != 0 ? secondsCompare : string.CompareOrdinal(a.id, b.id);
    }

    private void RefreshDropdownOptions(TMP_Dropdown dropdown, List<string> cfgIds, string selectedCfgId, Func<string, string> displayNameResolver)
    {
        if (dropdown == null) {
            return;
        }

        dropdown.ClearOptions();
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        foreach (string cfgId in cfgIds) {
            options.Add(new TMP_Dropdown.OptionData(displayNameResolver(cfgId)));
        }
        dropdown.AddOptions(options);

        int selectedIndex = cfgIds.IndexOf(selectedCfgId);
        if (selectedIndex < 0) {
            selectedIndex = 0;
        }
        dropdown.SetValueWithoutNotify(selectedIndex);
        dropdown.RefreshShownValue();
    }

    private string GetHoldTimeDisplayName(string cfgId)
    {
        DuelHoldTimeDataType data = DuelHoldTimeDataType.GetConfigData(cfgId);
        return data != null ? data.displayName : cfgId;
    }

    private string GetByoyomiCountDisplayName(string cfgId)
    {
        DuelByoyomiCountDataType data = DuelByoyomiCountDataType.GetConfigData(cfgId);
        return data != null ? data.displayName : cfgId;
    }

    private string GetByoyomiTimeDisplayName(string cfgId)
    {
        DuelByoyomiTimeDataType data = DuelByoyomiTimeDataType.GetConfigData(cfgId);
        return data != null ? data.displayName : cfgId;
    }

    private string GetOgsAutomatchTimeOptionDisplayName(string cfgId)
    {
        OgsAutomatchTimeOptionDataType data = OgsAutomatchTimeOptionDataType.GetConfigData(cfgId);
        return data != null ? data.displayName : cfgId;
    }

    private void AddUnique(List<string> cfgIds, string cfgId)
    {
        if (!string.IsNullOrEmpty(cfgId) && !cfgIds.Contains(cfgId)) {
            cfgIds.Add(cfgId);
        }
    }

    private void RefreshSelectionState()
    {
        SetButtonInteractable(binder.btn_9x9, CanUseBoardCfg("9x9") && selectedBoardCfgId != "9x9");
        SetButtonInteractable(binder.btn_13x13, CanUseBoardCfg("13x13") && selectedBoardCfgId != "13x13");
        SetButtonInteractable(binder.btn_19x19, CanUseBoardCfg("19x19") && selectedBoardCfgId != "19x19");

        bool infiniteHoldTime = IsInfiniteHoldTimeSelected();
        bool byoyomiEnabled = !infiniteHoldTime && selectedByoyomiCountCfgId != ByoyomiOffCfgId;
        if (binder.dropdown_ogs_time_option != null) {
            binder.dropdown_ogs_time_option.interactable = IsOgsAutomatchSetup() && ogsAutomatchTimeOptionCfgIds.Count > 1;
        }
        if (binder.dropdown_hold_time != null) {
            binder.dropdown_hold_time.interactable = !IsOgsAutomatchSetup() && holdTimeCfgIds.Count > 1;
        }
        if (binder.dropdown_byoyomi_count != null) {
            binder.dropdown_byoyomi_count.interactable = !IsOgsAutomatchSetup() && !infiniteHoldTime && byoyomiCountCfgIds.Count > 1;
        }
        if (binder.dropdown_byoyomi_time != null) {
            binder.dropdown_byoyomi_time.interactable = !IsOgsAutomatchSetup() && byoyomiEnabled && byoyomiTimeCfgIds.Count > 1;
        }

        binder.SetSrModeState(ResolveModeState());
        if (binder.dropdown_handicap != null) {
            binder.dropdown_handicap.interactable = !ShouldForceEvenGameHandicap();
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

    private void RefreshPlayerColorDropdown()
    {
        if (binder.dropdown_player_color == null) {
            return;
        }

        binder.dropdown_player_color.ClearOptions();
        binder.dropdown_player_color.AddOptions(new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData("猜先"),
            new TMP_Dropdown.OptionData("执黑"),
            new TMP_Dropdown.OptionData("执白"),
        });

        int selectedIndex = playerSideCfgIds.IndexOf(selectedPlayerSideCfgId);
        if (selectedIndex < 0) {
            selectedIndex = 0;
            selectedPlayerSideCfgId = PlayerSideGuess;
        }

        binder.dropdown_player_color.SetValueWithoutNotify(selectedIndex);
        binder.dropdown_player_color.RefreshShownValue();
    }

    private void RefreshHandicapDropdown()
    {
        if (binder.dropdown_handicap == null) {
            return;
        }

        NormalizeHandicapSelection();
        handicapCfgIds.Clear();
        foreach (string cfgId in DuelHandicapPlacement.GetCfgIdsForBoard(selectedBoardCfgId)) {
            if (CanUseHandicapCfg(cfgId)) {
                handicapCfgIds.Add(cfgId);
            }
        }
        if (handicapCfgIds.Count == 0) {
            handicapCfgIds.Add(ResolveDefaultHandicapCfgId(selectedBoardCfgId));
        }

        binder.dropdown_handicap.ClearOptions();
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        foreach (string cfgId in handicapCfgIds) {
            DuelHandicapDataType data = DuelHandicapDataType.GetConfigData(cfgId);
            options.Add(new TMP_Dropdown.OptionData(data != null ? data.displayName : cfgId));
        }
        binder.dropdown_handicap.AddOptions(options);

        int selectedIndex = handicapCfgIds.IndexOf(selectedHandicapCfgId);
        if (selectedIndex < 0) {
            selectedIndex = 0;
            selectedHandicapCfgId = handicapCfgIds.Count > 0 ? handicapCfgIds[0] : DuelHandicapPlacement.GetDefaultCfgId(selectedBoardCfgId);
        }

        binder.dropdown_handicap.SetValueWithoutNotify(selectedIndex);
        binder.dropdown_handicap.RefreshShownValue();
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
            case "k20_k18": return 0;
            case "k17_k15": return 1;
            case "k14_k12": return 2;
            case "k11_k9": return 3;
            case "k8_k6": return 4;
            case "k5_k3": return 5;
            case "k2_k1": return 6;
            case "d1_d2": return 7;
            case "d3_d4": return 8;
            case "d5_d6": return 9;
            case "d7_d9": return 10;
            case "modern_pro": return 11;
            default: return 1000;
        }
    }

    private void OnAiDifficultyDropdownValueChanged(int index)
    {
        if (index >= 0 && index < aiDifficultyCfgIds.Count) {
            selectedAiDifficultyCfgId = aiDifficultyCfgIds[index];
        }
    }

    private void OnHoldTimeDropdownValueChanged(int index)
    {
        if (isRefreshingTimeDropdowns) {
            return;
        }
        if (index >= 0 && index < holdTimeCfgIds.Count) {
            SelectHoldTime(holdTimeCfgIds[index]);
        }
    }

    private void OnOgsTimeOptionDropdownValueChanged(int index)
    {
        if (isRefreshingTimeDropdowns) {
            return;
        }
        if (index >= 0 && index < ogsAutomatchTimeOptionCfgIds.Count) {
            SelectOgsAutomatchTimeOption(ogsAutomatchTimeOptionCfgIds[index]);
        }
    }

    private void OnByoyomiCountDropdownValueChanged(int index)
    {
        if (isRefreshingTimeDropdowns) {
            return;
        }
        if (index >= 0 && index < byoyomiCountCfgIds.Count) {
            SelectByoyomiCount(byoyomiCountCfgIds[index]);
        }
    }

    private void OnByoyomiTimeDropdownValueChanged(int index)
    {
        if (isRefreshingTimeDropdowns) {
            return;
        }
        if (index >= 0 && index < byoyomiTimeCfgIds.Count) {
            SelectByoyomiTime(byoyomiTimeCfgIds[index]);
        }
    }

    private void OnPlayerColorDropdownValueChanged(int index)
    {
        if (index >= 0 && index < playerSideCfgIds.Count) {
            selectedPlayerSideCfgId = playerSideCfgIds[index];
            NormalizeHandicapSelection();
            RefreshHandicapDropdown();
            RefreshSelectionState();
        }
    }

    private void OnHandicapDropdownValueChanged(int index)
    {
        if (ShouldForceEvenGameHandicap()) {
            selectedHandicapCfgId = DuelHandicapPlacement.GetDefaultCfgId(selectedBoardCfgId);
            RefreshHandicapDropdown();
            return;
        }

        if (index >= 0 && index < handicapCfgIds.Count) {
            selectedHandicapCfgId = handicapCfgIds[index];
        }
    }

    private bool ShouldShowPlayerColor()
    {
        return isAiDuel || IsLanRoomSetup() || IsAnyOgsSetup();
    }

    private DuelSetupPopupUI.SrModeState ResolveModeState()
    {
        if (IsLanRoomSetup()) {
            return DuelSetupPopupUI.SrModeState.Lan;
        }
        if (IsOgsFriendSetup()) {
            return DuelSetupPopupUI.SrModeState.Lan;
        }
        if (IsOgsAutomatchSetup()) {
            return DuelSetupPopupUI.SrModeState.Ogs;
        }

        return isAiDuel ? DuelSetupPopupUI.SrModeState.Ai : DuelSetupPopupUI.SrModeState.Local;
    }

    private bool ShouldForceEvenGameHandicap()
    {
        if (IsOgsAutomatchSetup()) {
            return true;
        }

        return ShouldShowPlayerColor() && selectedPlayerSideCfgId == PlayerSideGuess;
    }

    private bool IsLanRoomSetup()
    {
        return setupMode == SetupOpenMode.Lan;
    }

    private bool IsOgsAutomatchSetup()
    {
        return setupMode == SetupOpenMode.Ogs;
    }

    private bool IsOgsFriendSetup()
    {
        return setupMode == SetupOpenMode.OgsFriend;
    }

    private bool IsAnyOgsSetup()
    {
        return IsOgsAutomatchSetup() || IsOgsFriendSetup();
    }

    private DuelSetupPreferenceMode ResolvePreferenceMode()
    {
        if (IsLanRoomSetup()) {
            return DuelSetupPreferenceMode.Lan;
        }
        if (IsOgsFriendSetup()) {
            return DuelSetupPreferenceMode.OgsFriend;
        }
        if (IsOgsAutomatchSetup()) {
            return DuelSetupPreferenceMode.Ogs;
        }

        return isAiDuel ? DuelSetupPreferenceMode.Ai : DuelSetupPreferenceMode.Local;
    }

    private void LoadPreference()
    {
        DuelSetupModePreference preference = User.Instance.compDuelSetupPreference?.GetPreference(setupPreferenceMode);
        if (preference == null) {
            return;
        }

        selectedBoardCfgId = preference.boardCfgId.value;
        selectedHoldTimeCfgId = preference.holdTimeCfgId.value;
        selectedByoyomiCountCfgId = preference.byoyomiCountCfgId.value;
        selectedByoyomiTimeCfgId = preference.byoyomiTimeCfgId.value;
        selectedPlayerSideCfgId = preference.playerSideCfgId.value;
        selectedHandicapCfgId = preference.handicapCfgId.value;
        selectedAiDifficultyCfgId = preference.aiDifficultyCfgId.value;
    }

    private void SavePreference()
    {
        DuelSetupModePreference preference = User.Instance.compDuelSetupPreference?.GetPreference(setupPreferenceMode);
        if (preference == null) {
            return;
        }

        preference.Set(
            selectedBoardCfgId,
            selectedHoldTimeCfgId,
            selectedByoyomiCountCfgId,
            selectedByoyomiTimeCfgId,
            selectedPlayerSideCfgId,
            selectedHandicapCfgId,
            selectedAiDifficultyCfgId);
        User.Instance.Save();
    }

    private PlayerFlag ResolveLocalPlayerFlag()
    {
        switch (selectedPlayerSideCfgId) {
            case PlayerSideBlack:
                return PlayerFlag.Player1;
            case PlayerSideWhite:
                return PlayerFlag.Player2;
            case PlayerSideGuess:
            default:
                return UnityEngine.Random.value < 0.5f ? PlayerFlag.Player1 : PlayerFlag.Player2;
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
