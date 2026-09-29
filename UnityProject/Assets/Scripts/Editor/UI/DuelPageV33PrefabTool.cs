using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DuelPageV33PrefabTool
{
    private const string DuelPagePath = "Assets/UI/Prefab/Page/DuelPage.prefab";
    private const string MoveConfirmPath = "Assets/UI/Prefab/Page/DuelMoveConfirmPopup.prefab";
    private const string MainMenuPath = "Assets/UI/Prefab/Page/MainMenuPage.prefab";
    private const int UiLayer = 5;
    private const float LandscapePanelWidth = 610f;

    private static TMP_FontAsset regularFont;
    private static TMP_FontAsset mediumFont;
    private static TMP_FontAsset serifFont;

    private sealed class ActionRow
    {
        public GameObject root;
        public Button button;
        public RectTransform icon;
        public RectTransform label;
        public TextMeshProUGUI hint;
    }

    private sealed class PlayerCard
    {
        public GameObject root;
        public GameObject turnAccent;
        public TextMeshProUGUI title;
        public TextMeshProUGUI name;
        public TextMeshProUGUI subtitle;
        public TextMeshProUGUI holdTime;
        public TextMeshProUGUI byoyomiCount;
        public TextMeshProUGUI byoyomiTime;
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/重建 DuelPage V3.3")]
    public static void Rebuild()
    {
        LoadFonts();
        BuildDuelPage();
        BuildMoveConfirmPopup();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("DuelPage V3.3 prefabs rebuilt.");
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/重建 MainMenuPage V3.3")]
    public static void RebuildMainMenu()
    {
        LoadFonts();
        BuildMainMenuPage();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("MainMenuPage V3.3 prefab rebuilt.");
    }

    private static void LoadFonts()
    {
        regularFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.RegularFontAssetPath);
        mediumFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.MediumFontAssetPath);
        serifFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.SerifFontAssetPath);
        if (regularFont == null || mediumFont == null || serifFont == null) {
            throw new InvalidOperationException("DuelPage V3.3 requires all theme font assets.");
        }
    }

    private static void BuildDuelPage()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(DuelPagePath);
        try {
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Canvas canvas = root.GetComponent<Canvas>();
            ClearChildren(rootRect);

            RectTransform panelRoot = CreateRect(rootRect, "PanelRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform platform = CreateRect(panelRoot, "sr_platform", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            StateRoot stateRoot = platform.gameObject.AddComponent<StateRoot>();

            RectTransform info = CreateRect(platform, "panel_duel_info", Vector2.one, Vector2.one, new Vector2(-42f, -216f), new Vector2(LandscapePanelWidth, 96f), Vector2.one);
            TextMeshProUGUI duelMeta = CreateText(info, "txt_duel_meta", "电脑对局  ·  十九路  ·  贴 6.5 目", mediumFont, 13f, PaperAlpha(0.5f), TextAlignmentOptions.TopLeft);
            Place(duelMeta.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(430f, 30f), new Vector2(0f, 1f));
            TextMeshProUGUI moveCount = CreateText(info, "txt_duel_move_count", "第 57 手", serifFont, 44f, PaperAlpha(0.95f), TextAlignmentOptions.BottomLeft);
            Place(moveCount.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, -8f), new Vector2(0f, -30f), new Vector2(0f, 0.5f));
            TextMeshProUGUI portraitMoveCount = CreateText(info, "txt_duel_move_count_portrait", "第 57 手", serifFont, 22f, PaperAlpha(0.95f), TextAlignmentOptions.MidlineRight);
            Place(portraitMoveCount.rectTransform, new Vector2(1f, 0f), Vector2.one, Vector2.zero, new Vector2(190f, 0f), new Vector2(1f, 0.5f));
            portraitMoveCount.gameObject.SetActive(false);

            PlayerCard whiteCard = CreatePlayerCard(platform, "white", false);
            PlayerCard blackCard = CreatePlayerCard(platform, "black", true);
            Place(whiteCard.root.transform as RectTransform, Vector2.one, Vector2.one, new Vector2(-42f, -42f), new Vector2(LandscapePanelWidth, 150f), Vector2.one);
            Place(blackCard.root.transform as RectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-42f, 42f), new Vector2(LandscapePanelWidth, 150f), new Vector2(1f, 0f));

            RectTransform dynamicPanel = CreateRect(platform, "panel_duel_dynamic", Vector2.one, Vector2.one, new Vector2(-42f, -320f), new Vector2(LandscapePanelWidth, 104f), Vector2.one);
            VerticalLayoutGroup dynamicLayout = dynamicPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            dynamicLayout.childControlWidth = true;
            dynamicLayout.childControlHeight = true;
            dynamicLayout.childForceExpandWidth = true;
            dynamicLayout.childForceExpandHeight = false;
            dynamicLayout.spacing = 8f;

            GameObject shapePanel = CreateLayoutRow(dynamicPanel, "panel_duel_shape_result", 48f);
            TextMeshProUGUI shapeLabel = CreateText(shapePanel.transform as RectTransform, "txt_shape_label", "形势", mediumFont, 13f, PaperAlpha(0.5f), TextAlignmentOptions.MidlineLeft);
            Place(shapeLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(52f, 0f), new Vector2(0f, 0.5f));
            TextMeshProUGUI shapeLead = CreateText(shapePanel.transform as RectTransform, "txt_shape_lead_points", "黑领先 3.5 目", serifFont, 24f, PaperAlpha(0.95f), TextAlignmentOptions.MidlineLeft);
            Place(shapeLead.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(54f, 0f), new Vector2(-170f, 0f), new Vector2(0f, 0.5f));
            TextMeshProUGUI shapeRule = CreateText(shapePanel.transform as RectTransform, "txt_shape_rule_info", "贴目 6.5", regularFont, 13f, PaperAlpha(0.5f), TextAlignmentOptions.MidlineRight);
            Place(shapeRule.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(160f, 0f), new Vector2(1f, 0.5f));
            shapePanel.SetActive(false);

            GameObject noticePanel = CreateLayoutRow(dynamicPanel, "panel_duel_action_notice", 48f);
            CanvasGroup noticeCanvas = noticePanel.AddComponent<CanvasGroup>();
            Image noticeAccent = CreateImage(noticePanel.transform as RectTransform, "img_duel_action_notice_accent", UIPalette.AccentOnInk, null, false);
            Place(noticeAccent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, 0f), new Vector2(0f, 0.5f));
            Image noticeIcon = CreateImage(noticePanel.transform as RectTransform, "img_duel_action_notice_icon", PaperAlpha(0.85f), Sprite("icon_info"), false);
            Place(noticeIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(22f, 22f), new Vector2(0f, 0.5f));
            TextMeshProUGUI noticeText = CreateText(noticePanel.transform as RectTransform, "txt_duel_action_notice", "白 落子 Q16", regularFont, 15f, PaperAlpha(0.85f), TextAlignmentOptions.MidlineLeft);
            Place(noticeText.rectTransform, Vector2.zero, Vector2.one, new Vector2(52f, 0f), new Vector2(-52f, 0f), new Vector2(0f, 0.5f));
            noticePanel.SetActive(false);

            RectTransform actions = CreateRect(platform, "panel_duel_actions", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-42f, 216f), new Vector2(LandscapePanelWidth, 216f), new Vector2(1f, 0f));
            actions.gameObject.AddComponent<DuelActionLayoutGroup>();
            ActionRow pass = CreateActionRow(actions, "pass", "虚手", "这一手不下", "icon_pass");
            ActionRow ownership = CreateActionRow(actions, "ownership", "形势", "显示归属与目数", "icon_ownership");
            ActionRow analysis = CreateActionRow(actions, "ai_analysis", "分析", "AI 推荐点", "icon_search");
            analysis.root.name = "panel_duel_ai_analysis";
            ActionRow settings = CreateActionRow(actions, "settings", "对局菜单", "数子 · 悔棋 · 认输 · 退出", "icon_menu");

            GameObject gameEnd = CreateGameEndCard(platform);
            RectTransform gameEndRect = gameEnd.transform as RectTransform;
            Place(gameEndRect, Vector2.one, Vector2.one, new Vector2(-42f, -216f), new Vector2(LandscapePanelWidth, 220f), Vector2.one);
            TextMeshProUGUI gameEndWinner = gameEnd.transform.Find("txt_game_end_winner").GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI gameEndReason = gameEnd.transform.Find("txt_game_end_reason").GetComponent<TextMeshProUGUI>();
            Button gameEndExit = gameEnd.transform.Find("btn_game_end_exit").GetComponent<Button>();
            gameEnd.SetActive(false);

            GameObject settingsPanel = CreateSettingsDrawer(platform, out Button settingsScrim, out Button settingsClose,
                out Button requestScore, out Button takeBack, out Button resign, out Button exit,
                out GameObject inlineConfirm, out TextMeshProUGUI inlineTitle, out TextMeshProUGUI inlineContent,
                out TextMeshProUGUI inlineConfirmText, out Button inlineCancel, out Button inlineConfirmButton,
                out RectTransform settingsSheet);
            settingsPanel.SetActive(false);

            ConfigurePlatformStates(stateRoot, info, duelMeta.rectTransform, moveCount, portraitMoveCount, dynamicPanel, actions,
                whiteCard.root.transform as RectTransform, blackCard.root.transform as RectTransform, gameEndRect, settingsSheet,
                pass, ownership, analysis, settings);

            DuelPageUI binder = root.GetComponent<DuelPageUI>();
            binder.sr_platform = stateRoot;
            binder.panel_duel_info = info.gameObject;
            binder.panel_duel_dynamic = dynamicPanel.gameObject;
            binder.panel_duel_actions = actions.gameObject;
            binder.txt_duel_meta = duelMeta;
            binder.txt_duel_move_count = moveCount;
            binder.txt_duel_move_count_portrait = portraitMoveCount;
            AssignPlayerCard(binder, blackCard, true);
            AssignPlayerCard(binder, whiteCard, false);
            binder.btn_duel_pass = pass.button;
            binder.btn_duel_ownership = ownership.button;
            binder.txt_duel_ownership_button = ownership.label.GetComponent<TextMeshProUGUI>();
            binder.txt_duel_ownership_hint = ownership.hint;
            binder.txt_duel_pass_hint = pass.hint;
            binder.btn_duel_ai_analysis = analysis.button;
            binder.txt_duel_ai_hint = analysis.hint;
            binder.panel_duel_ai_analysis = analysis.root;
            binder.btn_duel_settings = settings.button;
            binder.txt_duel_menu_hint = settings.hint;
            binder.panel_duel_shape_result = shapePanel;
            binder.txt_shape_lead_points = shapeLead;
            binder.txt_shape_rule_info = shapeRule;
            binder.panel_duel_action_notice = noticePanel;
            binder.canvas_duel_action_notice = noticeCanvas;
            binder.img_duel_action_notice_icon = noticeIcon;
            binder.txt_duel_action_notice = noticeText;
            binder.txt_duel_stone_removal_countdown = ownership.root.transform.Find("txt_duel_stone_removal_countdown").GetComponent<TextMeshProUGUI>();
            binder.panel_duel_settings = settingsPanel;
            binder.btn_settings_scrim = settingsScrim;
            binder.btn_settings_close = settingsClose;
            binder.btn_settings_request_score = requestScore;
            binder.btn_settings_take_back = takeBack;
            binder.btn_settings_resign = resign;
            binder.btn_settings_exit = exit;
            binder.panel_settings_inline_confirm = inlineConfirm;
            binder.txt_settings_inline_title = inlineTitle;
            binder.txt_settings_inline_content = inlineContent;
            binder.txt_settings_inline_confirm = inlineConfirmText;
            binder.btn_settings_inline_cancel = inlineCancel;
            binder.btn_settings_inline_confirm = inlineConfirmButton;
            binder.panel_game_end_result = gameEnd;
            binder.txt_game_end_winner = gameEndWinner;
            binder.txt_game_end_reason = gameEndReason;
            binder.btn_game_end_exit = gameEndExit;

            UpdateBinderEditor(root, BuildBinderNodes(binder));
            RestoreCanvasRoot(rootRect, canvas);
            PrefabUtility.SaveAsPrefabAsset(root, DuelPagePath);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildMoveConfirmPopup()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(MoveConfirmPath);
        try {
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Canvas canvas = root.GetComponent<Canvas>();
            ClearChildren(rootRect);

            RectTransform panelRoot = CreateRect(rootRect, "PanelRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform controls = CreateRect(panelRoot, "panel_controls", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(-28f, 132f), new Vector2(0.5f, 0f));

            Button up = CreateIconButton(controls, "btn_move_up", "icon_up", new Vector2(72f, 108f));
            Button down = CreateIconButton(controls, "btn_move_down", "icon_down", new Vector2(72f, 20f));
            Button left = CreateIconButton(controls, "btn_move_left", "icon_left", new Vector2(28f, 64f));
            Button right = CreateIconButton(controls, "btn_move_right", "icon_right", new Vector2(116f, 64f));

            TextMeshProUGUI coordLabel = CreateText(controls, "txt_coordinate_label", "落子位置", mediumFont, 13f, PaperAlpha(0.5f), TextAlignmentOptions.BottomLeft);
            Place(coordLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(180f, 24f), new Vector2(120f, 28f), new Vector2(0f, 0.5f));
            TextMeshProUGUI coordinate = CreateText(controls, "txt_move_coordinate", "K14", serifFont, 34f, PaperAlpha(0.95f), TextAlignmentOptions.TopLeft);
            Place(coordinate.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(180f, -18f), new Vector2(150f, 54f), new Vector2(0f, 0.5f));

            Button cancel = CreateTextButton(controls, "btn_cancel", "取消", regularFont, 18f, PaperAlpha(0.85f), Color.clear, false);
            Place(cancel.transform as RectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-220f, 0f), new Vector2(100f, 56f), new Vector2(1f, 0.5f));
            Button confirm = CreateTextButton(controls, "btn_confirm", "确认落子", mediumFont, 18f, UIPalette.Ink, UIPalette.Paper, true);
            Place(confirm.transform as RectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(176f, 56f), new Vector2(1f, 0.5f));

            DuelMoveConfirmPopupUI binder = root.GetComponent<DuelMoveConfirmPopupUI>();
            binder.btn_confirm = confirm;
            binder.btn_cancel = cancel;
            binder.btn_move_up = up;
            binder.btn_move_down = down;
            binder.btn_move_left = left;
            binder.btn_move_right = right;
            binder.txt_move_coordinate = coordinate;

            var nodes = new List<UIBinderNode> {
                new UIBinderNode(nameof(binder.btn_confirm), confirm),
                new UIBinderNode(nameof(binder.btn_cancel), cancel),
                new UIBinderNode(nameof(binder.btn_move_up), up),
                new UIBinderNode(nameof(binder.btn_move_down), down),
                new UIBinderNode(nameof(binder.btn_move_left), left),
                new UIBinderNode(nameof(binder.btn_move_right), right),
                new UIBinderNode(nameof(binder.txt_move_coordinate), coordinate),
            };
            UpdateBinderEditor(root, nodes);
            RestoreCanvasRoot(rootRect, canvas);
            PrefabUtility.SaveAsPrefabAsset(root, MoveConfirmPath);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildMainMenuPage()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(MainMenuPath);
        try {
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Canvas canvas = root.GetComponent<Canvas>();
            CanvasScaler canvasScaler = root.GetComponent<CanvasScaler>();
            ClearChildren(rootRect);

            RectTransform panelRoot = CreateRect(rootRect, "PanelRoot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            RectTransform platform = CreateRect(panelRoot, "sr_platform", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            StateRoot stateRoot = platform.gameObject.AddComponent<StateRoot>();

            Image paperShadow = CreateImage(platform, "img_menu_paper_shadow", WithAlpha(UIPalette.Ink, 0.55f), Sprite("ui_card_shadow"), false);
            Place(paperShadow.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(8f, 0f), new Vector2(640f, 0f), new Vector2(0f, 0.5f));
            Image paper = CreateImage(platform, "panel_menu_paper", UIPalette.Paper, Sprite("ui_panel"), false);
            Place(paper.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(620f, 0f), new Vector2(0f, 0.5f));

            RectTransform header = CreateMainMenuHeader(paper.rectTransform);
            RectTransform menuList = CreateRect(paper.rectTransform, "panel_buttons", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(96f, -350f), new Vector2(428f, 336f), new Vector2(0f, 1f));
            VerticalLayoutGroup menuLayout = menuList.gameObject.AddComponent<VerticalLayoutGroup>();
            menuLayout.childControlWidth = true;
            menuLayout.childControlHeight = true;
            menuLayout.childForceExpandWidth = true;
            menuLayout.childForceExpandHeight = false;
            menuLayout.spacing = 0f;

            Button newGame = CreateMainMenuRow(menuList, "btn_new_game", "01", "新游戏", "本地双人对弈");
            Button aiGame = CreateMainMenuRow(menuList, "btn_ai_game", "02", "电脑对局", "与 KataGo 对弈");
            Button lanGame = CreateMainMenuRow(menuList, "btn_lan_game", "03", "局域网对战", "同一网络内联机");
            Button ogsGame = CreateMainMenuRow(menuList, "btn_ogs_game", "04", "OGS 对战", "Online Go Server");

            Button exit = CreateMainMenuFooterButton(paper.rectTransform);
            TextMeshProUGUI version = CreateText(paper.rectTransform, "txt_version", "v1.0", regularFont, 12f, UIPalette.InkTertiary, TextAlignmentOptions.MidlineRight);
            Place(version.rectTransform, Vector2.zero, Vector2.zero, new Vector2(524f, 52f), new Vector2(100f, 24f), new Vector2(1f, 0f));

            Button userInfo = CreateMainMenuUserChip(platform, out RectTransform redDot, out TextMeshProUGUI userName, out TextMeshProUGUI userStatus);
            RectTransform userChip = userInfo.transform as RectTransform;

            StateConfig landscape = new StateConfig { name = "Landscape" };
            StateConfig portrait = new StateConfig { name = "Portrait" };
            stateRoot.EditableStates.Add(landscape);
            stateRoot.EditableStates.Add(portrait);
            AddRectState(landscape, "PaperShadow", paperShadow.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(8f, 0f), new Vector2(640f, 0f), new Vector2(0f, 0.5f));
            AddRectState(portrait, "PaperShadow", paperShadow.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(0f, 740f), new Vector2(0.5f, 0f));
            AddRectState(landscape, "Paper", paper.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(620f, 0f), new Vector2(0f, 0.5f));
            AddRectState(portrait, "Paper", paper.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 720f), new Vector2(0.5f, 0f));
            AddRectState(landscape, "Header", header, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(96f, -108f), new Vector2(428f, 174f), new Vector2(0f, 1f));
            AddRectState(portrait, "Header", header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -54f), new Vector2(-96f, 142f), new Vector2(0.5f, 1f));
            AddRectState(landscape, "Menu", menuList, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(96f, -350f), new Vector2(428f, 336f), new Vector2(0f, 1f));
            AddRectState(portrait, "Menu", menuList, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -206f), new Vector2(-96f, 336f), new Vector2(0.5f, 1f));
            AddRectState(landscape, "Exit", exit.transform as RectTransform, Vector2.zero, Vector2.zero, new Vector2(96f, 144f), new Vector2(120f, 48f), new Vector2(0f, 0.5f));
            AddRectState(portrait, "Exit", exit.transform as RectTransform, Vector2.zero, Vector2.zero, new Vector2(48f, 80f), new Vector2(120f, 48f), new Vector2(0f, 0.5f));
            AddRectState(landscape, "Version", version.rectTransform, Vector2.zero, Vector2.zero, new Vector2(524f, 52f), new Vector2(100f, 24f), new Vector2(1f, 0f));
            AddRectState(portrait, "Version", version.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-48f, 80f), new Vector2(100f, 24f), new Vector2(1f, 0.5f));
            AddRectState(landscape, "UserChip", userChip, Vector2.one, Vector2.one, new Vector2(-40f, -40f), new Vector2(214f, 56f), Vector2.one);
            AddRectState(portrait, "UserChip", userChip, Vector2.one, Vector2.one, new Vector2(-24f, -52f), new Vector2(214f, 56f), Vector2.one);

            MainMenuPageUI binder = root.GetComponent<MainMenuPageUI>();
            binder.sr_platform = stateRoot;
            binder.btn_new_game = newGame;
            binder.btn_ai_game = aiGame;
            binder.btn_lan_game = lanGame;
            binder.btn_ogs_game = ogsGame;
            binder.btn_exit = exit;
            binder.btn_user_info = userInfo;
            binder.red_dot_user_info = redDot;
            binder.txt_user_name = userName;
            binder.txt_user_status = userStatus;
            binder.txt_version = version;

            UpdateBinderEditor(root, BuildBinderNodes(binder));
            if (canvasScaler != null) {
                canvasScaler.referenceResolution = UICanvasResolutionProfile.EditorDefaultReferenceResolution;
                canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                canvasScaler.matchWidthOrHeight = 0.5f;
            }
            RestoreCanvasRoot(rootRect, canvas);
            PrefabUtility.SaveAsPrefabAsset(root, MainMenuPath);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static RectTransform CreateMainMenuHeader(RectTransform parent)
    {
        RectTransform header = CreateRect(parent, "panel_header", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(96f, -108f), new Vector2(428f, 174f), new Vector2(0f, 1f));
        TextMeshProUGUI eyebrow = CreateText(header, "txt_eyebrow", "围 棋  ·  对 弈 与 复 盘", mediumFont, 13f, UIPalette.InkTertiary, TextAlignmentOptions.TopLeft);
        Place(eyebrow.rectTransform, new Vector2(0f, 1f), Vector2.one, Vector2.zero, new Vector2(0f, 28f), new Vector2(0f, 1f));
        TextMeshProUGUI title = CreateText(header, "txt_title", "弈·悟", serifFont, 76f, UIPalette.Ink, TextAlignmentOptions.BottomLeft);
        Place(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(-4f, -2f), new Vector2(270f, -34f), new Vector2(0f, 0.5f));
        Image seal = CreateImage(header, "img_title_seal", UIPalette.Accent, Sprite("ui_panel"), false);
        Place(seal.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(258f, -20f), new Vector2(34f, 34f), new Vector2(0f, 0.5f));
        TextMeshProUGUI sealText = CreateText(seal.rectTransform, "txt_title_seal", "弈", serifFont, 22f, UIPalette.Paper, TextAlignmentOptions.Center);
        Place(sealText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        return header;
    }

    private static Button CreateMainMenuRow(RectTransform parent, string name, string number, string titleValue, string captionValue)
    {
        RectTransform row = CreateRect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 84f), new Vector2(0.5f, 0.5f));
        LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 84f;
        Image background = CreateImage(row, "img_hit", Color.clear, null, true);
        Place(background.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Button button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;
        row.gameObject.AddComponent<UIButtonFeedback>();

        Image accent = CreateImage(row, "img_hover_accent", UIPalette.Accent, null, false);
        Place(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, -2f), new Vector2(0f, 0.5f));
        TextMeshProUGUI index = CreateText(row, "txt_index", number, mediumFont, 12f, UIPalette.InkTertiary, TextAlignmentOptions.MidlineLeft);
        Place(index.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(38f, 0f), new Vector2(0f, 0.5f));
        TextMeshProUGUI title = CreateText(row, "txt_title", titleValue, serifFont, 26f, UIPalette.Ink, TextAlignmentOptions.MidlineLeft);
        Place(title.rectTransform, new Vector2(0f, 0f), new Vector2(0.65f, 1f), new Vector2(44f, 0f), new Vector2(-44f, 0f), new Vector2(0f, 0.5f));
        TextMeshProUGUI caption = CreateText(row, "txt_caption", captionValue, regularFont, 14f, UIPalette.InkTertiary, TextAlignmentOptions.MidlineRight);
        Place(caption.rectTransform, new Vector2(0.5f, 0f), Vector2.one, new Vector2(-24f, 0f), new Vector2(24f, 0f), new Vector2(0.5f, 0.5f));
        Image arrow = CreateImage(row, "img_arrow", UIPalette.Accent, Sprite("icon_right"), false);
        Place(arrow.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(18f, 18f), new Vector2(1f, 0.5f));
        Image line = CreateImage(row, "img_hairline", UIPalette.Hairline, null, false);
        Place(line.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f), new Vector2(0.5f, 0f));
        MainMenuRowVisual visual = row.gameObject.AddComponent<MainMenuRowVisual>();
        visual.Configure(background, accent, arrow, title);
        return button;
    }

    private static Button CreateMainMenuFooterButton(RectTransform parent)
    {
        RectTransform root = CreateRect(parent, "btn_exit", Vector2.zero, Vector2.zero, new Vector2(96f, 144f), new Vector2(120f, 48f), new Vector2(0f, 0.5f));
        Image hit = CreateImage(root, "img_hit", Color.clear, null, true);
        Place(hit.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.colors = PaperGhostButtonColors();
        root.gameObject.AddComponent<UIButtonFeedback>();
        TextMeshProUGUI text = CreateText(root, "txt_exit", "退出", mediumFont, 16f, UIPalette.InkSecondary, TextAlignmentOptions.MidlineLeft);
        Place(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f));
        return button;
    }

    private static Button CreateMainMenuUserChip(RectTransform parent, out RectTransform redDot, out TextMeshProUGUI userName, out TextMeshProUGUI userStatus)
    {
        RectTransform root = CreateRect(parent, "btn_user_info", Vector2.one, Vector2.one, new Vector2(-40f, -40f), new Vector2(214f, 56f), Vector2.one);
        Image shadow = CreateImage(root, "img_shadow", WithAlpha(UIPalette.Ink, 0.35f), Sprite("ui_card_shadow"), false);
        Place(shadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, -3f), new Vector2(12f, 12f), new Vector2(0.5f, 0.5f));
        Image plate = CreateImage(root, "img_plate", WithAlpha(UIPalette.Paper, 0.94f), Sprite("ui_capsule"), true);
        Place(plate.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        button.colors = SolidButtonColors(plate.color);
        root.gameObject.AddComponent<UIButtonFeedback>();
        Image icon = CreateImage(root, "img_user", UIPalette.InkSecondary, Sprite("icon_user"), false);
        Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(24f, 24f), new Vector2(0.5f, 0.5f));
        userName = CreateText(root, "txt_user_name", "棋手", mediumFont, 16f, UIPalette.Ink, TextAlignmentOptions.MidlineLeft);
        Place(userName.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(58f, -7f), new Vector2(-68f, 22f), new Vector2(0f, 1f));
        userStatus = CreateText(root, "txt_user_status", "本地棋手 · OGS 未登录", regularFont, 11f, UIPalette.InkSecondary, TextAlignmentOptions.MidlineLeft);
        Place(userStatus.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(58f, -30f), new Vector2(-68f, 18f), new Vector2(0f, 1f));
        Image dot = CreateImage(root, "red_dot_user_info", UIPalette.Accent, Sprite("ui_knob"), false);
        Place(dot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(43f, -13f), new Vector2(10f, 10f), new Vector2(0.5f, 0.5f));
        redDot = dot.rectTransform;
        return button;
    }

    private static PlayerCard CreatePlayerCard(RectTransform parent, string colorName, bool isBlack)
    {
        RectTransform root = CreateRect(parent, $"panel_{colorName}_player", Vector2.one, Vector2.one, Vector2.zero, new Vector2(LandscapePanelWidth, 150f), Vector2.one);
        Image shadow = CreateImage(root, "img_shadow", new Color(0f, 0f, 0f, 0.45f), Sprite("ui_card_shadow"), false);
        Place(shadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, -4f), new Vector2(20f, 20f), new Vector2(0.5f, 0.5f));
        Color bodyColor = isBlack ? UIPalette.InkCard : WithAlpha(UIPalette.Paper, 0.9f);
        Image body = CreateImage(root, "img_card", bodyColor, Sprite("ui_panel"), false);
        Place(body.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));

        Image accent = CreateImage(root, $"img_{colorName}_turn_accent", isBlack ? UIPalette.AccentOnInk : UIPalette.Accent, null, false);
        Place(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(3f, -28f), new Vector2(0f, 0.5f));
        accent.gameObject.SetActive(isBlack);

        Color primary = isBlack ? PaperAlpha(0.95f) : UIPalette.Ink;
        Color secondary = isBlack ? PaperAlpha(0.5f) : WithAlpha(UIPalette.InkSecondary, 0.85f);
        Image stone = CreateImage(root, $"img_{colorName}_stone", isBlack ? UIPalette.Ink : UIPalette.PaperRaised, Sprite("ui_knob"), false);
        Place(stone.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -28f), new Vector2(20f, 20f), new Vector2(0f, 0.5f));

        TextMeshProUGUI title = CreateText(root, $"txt_{colorName}_title", isBlack ? "黑方  行棋中" : "白方", mediumFont, 13f, secondary, TextAlignmentOptions.MidlineLeft);
        title.richText = true;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(58f, -28f), new Vector2(220f, 26f), new Vector2(0f, 0.5f));
        TextMeshProUGUI playerName = CreateText(root, $"txt_{colorName}_player_name", isBlack ? "棋手" : "AI", serifFont, 28f, primary, TextAlignmentOptions.MidlineLeft);
        Place(playerName.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(260f, 44f), new Vector2(0f, 0.5f));
        TextMeshProUGUI subtitle = CreateText(root, $"txt_{colorName}_subtitle", isBlack ? "本机" : "电脑 · KataGo", regularFont, 13f, secondary, TextAlignmentOptions.MidlineLeft);
        Place(subtitle.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 22f), new Vector2(260f, 24f), new Vector2(0f, 0.5f));
        TextMeshProUGUI hold = CreateText(root, $"txt_{colorName}_hold_time", isBlack ? "18:42" : "21:07", serifFont, 52f, primary, TextAlignmentOptions.MidlineRight);
        Place(hold.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -54f), new Vector2(260f, 72f), new Vector2(1f, 0.5f));
        TextMeshProUGUI count = CreateText(root, $"txt_{colorName}_byoyomi_count", "剩余读秒 3 次 ·", regularFont, 13f, secondary, TextAlignmentOptions.MidlineRight);
        Place(count.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-120f, 22f), new Vector2(170f, 24f), new Vector2(1f, 0.5f));
        TextMeshProUGUI time = CreateText(root, $"txt_{colorName}_byoyomi_time", "读秒 00:30", regularFont, 13f, secondary, TextAlignmentOptions.MidlineRight);
        Place(time.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 22f), new Vector2(100f, 24f), new Vector2(1f, 0.5f));

        return new PlayerCard {
            root = root.gameObject,
            turnAccent = accent.gameObject,
            title = title,
            name = playerName,
            subtitle = subtitle,
            holdTime = hold,
            byoyomiCount = count,
            byoyomiTime = time,
        };
    }

    private static ActionRow CreateActionRow(RectTransform parent, string id, string labelValue, string hintValue, string iconName)
    {
        RectTransform root = CreateRect(parent, $"btn_duel_{id}", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Image hit = CreateImage(root, "img_hit", Color.clear, null, true);
        Place(hit.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = GhostButtonColors();
        root.gameObject.AddComponent<UIButtonFeedback>();

        Image icon = CreateImage(root, $"img_{id}_icon", PaperAlpha(0.85f), Sprite(iconName), false);
        Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(24f, 24f), new Vector2(0f, 0.5f));
        TextMeshProUGUI label = CreateText(root, $"btn_duel_{id}_text", labelValue, mediumFont, 18f, PaperAlpha(0.95f), TextAlignmentOptions.MidlineLeft);
        Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(170f, 36f), new Vector2(0f, 0.5f));
        TextMeshProUGUI hint = CreateText(root, $"txt_duel_{id}_hint", hintValue, regularFont, 13f, PaperAlpha(0.35f), TextAlignmentOptions.MidlineRight);
        Place(hint.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(360f, 30f), new Vector2(1f, 0.5f));
        Image line = CreateImage(root, "img_hairline", PaperAlpha(0.12f), null, false);
        Place(line.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f), new Vector2(0.5f, 0f));

        if (id == "ownership") {
            TextMeshProUGUI countdown = CreateText(root, "txt_duel_stone_removal_countdown", "--:--", regularFont, 13f, PaperAlpha(0.5f), TextAlignmentOptions.BottomRight);
            Place(countdown.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-12f, 3f), new Vector2(120f, 18f), new Vector2(1f, 0f));
            countdown.gameObject.SetActive(false);
        }

        return new ActionRow { root = root.gameObject, button = button, icon = icon.rectTransform, label = label.rectTransform, hint = hint };
    }

    private static GameObject CreateGameEndCard(RectTransform parent)
    {
        RectTransform root = CreateRect(parent, "panel_game_end_result", Vector2.one, Vector2.one, Vector2.zero, new Vector2(LandscapePanelWidth, 220f), Vector2.one);
        Image paper = CreateImage(root, "img_result_paper", WithAlpha(UIPalette.Paper, 0.92f), Sprite("ui_panel"), false);
        Place(paper.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        TextMeshProUGUI eyebrow = CreateText(root, "txt_result_eyebrow", "终局 · 电脑对局", mediumFont, 13f, WithAlpha(UIPalette.InkSecondary, 0.85f), TextAlignmentOptions.TopLeft);
        Place(eyebrow.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -26f), new Vector2(300f, 24f), new Vector2(0f, 1f));
        TextMeshProUGUI seal = CreateText(root, "txt_result_seal", "胜", serifFont, 24f, UIPalette.Accent, TextAlignmentOptions.Center);
        Place(seal.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -62f), new Vector2(46f, 46f), new Vector2(0f, 1f));
        TextMeshProUGUI winner = CreateText(root, "txt_game_end_winner", "黑方胜出", serifFont, 34f, UIPalette.Ink, TextAlignmentOptions.MidlineLeft);
        Place(winner.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(86f, -70f), new Vector2(-250f, 54f), new Vector2(0f, 1f));
        TextMeshProUGUI reason = CreateText(root, "txt_game_end_reason", "黑方领先 3.5 目", regularFont, 14f, UIPalette.InkSecondary, TextAlignmentOptions.MidlineLeft);
        Place(reason.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 30f), new Vector2(-250f, 36f), new Vector2(0f, 0f));
        Button exit = CreateTextButton(root, "btn_game_end_exit", "退出对局", mediumFont, 18f, UIPalette.Paper, UIPalette.Ink, true);
        Place(exit.transform as RectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 28f), new Vector2(180f, 52f), new Vector2(1f, 0f));
        return root.gameObject;
    }

    private static GameObject CreateSettingsDrawer(
        RectTransform parent,
        out Button scrim,
        out Button close,
        out Button requestScore,
        out Button takeBack,
        out Button resign,
        out Button exit,
        out GameObject inlineConfirm,
        out TextMeshProUGUI inlineTitle,
        out TextMeshProUGUI inlineContent,
        out TextMeshProUGUI inlineConfirmText,
        out Button inlineCancel,
        out Button inlineConfirmButton,
        out RectTransform sheet)
    {
        RectTransform root = CreateRect(parent, "panel_duel_settings", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Image scrimImage = CreateImage(root, "img_settings_scrim", UIPalette.Scrim, null, true);
        Place(scrimImage.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        scrim = scrimImage.gameObject.AddComponent<Button>();
        scrim.targetGraphic = scrimImage;
        scrim.transition = Selectable.Transition.None;

        sheet = CreateRect(root, "panel_settings_sheet", new Vector2(1f, 0f), Vector2.one, Vector2.zero, new Vector2(718f, 0f), new Vector2(1f, 0.5f));
        Image sheetImage = CreateImage(sheet, "img_sheet", UIPalette.Paper, Sprite("ui_panel"), false);
        Place(sheetImage.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Image handle = CreateImage(sheet, "img_drawer_handle", WithAlpha(UIPalette.Ink, 0.15f), Sprite("ui_capsule"), false);
        Place(handle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(40f, 4f), new Vector2(0.5f, 1f));

        TextMeshProUGUI meta = CreateText(sheet, "txt_settings_meta", "电脑对局  ·  第 57 手", mediumFont, 13f, UIPalette.InkTertiary, TextAlignmentOptions.TopLeft);
        Place(meta.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -44f), new Vector2(-112f, 24f), new Vector2(0f, 1f));
        TextMeshProUGUI title = CreateText(sheet, "txt_settings_title", "对局菜单", serifFont, 34f, UIPalette.Ink, TextAlignmentOptions.TopLeft);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -76f), new Vector2(-112f, 54f), new Vector2(0f, 1f));
        close = CreateIconButton(sheet, "btn_settings_close", "icon_close", new Vector2(-42f, -50f), true);
        Image headerLine = CreateImage(sheet, "img_header_hairline", WithAlpha(UIPalette.Ink, 0.14f), null, false);
        Place(headerLine.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -154f), new Vector2(-96f, 1f), new Vector2(0.5f, 1f));

        RectTransform list = CreateRect(sheet, "panel_settings_list", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -168f), new Vector2(-96f, 600f), new Vector2(0.5f, 1f));
        VerticalLayoutGroup layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 0f;

        requestScore = CreateSettingsRow(list, "btn_settings_request_score", "请求数子", "按当前局面计算结果", false);
        takeBack = CreateSettingsRow(list, "btn_settings_take_back", "悔棋", "回退到你的上一手", false);
        resign = CreateSettingsRow(list, "btn_settings_resign", "认输", "确认后对局立即结束", true);
        inlineConfirm = CreateInlineConfirmation(list, out inlineTitle, out inlineContent, out inlineConfirmText, out inlineCancel, out inlineConfirmButton);
        inlineConfirm.SetActive(false);
        exit = CreateSettingsRow(list, "btn_settings_exit", "退出对局", "确认后返回主菜单", false);
        return root.gameObject;
    }

    private static Button CreateSettingsRow(RectTransform parent, string name, string titleValue, string detailValue, bool danger)
    {
        RectTransform root = CreateRect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 94f), new Vector2(0.5f, 0.5f));
        LayoutElement element = root.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 94f;
        Image hit = CreateImage(root, "img_hit", Color.clear, null, true);
        Place(hit.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.colors = PaperGhostButtonColors();
        root.gameObject.AddComponent<UIButtonFeedback>();
        Color titleColor = danger ? UIPalette.Accent : UIPalette.Ink;
        TextMeshProUGUI title = CreateText(root, name + "_text", titleValue, mediumFont, 20f, titleColor, TextAlignmentOptions.MidlineLeft);
        Place(title.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -16f), new Vector2(-48f, 32f), new Vector2(0f, 1f));
        TextMeshProUGUI detail = CreateText(root, "txt_detail", detailValue, regularFont, 14f, UIPalette.InkSecondary, TextAlignmentOptions.MidlineLeft);
        Place(detail.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -50f), new Vector2(-48f, 24f), new Vector2(0f, 1f));
        TextMeshProUGUI chevron = CreateText(root, "txt_chevron", "›", regularFont, 26f, UIPalette.InkTertiary, TextAlignmentOptions.Center);
        Place(chevron.rectTransform, new Vector2(1f, 0f), Vector2.one, Vector2.zero, new Vector2(36f, 0f), new Vector2(1f, 0.5f));
        Image line = CreateImage(root, "img_hairline", WithAlpha(UIPalette.Ink, 0.12f), null, false);
        Place(line.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f), new Vector2(0.5f, 0f));
        return button;
    }

    private static GameObject CreateInlineConfirmation(RectTransform parent, out TextMeshProUGUI title, out TextMeshProUGUI content,
        out TextMeshProUGUI confirmText, out Button cancel, out Button confirm)
    {
        RectTransform root = CreateRect(parent, "panel_settings_inline_confirm", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 176f), new Vector2(0.5f, 0.5f));
        LayoutElement element = root.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 176f;
        Image plate = CreateImage(root, "img_confirm_plate", WithAlpha(UIPalette.Accent, 0.06f), Sprite("ui_panel"), false);
        Place(plate.rectTransform, Vector2.zero, Vector2.one, new Vector2(-8f, 0f), new Vector2(16f, -10f), new Vector2(0.5f, 0.5f));
        title = CreateText(root, "txt_settings_inline_title", "认输", mediumFont, 20f, UIPalette.Accent, TextAlignmentOptions.TopLeft);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -18f), new Vector2(0f, 32f), new Vector2(0f, 1f));
        content = CreateText(root, "txt_settings_inline_content", "确认棋手认输？认输后对局立即结束。", regularFont, 14f, UIPalette.InkSecondary, TextAlignmentOptions.TopLeft);
        Place(content.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -52f), new Vector2(0f, 42f), new Vector2(0f, 1f));
        cancel = CreateTextButton(root, "btn_settings_inline_cancel", "继续对局", mediumFont, 17f, UIPalette.Ink, Color.clear, true);
        Place(cancel.transform as RectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-152f, 20f), new Vector2(136f, 48f), new Vector2(1f, 0f));
        confirm = CreateTextButton(root, "btn_settings_inline_confirm", "确认认输", mediumFont, 17f, UIPalette.Paper, UIPalette.Accent, true);
        Place(confirm.transform as RectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 20f), new Vector2(136f, 48f), new Vector2(1f, 0f));
        confirmText = confirm.GetComponentInChildren<TextMeshProUGUI>();
        return root.gameObject;
    }

    private static void ConfigurePlatformStates(StateRoot stateRoot, RectTransform info, RectTransform meta, TextMeshProUGUI moveCount,
        TextMeshProUGUI portraitMoveCount, RectTransform dynamicPanel, RectTransform actions,
        RectTransform whiteCard, RectTransform blackCard, RectTransform gameEnd, RectTransform settingsSheet,
        params ActionRow[] actionRows)
    {
        StateConfig landscape = new StateConfig { name = "Landscape" };
        StateConfig portrait = new StateConfig { name = "Portrait" };
        stateRoot.EditableStates.Add(landscape);
        stateRoot.EditableStates.Add(portrait);

        AddRectState(landscape, "Info", info, Vector2.one, Vector2.one, new Vector2(-42f, -216f), new Vector2(LandscapePanelWidth, 96f), Vector2.one);
        AddRectState(portrait, "Info", info, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -42f), new Vector2(-28f, 64f), new Vector2(0.5f, 1f));
        AddRectState(landscape, "Meta", meta, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(430f, 30f), new Vector2(0f, 1f));
        AddRectState(portrait, "Meta", meta, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(450f, 0f), new Vector2(0f, 0.5f));
        AddRectState(landscape, "MoveCount", moveCount.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, -8f), new Vector2(0f, -30f), new Vector2(0f, 0.5f));
        AddActiveState(landscape, "MoveCountVisible", moveCount.gameObject, true);
        AddActiveState(portrait, "MoveCountVisible", moveCount.gameObject, false);
        AddActiveState(landscape, "PortraitMoveCountVisible", portraitMoveCount.gameObject, false);
        AddActiveState(portrait, "PortraitMoveCountVisible", portraitMoveCount.gameObject, true);
        AddRectState(landscape, "Dynamic", dynamicPanel, Vector2.one, Vector2.one, new Vector2(-42f, -320f), new Vector2(LandscapePanelWidth, 104f), Vector2.one);
        AddRectState(portrait, "Dynamic", dynamicPanel, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -42f), new Vector2(-28f, 64f), new Vector2(0.5f, 1f));
        AddRectState(landscape, "WhiteCard", whiteCard, Vector2.one, Vector2.one, new Vector2(-42f, -42f), new Vector2(LandscapePanelWidth, 150f), Vector2.one);
        AddRectState(portrait, "WhiteCard", whiteCard, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -138f), new Vector2(-28f, 136f), new Vector2(0.5f, 1f));
        AddRectState(landscape, "BlackCard", blackCard, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-42f, 42f), new Vector2(LandscapePanelWidth, 150f), new Vector2(1f, 0f));
        AddRectState(portrait, "BlackCard", blackCard, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 140f), new Vector2(-28f, 136f), new Vector2(0.5f, 0f));
        AddRectState(landscape, "Actions", actions, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-42f, 216f), new Vector2(LandscapePanelWidth, 216f), new Vector2(1f, 0f));
        AddRectState(portrait, "Actions", actions, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(-28f, 116f), new Vector2(0.5f, 0f));
        AddRectState(landscape, "GameEnd", gameEnd, Vector2.one, Vector2.one, new Vector2(-42f, -216f), new Vector2(LandscapePanelWidth, 220f), Vector2.one);
        AddRectState(portrait, "GameEnd", gameEnd, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(-28f, 220f), new Vector2(0.5f, 0.5f));
        AddRectState(landscape, "SettingsSheet", settingsSheet, new Vector2(1f, 0f), Vector2.one, Vector2.zero, new Vector2(718f, 0f), new Vector2(1f, 0.5f));
        AddRectState(portrait, "SettingsSheet", settingsSheet, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 650f), new Vector2(0.5f, 0f));

        foreach (ActionRow row in actionRows) {
            AddRectState(landscape, row.root.name + "Icon", row.icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(24f, 24f), new Vector2(0f, 0.5f));
            AddRectState(portrait, row.root.name + "Icon", row.icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(24f, 24f), new Vector2(0.5f, 0.5f));
            AddRectState(landscape, row.root.name + "Label", row.label, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(52f, 0f), new Vector2(170f, 36f), new Vector2(0f, 0.5f));
            AddRectState(portrait, row.root.name + "Label", row.label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(150f, 30f), new Vector2(0.5f, 0.5f));
            AddActiveState(landscape, row.root.name + "Hint", row.hint.gameObject, true);
            AddActiveState(portrait, row.root.name + "Hint", row.hint.gameObject, false);
        }

        stateRoot.SetState(0, true);
    }

    private static void AddRectState(StateConfig state, string name, RectTransform target, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
    {
        StateElement element = new StateElement { name = name, elementType = StateElementType.RectTransform, target = target };
        element.Property.anchorMin = anchorMin;
        element.Property.anchorMax = anchorMax;
        element.Property.anchoredPosition = anchoredPosition;
        element.Property.sizeDelta = sizeDelta;
        element.Property.pivot = pivot;
        element.Property.localScale = Vector3.one;
        state.Elements.Add(element);
    }

    private static void AddActiveState(StateConfig state, string name, GameObject target, bool active)
    {
        StateElement element = new StateElement { name = name, elementType = StateElementType.GameObjectActive, target = target };
        element.Property.boolValue = active;
        state.Elements.Add(element);
    }

    private static void AssignPlayerCard(DuelPageUI binder, PlayerCard card, bool isBlack)
    {
        if (isBlack) {
            binder.panel_black_player = card.root;
            binder.img_black_turn_accent = card.turnAccent;
            binder.txt_black_title = card.title;
            binder.txt_black_player_name = card.name;
            binder.txt_black_subtitle = card.subtitle;
            binder.txt_black_hold_time = card.holdTime;
            binder.txt_black_byoyomi_count = card.byoyomiCount;
            binder.txt_black_byoyomi_time = card.byoyomiTime;
            return;
        }

        binder.panel_white_player = card.root;
        binder.img_white_turn_accent = card.turnAccent;
        binder.txt_white_title = card.title;
        binder.txt_white_player_name = card.name;
        binder.txt_white_subtitle = card.subtitle;
        binder.txt_white_hold_time = card.holdTime;
        binder.txt_white_byoyomi_count = card.byoyomiCount;
        binder.txt_white_byoyomi_time = card.byoyomiTime;
    }

    private static List<UIBinderNode> BuildBinderNodes(UIBinderBase binder)
    {
        var nodes = new List<UIBinderNode>();
        foreach (var field in binder.GetType().GetFields()) {
            if (field.GetValue(binder) is UnityEngine.Object value) {
                nodes.Add(new UIBinderNode(field.Name, value));
            }
        }
        return nodes;
    }

    private static void UpdateBinderEditor(GameObject root, List<UIBinderNode> nodes)
    {
        UIBinderEditor binderEditor = root.GetComponent<UIBinderEditor>();
        binderEditor.nodeList = nodes;
        binderEditor.generateTime = DateTime.UtcNow.Ticks;
        UIBinderBase binder = root.GetComponent<UIBinderBase>();
        binder.generatedTime = binderEditor.generateTime;
        EditorUtility.SetDirty(binderEditor);
        EditorUtility.SetDirty(binder);
    }

    private static GameObject CreateLayoutRow(RectTransform parent, string name, float height)
    {
        RectTransform row = CreateRect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, height), new Vector2(0.5f, 0.5f));
        LayoutElement element = row.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        return row.gameObject;
    }

    private static Button CreateIconButton(RectTransform parent, string name, string iconName, Vector2 position, bool anchoredRight = false)
    {
        RectTransform root = CreateRect(parent, name, anchoredRight ? Vector2.one : new Vector2(0f, 0f), anchoredRight ? Vector2.one : new Vector2(0f, 0f), position, new Vector2(44f, 44f), new Vector2(0.5f, 0.5f));
        Image plate = CreateImage(root, "img_plate", WithAlpha(UIPalette.Paper, 0.04f), Sprite("ui_button"), true);
        Place(plate.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        button.colors = GhostButtonColors();
        root.gameObject.AddComponent<UIButtonFeedback>();
        Image icon = CreateImage(root, "img_icon", anchoredRight ? UIPalette.InkSecondary : PaperAlpha(0.85f), Sprite(iconName), false);
        Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f), new Vector2(0.5f, 0.5f));
        return button;
    }

    private static Button CreateTextButton(RectTransform parent, string name, string value, TMP_FontAsset font, float fontSize,
        Color textColor, Color plateColor, bool usePlate)
    {
        RectTransform root = CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(160f, 48f), new Vector2(0.5f, 0.5f));
        Image plate = CreateImage(root, "img_plate", usePlate ? plateColor : Color.clear, usePlate ? Sprite("ui_button") : null, true);
        Place(plate.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        button.colors = usePlate ? SolidButtonColors(plateColor) : GhostButtonColors();
        root.gameObject.AddComponent<UIButtonFeedback>();
        TextMeshProUGUI text = CreateText(root, name + "_text", value, font, fontSize, textColor, TextAlignmentOptions.Center);
        Place(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        return button;
    }

    private static TextMeshProUGUI CreateText(RectTransform parent, string name, string value, TMP_FontAsset font, float fontSize,
        Color color, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(100f, 30f), new Vector2(0.5f, 0.5f));
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Normal;
        text.color = color;
        text.alignment = alignment;
        text.text = value;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Image CreateImage(RectTransform parent, string name, Color color, Sprite sprite, bool raycastTarget)
    {
        RectTransform rect = CreateRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(100f, 100f), new Vector2(0.5f, 0.5f));
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = sprite != null && sprite.border.sqrMagnitude <= 0f;
        image.raycastTarget = raycastTarget;
        return image;
    }

    private static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = UiLayer;
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Place(rect, anchorMin, anchorMax, anchoredPosition, sizeDelta, pivot);
        return rect;
    }

    private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.pivot = pivot;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void ClearChildren(RectTransform root)
    {
        for (int index = root.childCount - 1; index >= 0; index--) {
            UnityEngine.Object.DestroyImmediate(root.GetChild(index).gameObject);
        }
    }

    private static void RestoreCanvasRoot(RectTransform root, Canvas canvas)
    {
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.zero;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.zero;
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = Vector2.zero;
        root.pivot = Vector2.zero;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
    }

    private static Sprite Sprite(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(UIThemeTextureTool.SpritePath(name));
    }

    private static ColorBlock GhostButtonColors()
    {
        return new ColorBlock {
            normalColor = Color.clear,
            highlightedColor = PaperAlpha(0.08f),
            pressedColor = PaperAlpha(0.14f),
            selectedColor = PaperAlpha(0.08f),
            disabledColor = PaperAlpha(0.02f),
            colorMultiplier = 1f,
            fadeDuration = 0.12f,
        };
    }

    private static ColorBlock PaperGhostButtonColors()
    {
        return new ColorBlock {
            normalColor = Color.clear,
            highlightedColor = WithAlpha(UIPalette.Ink, 0.04f),
            pressedColor = WithAlpha(UIPalette.Ink, 0.08f),
            selectedColor = WithAlpha(UIPalette.Ink, 0.04f),
            disabledColor = WithAlpha(UIPalette.Ink, 0.01f),
            colorMultiplier = 1f,
            fadeDuration = 0.12f,
        };
    }

    private static ColorBlock SolidButtonColors(Color baseColor)
    {
        return new ColorBlock {
            normalColor = baseColor,
            highlightedColor = Color.Lerp(baseColor, Color.white, 0.08f),
            pressedColor = Color.Lerp(baseColor, Color.black, 0.08f),
            selectedColor = Color.Lerp(baseColor, Color.white, 0.08f),
            disabledColor = WithAlpha(baseColor, UIPalette.DisabledAlpha),
            colorMultiplier = 1f,
            fadeDuration = 0.12f,
        };
    }

    private static Color PaperAlpha(float alpha)
    {
        return WithAlpha(UIPalette.Paper, alpha);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
