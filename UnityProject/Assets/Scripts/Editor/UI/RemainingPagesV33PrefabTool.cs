using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class RemainingPagesV33PrefabTool
{
    private static TMP_FontAsset regularFont;
    private static TMP_FontAsset mediumFont;
    private static TMP_FontAsset serifFont;

    private sealed class PageLayout
    {
        public GameObject root;
        public RectTransform content;
        public StateRoot platform;
        public StateConfig landscape;
        public StateConfig portrait;
        public UIBinderEditor editor;

        public RectTransform Find(string path)
        {
            Transform target = content.Find(path);
            if (target == null) {
                throw new InvalidOperationException($"Missing UI node: {root.name}/{path}");
            }
            return (RectTransform)target;
        }

        public void Bind(string name, Object value)
        {
            UIBinderNode node = editor.nodeList.Find(item => item.name == name);
            if (node == null) {
                editor.nodeList.Add(new UIBinderNode(name, value));
            } else {
                node.value = value;
            }
            var field = root.GetComponent<UIBinderBase>().GetType().GetField(name);
            field?.SetValue(root.GetComponent<UIBinderBase>(), value);
        }

        public void Dual(RectTransform target, Rect wide, Rect tall)
        {
            Place(target, wide);
            Capture(landscape, target);
            Place(target, tall);
            Capture(portrait, target);
        }

        public void Window(RectTransform target, float width, float height)
        {
            SetRect(target, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height), new Vector2(0.5f, 0.5f));
            Capture(landscape, target);
            Stretch(target, 16f, 24f, 16f, 24f);
            Capture(portrait, target);
        }
    }

    private readonly struct RootState
    {
        private readonly Vector2 anchorMin;
        private readonly Vector2 anchorMax;
        private readonly Vector2 pivot;
        private readonly Vector2 size;
        private readonly Vector3 position;
        private readonly Vector3 scale;
        private readonly Quaternion rotation;
        private readonly RenderMode renderMode;

        public RootState(GameObject asset)
        {
            RectTransform rect = (RectTransform)asset.transform;
            anchorMin = rect.anchorMin;
            anchorMax = rect.anchorMax;
            pivot = rect.pivot;
            size = rect.sizeDelta;
            position = rect.anchoredPosition3D;
            scale = rect.localScale;
            rotation = rect.localRotation;
            renderMode = asset.GetComponent<Canvas>().renderMode;
        }

        public void Restore(GameObject root)
        {
            RectTransform rect = (RectTransform)root.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition3D = position;
            rect.localScale = scale;
            rect.localRotation = rotation;
            root.GetComponent<Canvas>().renderMode = renderMode;
        }
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/重排剩余页面 V3.3")]
    public static void Rebuild()
    {
        regularFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.RegularFontAssetPath);
        mediumFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.MediumFontAssetPath);
        serifFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.SerifFontAssetPath);
        if (regularFont == null || mediumFont == null || serifFont == null) {
            throw new InvalidOperationException("Remaining page layouts require the V3 theme fonts.");
        }

        EditorApplication.LockReloadAssemblies();
        try {
            BuildPage("DuelSetupPopup", BuildSetup);
            BuildPage("LanRoomPopup", BuildLan);
            BuildPage("ReplayPage", BuildReplay);
            BuildPage("UserInfoPopup", BuildUser);
            BuildPage("OgsFriendListPopup", page => BuildList(page, true));
            BuildPage("RecentReplayListPopup", page => BuildList(page, false));
            BuildPage("OgsFriendProfilePopup", BuildProfile);
            BuildPage("LoadingPage", BuildLoading);
            BuildPage("ConfirmPopup", BuildConfirm);
            BuildWidgets();
            BuildDuelEnd();
            AssetDatabase.SaveAssets();
        }
        finally {
            EditorApplication.UnlockReloadAssemblies();
        }
        Debug.Log("Remaining V3.3 layouts saved. Run again after generated Binder compilation to attach new fields.");
    }

    private static void BuildPage(string name, Action<PageLayout> build)
    {
        string path = "Assets/UI/Prefab/Page/" + name + ".prefab";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        RootState protectedRoot = new RootState(asset);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try {
            RectTransform content = root.transform.Find("PanelRoot") as RectTransform ?? root.transform.Find("panel_root") as RectTransform;
            if (content == null) {
                Transform[] children = root.transform.Cast<Transform>().ToArray();
                content = Rect(root.transform, "PanelRoot");
                Stretch(content);
                foreach (Transform child in children) {
                    child.SetParent(content, false);
                }
            }
            StateRoot platform = root.GetComponentsInChildren<StateRoot>(true).FirstOrDefault(state => state.name == "sr_platform");
            if (platform == null) {
                platform = Rect(content, "sr_platform").gameObject.AddComponent<StateRoot>();
                Stretch((RectTransform)platform.transform);
            }
            platform.transform.SetParent(content, false);
            platform.transform.SetAsFirstSibling();
            platform.EditableStates.Clear();
            var page = new PageLayout {
                root = root, content = content, platform = platform,
                landscape = new StateConfig { name = "Landscape" }, portrait = new StateConfig { name = "Portrait" },
                editor = root.GetComponent<UIBinderEditor>(),
            };
            platform.EditableStates.Add(page.landscape);
            platform.EditableStates.Add(page.portrait);
            page.Bind("sr_platform", platform);
            build(page);
            foreach (Shadow effect in root.GetComponentsInChildren<Shadow>(true)) Object.DestroyImmediate(effect);
            if (content.GetComponent<Image>() is Image contentPlate) {
                contentPlate.color = Color.clear;
                contentPlate.raycastTarget = false;
            }
            platform.SetState(0, true);
            protectedRoot.Restore(root);
            UIBinderBase binder = root.GetComponent<UIBinderBase>();
            string exportBefore = File.ReadAllText(page.editor.binderExportPath);
            bool missingFields = page.editor.nodeList.Any(node => binder.GetType().GetField(node.name) == null);
            if (missingFields) {
                UICodeGenerator.ExportUIScripts(page.editor);
            }
            if (missingFields && File.ReadAllText(page.editor.binderExportPath) != exportBefore) {
                page.editor.generateTime = DateTime.UtcNow.Ticks;
            }
            binder.generatedTime = page.editor.generateTime;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/更新复盘图表视角布局")]
    public static void RebuildReplayChart()
    {
        regularFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.RegularFontAssetPath);
        mediumFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.MediumFontAssetPath);
        const string path = "Assets/UI/Prefab/Page/ReplayPage.prefab";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        RootState protectedRoot = new RootState(asset);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        EditorApplication.LockReloadAssemblies();
        try {
            ReplayPageUI binder = root.GetComponent<ReplayPageUI>();
            var page = new PageLayout {
                root = root, content = (RectTransform)root.transform.Find("PanelRoot"),
                platform = binder.sr_platform, editor = root.GetComponent<UIBinderEditor>(),
                landscape = binder.sr_platform.EditableStates.Find(state => state.name == "Landscape"),
                portrait = binder.sr_platform.EditableStates.Find(state => state.name == "Portrait"),
            };
            BuildReplayChart(page);
            page.platform.SetState(0, true);
            protectedRoot.Restore(root);
            if (page.editor.nodeList.Any(node => binder.GetType().GetField(node.name) == null)) {
                UICodeGenerator.ExportUIScripts(page.editor);
                page.editor.generateTime = DateTime.UtcNow.Ticks;
            }
            binder.generatedTime = page.editor.generateTime;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
            EditorApplication.UnlockReloadAssemblies();
        }
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/更新对局设置竖屏抽屉")]
    public static void RebuildSetupPortrait()
    {
        regularFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIThemeFontAssetTool.RegularFontAssetPath);
        const string path = "Assets/UI/Prefab/Page/DuelSetupPopup.prefab";
        RootState protectedRoot = new RootState(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try {
            DuelSetupPopupUI binder = root.GetComponent<DuelSetupPopupUI>();
            var page = new PageLayout {
                root = root, content = (RectTransform)root.transform.Find("PanelRoot"),
                platform = binder.sr_platform, editor = root.GetComponent<UIBinderEditor>(),
                landscape = binder.sr_platform.EditableStates.Find(state => state.name == "Landscape"),
                portrait = binder.sr_platform.EditableStates.Find(state => state.name == "Portrait"),
            };
            BuildSetupPortrait(page);
            page.platform.SetState(0, true);
            protectedRoot.Restore(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void BuildSetupPortrait(PageLayout page)
    {
        page.platform.SetState(0, true);
        DuelSetupPopupUI binder = page.root.GetComponent<DuelSetupPopupUI>();
        RectTransform main = page.Find("panel_main");
        RectTransform layoutNode = Rect(main, "layout_setup_portrait");
        layoutNode.gameObject.SetActive(false);
        DuelSetupPortraitLayout layout = layoutNode.GetComponent<DuelSetupPortraitLayout>() ?? layoutNode.gameObject.AddComponent<DuelSetupPortraitLayout>();
        layout.canvasContent = page.content;
        layout.padding = new RectOffset(52, 52, 20, 28);
        layout.rowHeight = 76;
        layout.rowSpacing = 12;
        layout.sectionSpacing = 26;
        layout.labelWidth = 146;
        layout.paper = main;
        layout.settings = page.Find("panel_main/PanelSettings");
        layout.landscapeGrid = layout.settings.GetComponent<GridLayoutGroup>();
        layout.eyebrow = page.Find("panel_main/txt_setup_eyebrow");
        layout.heading = page.Find("panel_main/txt_setup_title");
        layout.intro = page.Find("panel_main/txt_setup_intro");
        layout.close = (RectTransform)binder.btn_close.transform;
        Capture(page.landscape, layout.close);
        layout.boardLabel = page.Find("panel_main/time_settings_label_board");
        layout.boards = new[] { (RectTransform)binder.btn_9x9.transform, (RectTransform)binder.btn_13x13.transform, (RectTransform)binder.btn_19x19.transform };
        layout.summary = page.Find("panel_main/panel_setup_preview");
        layout.summaryLabel = page.Find("panel_main/panel_setup_preview/txt_summary_label").GetComponent<TextMeshProUGUI>();
        layout.summaryMain = binder.txt_setup_summary;
        layout.summaryDetail = binder.txt_setup_detail;
        layout.start = (RectTransform)binder.btn_start.transform;
        layout.footerHint = page.Find("panel_main/txt_footer_hint");
        layout.landscapePaperColor = main.GetComponent<Image>().color;
        layout.landscapeSummaryColor = layout.summary.GetComponent<Image>().color;
        layout.handle = SetupPortraitLine(main, "img_setup_handle", Alpha(UIPalette.Ink, 0.18f));
        layout.headerLine = SetupPortraitLine(main, "img_setup_header_line", UIPalette.Hairline);
        layout.summaryLine = SetupPortraitLine(main, "img_setup_summary_line", UIPalette.Hairline);
        layout.rulesLabel = Label(main, "txt_setup_rules_label", "对局参数", 24f, UIPalette.InkSecondary);
        layout.timeLabel = Label(main, "txt_setup_time_label", "用时", 24f, UIPalette.InkSecondary);

        layout.ruleFields = new[] { SetupPortraitField(page, binder.dropdown_ai_difficulty), SetupPortraitField(page, binder.dropdown_player_color), SetupPortraitField(page, binder.dropdown_handicap) };
        layout.timeFields = new[] { SetupPortraitField(page, binder.dropdown_hold_time), SetupPortraitField(page, binder.dropdown_ogs_time_option) };
        layout.byoyomiCount = SetupPortraitField(page, binder.dropdown_byoyomi_count);
        layout.byoyomiTime = SetupPortraitField(page, binder.dropdown_byoyomi_time);
        var appearances = new List<DuelSetupPortraitLayout.TextAppearance>();
        AddSetupAppearance(appearances, layout.eyebrow.GetComponent<TextMeshProUGUI>(), 20, UIPalette.InkTertiary);
        AddSetupAppearance(appearances, layout.heading.GetComponent<TextMeshProUGUI>(), 52, UIPalette.Ink);
        AddSetupAppearance(appearances, layout.intro.GetComponent<TextMeshProUGUI>(), 24, UIPalette.InkSecondary);
        AddSetupAppearance(appearances, layout.boardLabel.GetComponent<TextMeshProUGUI>(), 22, UIPalette.InkSecondary);
        AddSetupAppearance(appearances, layout.rulesLabel.GetComponent<TextMeshProUGUI>(), 24, UIPalette.InkSecondary);
        AddSetupAppearance(appearances, layout.timeLabel.GetComponent<TextMeshProUGUI>(), 24, UIPalette.InkSecondary);
        AddSetupAppearance(appearances, layout.summaryLabel, 22, UIPalette.InkTertiary);
        AddSetupAppearance(appearances, layout.summaryMain, 32, UIPalette.Ink);
        AddSetupAppearance(appearances, layout.summaryDetail, 22, UIPalette.InkSecondary);
        AddSetupAppearance(appearances, layout.footerHint.GetComponent<TextMeshProUGUI>(), 22, UIPalette.InkTertiary);
        appearances[appearances.Count - 1].portraitAlignment = TextAlignmentOptions.Center;
        AddSetupAppearance(appearances, binder.btn_start.GetComponentInChildren<TextMeshProUGUI>(), 28, UIPalette.Paper);
        foreach (RectTransform board in layout.boards) {
            AddSetupAppearance(appearances, board.GetComponentInChildren<TextMeshProUGUI>(), 28, UIPalette.Ink);
            appearances[appearances.Count - 1].preserveColor = true;
        }
        foreach (DuelSetupPortraitLayout.Field field in layout.ruleFields.Concat(layout.timeFields).Concat(new[] { layout.byoyomiCount, layout.byoyomiTime })) {
            AddSetupAppearance(appearances, field.label, 26, UIPalette.InkSecondary);
            appearances[appearances.Count - 1].portraitAlignment = TextAlignmentOptions.MidlineLeft;
            AddSetupAppearance(appearances, field.dropdown.captionText, 28, UIPalette.Ink);
            appearances[appearances.Count - 1].portraitAlignment = TextAlignmentOptions.MidlineLeft;
            if (field.dropdown.itemText != null) AddSetupAppearance(appearances, field.dropdown.itemText, 28, UIPalette.Ink);
        }
        layout.textAppearances = appearances.ToArray();
        SetRect(main, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 1216), new Vector2(0.5f, 0));
        Capture(page.portrait, main);
        Stretch(layout.settings);
        Capture(page.portrait, layout.settings);
        foreach (RectTransform target in new[] { layout.handle, layout.headerLine, layout.summaryLine, layout.rulesLabel, layout.timeLabel, layoutNode }) {
            foreach (StateConfig state in page.platform.EditableStates) state.Elements.RemoveAll(element => element.target == target.gameObject);
            Active(page.landscape, target.gameObject, false);
            Active(page.portrait, target.gameObject, true);
        }
        page.platform.SetState(1, true);
        layout.Rebuild();
    }

    private static RectTransform SetupPortraitLine(Transform parent, string name, Color color)
    {
        RectTransform rect = Rect(parent, name);
        Image image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private static DuelSetupPortraitLayout.Field SetupPortraitField(PageLayout page, TMP_Dropdown dropdown)
    {
        RectTransform row = (RectTransform)dropdown.transform.parent;
        TextMeshProUGUI label = row.GetComponentsInChildren<TextMeshProUGUI>(true).First(text => text.name.Contains("label"));
        Capture(page.landscape, row);
        Capture(page.landscape, label.rectTransform);
        Capture(page.landscape, (RectTransform)dropdown.transform);
        RectTransform item = dropdown.template.Find("Viewport/Content/Item") as RectTransform;
        if (item != null) {
            Capture(page.landscape, dropdown.template);
            Capture(page.landscape, item);
            dropdown.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 420);
            item.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 76);
            Capture(page.portrait, dropdown.template);
            Capture(page.portrait, item);
        }
        return new DuelSetupPortraitLayout.Field { row = row, label = label, dropdown = dropdown };
    }

    private static void AddSetupAppearance(List<DuelSetupPortraitLayout.TextAppearance> appearances, TMP_Text text, float size, Color color)
    {
        appearances.Add(new DuelSetupPortraitLayout.TextAppearance {
            text = text, landscapeSize = text.fontSize, landscapeColor = text.color,
            portraitSize = size, portraitColor = color,
            landscapeAlignment = text.alignment, portraitAlignment = text.alignment,
        });
    }

    private static void BuildReplayChart(PageLayout page)
    {
        RectTransform side = page.Find("panel_main/panel_side");
        RectTransform chart = page.Find("panel_main/panel_side/rect_chart_area");
        RectTransform controls = page.Find("panel_main/panel_controls");
        RectTransform detail = page.Find("panel_main/panel_side/txt_move_detail");
        ReplaceChartLayout(page, side, Box(948, 336, 610, 320), Box(0, 986, 720, 294));
        ReplaceChartLayout(page, chart, Box(28, 62, 554, 238), Box(24, 12, 672, 198));
        ReplaceChartLayout(page, detail, Box(28, 22, 554, 30), Box(32, 212, 656, 34));
        ReplaceChartLayout(page, controls, Box(948, 680, 610, 62), Box(32, 1232, 656, 44));
        foreach (RectTransform child in controls) {
            StateElement portrait = page.portrait.Elements.Find(element => element.target == child && element.elementType == StateElementType.RectTransform);
            if (portrait != null) portrait.Property.sizeDelta = new Vector2(portrait.Property.sizeDelta.x, 44);
        }
        RectTransform heading = Label(chart, "txt_chart_heading", "胜率与目差", 20, UIPalette.Ink, mediumFont);
        Place(heading, Box(14, 10, 240, 36));
        heading.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform tabs = Rect(chart, "panel_chart_perspective");
        SetRect(tabs, Vector2.one, Vector2.one, new Vector2(-14, -8), new Vector2(224, 40), Vector2.one);
        ToggleGroup group = tabs.GetComponent<ToggleGroup>() ?? tabs.gameObject.AddComponent<ToggleGroup>();
        group.allowSwitchOff = false;
        BuildChartPerspectiveToggle(page, tabs, group, "black", "黑方视角", 0);
        BuildChartPerspectiveToggle(page, tabs, group, "white", "白方视角", 1);
        RectTransform summary = page.Find("panel_main/panel_side/rect_chart_area/txt_scrub_preview");
        StyleText(summary, "黑胜率 --  ·  目差 黑--", 16, regularFont, UIPalette.InkSecondary);
        SetRect(summary, new Vector2(0, 1), Vector2.one, new Vector2(0, -48), new Vector2(-28, 26), new Vector2(0.5f, 1));
        RectTransform plot = page.Find("panel_main/panel_side/rect_chart_area/chart_analysis");
        Stretch(plot, 14, 98, 14, 32);
        RectTransform hit = page.Find("panel_main/panel_side/rect_chart_area/img_move_scrubber_hit");
        Stretch(hit, 14, 98, 14, 32);
        RectTransform cursor = page.Find("panel_main/panel_side/rect_chart_area/img_chart_cursor");
        SetRect(cursor, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -33), new Vector2(2, -130), new Vector2(0.5f, 0.5f));
        cursor.GetComponent<Image>().color = Alpha(UIPalette.Ink, 0.85f);
        cursor.GetComponent<Image>().raycastTarget = false;
        for (int index = 0; index < 2; index++) {
            RectTransform legend = Label(chart, index == 0 ? "txt_chart_legend_winrate" : "txt_chart_legend_score", index == 0 ? "━  胜率" : "━  目差", 13, index == 0 ? UIPalette.Analysis : UIPalette.Accent);
            legend.gameObject.SetActive(true);
            SetRect(legend, Vector2.zero, Vector2.zero, new Vector2(14 + index * 88, 4), new Vector2(84, 24), Vector2.zero);
        }
        RectTransform caption = Label(chart, "txt_chart_sign_hint", "正值领先 · 负值落后", 12, UIPalette.InkTertiary);
        SetRect(caption, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14, 4), new Vector2(200, 24), new Vector2(1, 0));
        caption.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineRight;
        RectTransform progress = page.root.GetComponent<ReplayPageUI>().txt_analysis_placeholder.rectTransform;
        progress.SetParent(chart, false);
        foreach (StateConfig state in page.platform.EditableStates) state.Elements.RemoveAll(element => element.target == progress || element.target == progress.gameObject);
        StyleText(progress, string.Empty, 13, regularFont, UIPalette.InkSecondary);
        SetRect(progress, new Vector2(0, 1), Vector2.one, new Vector2(0, -74), new Vector2(-28, 22), new Vector2(0.5f, 1));
        progress.gameObject.SetActive(false);
        BuildReplayTryControls(page);
    }

    private static void BuildReplayTryControls(PageLayout page)
    {
        RectTransform summary = page.Find("panel_main/txt_summary");
        Place(summary, Box(32, 126, 650, 44));
        Capture(page.portrait, summary);
        RectTransform status = page.Find("panel_main/txt_status");
        Place(status, Box(32, 170, 650, 30));
        Capture(page.portrait, status);
        RectTransform actions = page.Find("panel_main/panel_replay_actions");
        Place(actions, Box(32, 200, 656, 40));
        Capture(page.portrait, actions);
        int index = 0;
        foreach (string name in new[] { "panel_try_mode", "btn_ownership", "btn_ai_analysis", "btn_export_sgf" }) {
            RectTransform action = (RectTransform)actions.Find(name);
            Place(action, Box(index * 164, 0, 154, 40));
            Capture(page.portrait, action);
            index++;
        }
        RectTransform colors = page.Find("panel_main/panel_move_color");
        Place(colors, Box(32, 244, 656, 40));
        Capture(page.portrait, colors);
        HorizontalLayoutGroup layout = colors.GetComponent<HorizontalLayoutGroup>();
        layout.padding.top = 1;
        layout.padding.bottom = 1;
        layout.spacing = 4;
    }

    private static void ReplaceChartLayout(PageLayout page, RectTransform target, Rect landscape, Rect portrait)
    {
        foreach (StateConfig state in page.platform.EditableStates) state.Elements.RemoveAll(element => element.target == target && element.elementType == StateElementType.RectTransform);
        page.Dual(target, landscape, portrait);
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/验证复盘图表视角")]
    public static void ValidateReplayChart()
    {
        const string path = "Assets/UI/Prefab/Page/ReplayPage.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try {
            ReplayPageUI binder = root.GetComponent<ReplayPageUI>();
            if (binder.toggle_chart_black == null || binder.toggle_chart_white == null ||
                binder.toggle_chart_black.group != binder.toggle_chart_white.group || binder.toggle_chart_black.group.allowSwitchOff) {
                throw new InvalidOperationException("Replay perspective toggles are not an exclusive pair.");
            }
            var component = new SceneComponentReplay(null) { isReplayLoaded = true, isChartReady = true };
            for (int index = 0; index < 12; index++) component.replayMoves.Add(new ReplayMoveState());
            component.replayCursorMoveIndex = 4;
            ReplayChartPoint point = new ReplayChartPoint { moveIndex = 4, hasWinrate = true, blackWinrate = 0.54f, hasScoreLead = true, scoreLead = 3.5f };
            component.chartPoints.Add(point);
            ReplaySystem replay = new ReplaySystem(null);
            typeof(ReplaySystem).GetField("compReplay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(replay, component);
            if (replay.BuildChartSummaryText(false) != "黑胜率 54% · 目差 黑+3.5" ||
                replay.BuildChartSummaryText(true) != "白胜率 46% · 目差 白-3.5") {
                throw new InvalidOperationException("Replay perspective summary conversion failed.");
            }
            point.scoreLead = -3.5f;
            if (!replay.BuildChartSummaryText(false).Contains("黑-3.5") || !replay.BuildChartSummaryText(true).Contains("白+3.5")) {
                throw new InvalidOperationException("Replay negative score conversion failed.");
            }
            point.scoreLead = 0f;
            if (!replay.BuildChartSummaryText(true).Contains("均势")) throw new InvalidOperationException("Replay zero score failed.");
            point.scoreLead = 3.5f;
            if (!replay.BuildChartSummaryText(true, 5).Contains("--")) throw new InvalidOperationException("Missing point was substituted.");
            var sample = new List<ReplayChartPoint> {
                new ReplayChartPoint { moveIndex = 0, hasWinrate = true, blackWinrate = 0f, hasScoreLead = true, scoreLead = -8f }, point,
                new ReplayChartPoint { moveIndex = 12, hasWinrate = true, blackWinrate = 1f, hasScoreLead = true, scoreLead = 8f },
            };
            var populate = typeof(ReplayAnalysisChartGraphic).GetMethod("OnPopulateMesh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            var chartPosition = typeof(ReplayAnalysisChartGraphic).GetMethod("GetChartPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            foreach (string layout in new[] { "Landscape", "Portrait" }) {
                binder.sr_platform.SetState(layout, true);
                binder.chart_analysis.SetData(sample, 12);
                binder.chart_analysis.SetCursorMoveIndex(4);
                foreach (ReplayChartPoint entry in sample) {
                    foreach (bool winrate in new[] { false, true }) {
                        Rect rect = binder.chart_analysis.rectTransform.rect;
                        binder.chart_analysis.SetPerspective(false);
                        Vector2 black = (Vector2)chartPosition.Invoke(binder.chart_analysis, new object[] { rect, entry, winrate });
                        binder.chart_analysis.SetPerspective(true);
                        Vector2 white = (Vector2)chartPosition.Invoke(binder.chart_analysis, new object[] { rect, entry, winrate });
                        if (!Mathf.Approximately(black.x, white.x) || !Mathf.Approximately(black.y + white.y, rect.yMin + rect.yMax)) {
                            throw new InvalidOperationException("Replay chart mirroring failed.");
                        }
                    }
                }
                using (var vertices = new VertexHelper()) {
                    populate.Invoke(binder.chart_analysis, new object[] { vertices });
                    if (vertices.currentVertCount == 0 || vertices.currentVertCount >= 65000) throw new InvalidOperationException("Invalid replay chart mesh size.");
                    for (int index = 0; index < vertices.currentVertCount; index++) {
                        UIVertex vertex = new UIVertex();
                        vertices.PopulateUIVertex(ref vertex, index);
                        if (float.IsNaN(vertex.position.x) || float.IsNaN(vertex.position.y) || float.IsInfinity(vertex.position.x) || float.IsInfinity(vertex.position.y)) {
                            throw new InvalidOperationException("Invalid replay chart vertex.");
                        }
                    }
                }
            }
            if (point.blackWinrate != 0.54f || point.scoreLead != 3.5f || component.replayCursorMoveIndex != 4) {
                throw new InvalidOperationException("Perspective switch changed source replay data.");
            }
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.localScale = Vector3.one;
            rootRect.sizeDelta = UICanvasResolutionProfile.EditorMobilePreviewReferenceResolution;
            binder.sr_platform.SetState("Portrait", true);
            binder.panel_move_color.SetActive(true);
            binder.panel_try_mode.SetActive(true);
            RectTransform colors = (RectTransform)binder.panel_move_color.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(colors);
            Transform main = root.transform.Find("PanelRoot/panel_main");
            Bounds colorBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(main, colors);
            Bounds actionBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(main, main.Find("panel_replay_actions"));
            Bounds boardBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(main, main.Find("panel_board"));
            if (colorBounds.min.y <= boardBounds.max.y || actionBounds.min.y <= colorBounds.max.y) {
                throw new InvalidOperationException("Portrait replay try controls overlap the board or action row.");
            }
            const string report = "Replay chart validation passed: signed summaries, both mirrored series, missing/zero scores, horizontal/portrait meshes, unchanged source data, portrait try controls above board.";
            Directory.CreateDirectory("Temp/WeiqiXN/ThemePreview");
            File.WriteAllText("Temp/WeiqiXN/ThemePreview/replay_chart_validation.txt", report);
            Debug.Log(report);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void BuildChartPerspectiveToggle(PageLayout page, RectTransform parent, ToggleGroup group, string side, string caption, int index)
    {
        RectTransform rect = Rect(parent, "toggle_chart_" + side);
        Place(rect, Box(index * 112, 0, 112, 40));
        Image image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;
        Toggle toggle = rect.GetComponent<Toggle>() ?? rect.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = image;
        toggle.transition = Selectable.Transition.None;
        toggle.group = group;
        RectTransform label = Label(rect, "txt_perspective", caption, 15, UIPalette.Ink, mediumFont);
        Stretch(label);
        label.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        RectTransform underline = Rect(rect, "img_selection");
        Image selection = underline.GetComponent<Image>() ?? underline.gameObject.AddComponent<Image>();
        selection.color = UIPalette.Accent;
        selection.raycastTarget = false;
        SetRect(underline, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(-8, 2), new Vector2(0.5f, 0));
        toggle.graphic = selection;
        toggle.SetIsOnWithoutNotify(index == 0);
        page.Bind("toggle_chart_" + side, toggle);
    }

    [MenuItem(CustomEditorMenuPaths.UI + "/验证剩余页面 V3.3")]
    public static void Validate()
    {
        string[] names = { "DuelPage", "DuelSetupPopup", "LanRoomPopup", "ReplayPage", "UserInfoPopup",
            "OgsFriendListPopup", "RecentReplayListPopup", "OgsFriendProfilePopup", "LoadingPage", "ConfirmPopup",
            "OgsFriendItemWidget", "ReplayArchiveItemWidget", "LanRoomItemWidget" };
        int references = 0;
        int states = 0;
        foreach (string name in names) {
            string folder = name.EndsWith("Widget", StringComparison.Ordinal) ? "Widget" : "Page";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Prefab/" + folder + "/" + name + ".prefab");
            UIBinderBase binder = asset.GetComponent<UIBinderBase>();
            foreach (var field in binder.GetType().GetFields()) {
                if (!typeof(Object).IsAssignableFrom(field.FieldType)) continue;
                if (field.GetValue(binder) as Object == null) throw new InvalidOperationException(name + ": missing Binder " + field.Name);
                references++;
            }
            foreach (UIBinderNode node in asset.GetComponent<UIBinderEditor>().nodeList) {
                if (node.value == null) throw new InvalidOperationException(name + ": missing editor binding " + node.name);
            }
            foreach (StateRoot stateRoot in asset.GetComponentsInChildren<StateRoot>(true)) {
                foreach (StateConfig state in stateRoot.States) {
                    foreach (StateElement element in state.Elements) {
                        if (element.target == null || !element.TargetType.IsInstanceOfType(element.target)) {
                            throw new InvalidOperationException(name + ": invalid state target " + stateRoot.name + "/" + state.name);
                        }
                    }
                    states++;
                }
            }
            if (folder == "Page") {
                if (asset.GetComponent<Canvas>() == null ||
                    asset.GetComponent<CanvasScaler>() == null || asset.GetComponent<GraphicRaycaster>() == null) {
                    throw new InvalidOperationException(name + ": protected Canvas root changed");
                }
            }
        }
        string report = $"Remaining V3.3 validation passed: {names.Length} prefabs, {references} Binder references, {states} layout/content states.";
        Directory.CreateDirectory("Temp/WeiqiXN/ThemePreview");
        File.WriteAllText("Temp/WeiqiXN/ThemePreview/remaining_validation.txt", report);
        Debug.Log(report);
    }

    private static void BuildSetup(PageLayout page)
    {
        RectTransform main = page.Find("panel_main");
        page.Window(main, 1220f, 814f);
        Paper(main);
        Image mask = page.Find("mask").GetComponent<Image>();
        mask.color = UIPalette.Scrim;
        mask.sprite = null;
        Hide(main, "bg", "img_header", "section_board_text_plate", "section_time_text_plate", "txt_subtitle");
        RectTransform heading = main.Find("txt_setup_title") as RectTransform ?? main.Find("img_header/txt_title") as RectTransform;
        heading.SetParent(main, false);
        heading.name = "txt_setup_title";
        StyleText(heading, "对局设置", 36f, serifFont, UIPalette.Ink);
        page.Dual(heading, Box(446, 80, 650, 52), Box(32, 74, 560, 52));
        RectTransform eyebrow = Label(main, "txt_setup_eyebrow", "对局  ·  参数", 14f, UIPalette.InkTertiary);
        page.Dual(eyebrow, Box(446, 44, 500, 24), Box(32, 34, 500, 24));
        RectTransform intro = Label(main, "txt_setup_intro", "选定棋盘与用时后开始对局", 16f, UIPalette.InkSecondary);
        page.Dual(intro, Box(446, 140, 650, 32), Box(32, 132, 600, 32));
        RectTransform close = main.Find("btn_close") as RectTransform;
        IconButton(close, "icon_close");
        SetRect(close, Vector2.one, Vector2.one, new Vector2(-32, -32), new Vector2(48, 48), Vector2.one);
        RectTransform boardLabel = main.Find("time_settings_label_board") as RectTransform;
        StyleText(boardLabel, "棋盘", 14f, regularFont, UIPalette.InkTertiary);
        page.Dual(boardLabel, Box(446, 202, 640, 24), Box(32, 194, 600, 24));
        RectTransform sidebar = Rect(main, "panel_setup_preview");
        Paper(sidebar, UIPalette.Ink);
        page.Dual(sidebar, Box(0, 0, 398, 814), Box(32, 860, 624, 210));
        RectTransform pretitle = Label(sidebar, "txt_preview_title", "落子之前", 40f, UIPalette.Paper, serifFont);
        Place(pretitle, Box(36, 78, 326, 60));
        Active(page.landscape, pretitle.gameObject, true);
        Active(page.portrait, pretitle.gameObject, false);
        RectTransform presub = Label(sidebar, "txt_preview_hint", "先定好这一局。", 17f, Alpha(UIPalette.Paper, 0.6f));
        Place(presub, Box(36, 144, 326, 32));
        Active(page.landscape, presub.gameObject, true);
        Active(page.portrait, presub.gameObject, false);
        RectTransform summaryLabel = Label(sidebar, "txt_summary_label", "当前方案", 14f, Alpha(UIPalette.Paper, 0.6f));
        page.Dual(summaryLabel, Box(36, 580, 326, 30), Box(28, 26, 560, 30));
        RectTransform summary = Label(sidebar, "txt_setup_summary", "九路  ·  猜先  ·  分先", 24f, UIPalette.Paper, serifFont);
        page.Dual(summary, Box(36, 618, 326, 44), Box(28, 62, 560, 44));
        page.Bind("txt_setup_summary", summary.GetComponent<TextMeshProUGUI>());
        RectTransform detail = Label(sidebar, "txt_setup_detail", "无限时", 15f, Alpha(UIPalette.Paper, 0.6f));
        page.Dual(detail, Box(36, 670, 326, 70), Box(28, 114, 560, 64));
        page.Bind("txt_setup_detail", detail.GetComponent<TextMeshProUGUI>());
        RectTransform boardStateNode = Rect(sidebar, "sr_board_preview");
        StateRoot boardState = boardStateNode.GetComponent<StateRoot>() ?? boardStateNode.gameObject.AddComponent<StateRoot>();
        boardState.EditableStates.Clear();
        RectTransform boardContainer = Rect(sidebar, "panel_preview_boards");
        Stretch(boardContainer);
        Active(page.landscape, boardContainer.gameObject, true);
        Active(page.portrait, boardContainer.gameObject, false);
        string[] boardIds = { "9", "13", "19" };
        var previews = new List<RectTransform>();
        foreach (string id in boardIds) {
            RectTransform button = main.Find("btn_" + id + "x" + id) as RectTransform;
            Transform source = button.Find("preview_board_" + id);
            RectTransform preview = boardContainer.Find("preview_" + id) as RectTransform ?? sidebar.Find("preview_" + id) as RectTransform;
            if (preview == null) {
                preview = (RectTransform)Object.Instantiate(source.gameObject, boardContainer).transform;
                preview.name = "preview_" + id;
            }
            preview.SetParent(boardContainer, false);
            Place(preview, Box(36, 214, 90, 90));
            preview.localScale = Vector3.one * 3.6f;
            foreach (Image image in preview.GetComponentsInChildren<Image>(true)) {
                image.color = image.name.StartsWith("grid_") || image.name.StartsWith("star_") ? UIPalette.Ink : UIPalette.Kaya;
                image.raycastTarget = false;
            }
            previews.Add(preview);
            source.gameObject.SetActive(false);
            Transform desc = button.Find("txt_desc");
            desc.gameObject.SetActive(false);
            foreach (Transform child in button) {
                if (child.name.StartsWith("badge_")) child.gameObject.SetActive(false);
            }
            ButtonStyle(button.GetComponent<Button>(), false);
            TextMeshProUGUI label = button.GetComponentsInChildren<TextMeshProUGUI>(true).First(text => text.name == "Text (TMP)");
            StyleText(label.rectTransform, id == "9" ? "九路" : id == "13" ? "十三路" : "十九路", 20, mediumFont, UIPalette.Ink);
            Stretch(label.rectTransform);
            int index = Array.IndexOf(boardIds, id);
            page.Dual(button, Box(446 + index * 220, 236, 210, 56), Box(32 + index * 212, 230, 200, 60));
        }
        for (int selected = 0; selected < previews.Count; selected++) {
            var state = new StateConfig { name = "Board" + boardIds[selected] };
            for (int index = 0; index < previews.Count; index++) Active(state, previews[index].gameObject, selected == index);
            boardState.EditableStates.Add(state);
        }
        boardState.SetState(0, true);
        Active(page.portrait, sidebar.gameObject, true);
        page.Bind("sr_board_preview", boardState);
        RectTransform settings = main.Find("PanelSettings") as RectTransform;
        RemoveLayout(settings);
        GridLayoutGroup grid = settings.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.cellSize = new Vector2(300, 96);
        grid.spacing = new Vector2(28, 16);
        page.Dual(settings, Box(446, 348, 650, 440), Box(32, 334, 624, 440));
        foreach (Transform row in settings) {
            RectTransform rowRect = (RectTransform)row;
            RemoveLayout(rowRect);
            Image plate = row.GetComponent<Image>();
            if (plate != null) plate.color = Color.clear;
            TextMeshProUGUI caption = row.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(text => text.name.Contains("label"));
            TMP_Dropdown dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown != null && caption == null) {
                caption = Label(row, "txt_setting_label", SettingCaption(dropdown.name), 14, UIPalette.InkTertiary).GetComponent<TextMeshProUGUI>();
            }
            if (caption != null) {
                caption.gameObject.SetActive(true);
                caption.text = SettingCaption(dropdown.name);
                caption.transform.SetParent(row, false);
                StyleText(caption.rectTransform, null, 14, regularFont, UIPalette.InkTertiary);
                Place(caption.rectTransform, Box(0, 0, 300, 24));
            }
            if (dropdown != null) {
                dropdown.transform.SetParent(row, false);
                Place(dropdown.transform as RectTransform, Box(0, 30, 300, 54));
                ButtonStyle(dropdown, false, true);
                StyleText(dropdown.captionText.rectTransform, null, 19, regularFont, UIPalette.Ink);
                Stretch(dropdown.captionText.rectTransform, 16, 0, 38, 0);
                foreach (Transform child in row) {
                    if (child != caption?.transform && child != dropdown.transform) child.gameObject.SetActive(false);
                }
            }
        }
        StateRoot mode = page.root.GetComponentsInChildren<StateRoot>(true).First(state => state.name == "sr_mode");
        foreach (StateConfig state in mode.EditableStates) state.Elements.RemoveAll(element => element.elementType != StateElementType.GameObjectActive);
        mode.SetState(0, true);
        RectTransform start = main.Find("btn_start") as RectTransform;
        ButtonStyle(start.GetComponent<Button>(), true);
        StyleText(start.GetComponentInChildren<TextMeshProUGUI>().rectTransform, "开始对局", 20f, mediumFont, UIPalette.Paper);
        page.Dual(start, Box(946, 708, 230, 58), Box(32, 1110, 624, 62));
        RectTransform hint = main.Find("txt_footer_hint") as RectTransform;
        StyleText(hint, "每种模式分别保存设置", 14f, regularFont, UIPalette.InkTertiary);
        page.Dual(hint, Box(446, 724, 460, 32), Box(32, 1074, 624, 28));
        BuildSetupPortrait(page);
    }

    private static void BuildLan(PageLayout page)
    {
        RectTransform main = page.Find("panel_main");
        page.Window(main, 1140, 792);
        Paper(main);
        page.Find("mask").GetComponent<Image>().color = UIPalette.Scrim;
        Header(page, main, page.Find("panel_main/txt_title"), page.Find("panel_main/btn_close"), "寻找对局", "局域网  ·  同一网络");
        RectTransform sidebar = Rect(main, "panel_create_room");
        Paper(sidebar, UIPalette.Ink);
        page.Dual(sidebar, Box(56, 188, 320, 516), Box(32, 196, 624, 246));
        RectTransform title = Label(sidebar, "txt_create_title", "开一间房", 30f, UIPalette.Paper, serifFont);
        Place(title, Box(28, 24, 280, 50));
        RectTransform hint = Label(sidebar, "txt_create_hint", "邀请同一网络内的棋友加入", 16, Alpha(UIPalette.Paper, 0.6f));
        page.Dual(hint, Box(28, 80, 264, 60), Box(28, 82, 560, 32));
        RectTransform details = Label(sidebar, "txt_create_detail", "选择棋盘、用时与执子\n\n创建后等待另一位棋手加入", 17, Alpha(UIPalette.Paper, 0.8f));
        page.Dual(details, Box(28, 168, 264, 160), Box(28, 82, 568, 50));
        Active(page.landscape, details.gameObject, true);
        Active(page.portrait, details.gameObject, false);
        RectTransform create = sidebar.Find("btn_create_room") as RectTransform ?? page.Find("panel_main/btn_create_room");
        create.SetParent(sidebar, false);
        ButtonStyle(create.GetComponent<Button>(), false);
        StyleText(create.GetComponentInChildren<TextMeshProUGUI>().rectTransform, "设置并创建房间", 18, mediumFont, UIPalette.Ink);
        page.Dual(create, Box(28, 430, 264, 58), Box(28, 154, 568, 60));
        RectTransform listTitle = Label(main, "txt_nearby_title", "附近房间", 30f, UIPalette.Ink, serifFont);
        page.Dual(listTitle, Box(418, 188, 500, 48), Box(32, 488, 420, 48));
        RectTransform search = page.Find("panel_main/btn_search_room");
        ButtonStyle(search.GetComponent<Button>(), false, false, true);
        StyleText(search.GetComponentInChildren<TextMeshProUGUI>().rectTransform, "搜索房间", 17, mediumFont, UIPalette.Accent);
        page.Dual(search, Box(952, 192, 132, 48), Box(514, 490, 140, 48));
        RectTransform status = page.Find("panel_main/txt_status");
        StyleText(status, null, 16, regularFont, UIPalette.InkSecondary);
        page.Dual(status, Box(418, 250, 666, 68), Box(32, 550, 624, 90));
        RectTransform list = page.Find("panel_main/panel_room_list");
        Paper(list, Color.clear);
        Stretch(list, 418, 330, 56, 92);
        Capture(page.landscape, list);
        Stretch(list, 32, 654, 32, 80);
        Capture(page.portrait, list);
        RectTransform content = (RectTransform)list.Find("content_room_list");
        ConfigureListContent(content, 12);
        RectMask2D clipping = list.GetComponent<RectMask2D>() ?? list.gameObject.AddComponent<RectMask2D>();
        ScrollRect scroll = list.GetComponent<ScrollRect>() ?? list.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = list;
        scroll.horizontal = false;
        scroll.vertical = true;
        RectTransform foot = Label(main, "txt_lan_hint", "仅显示当前局域网内可加入的房间", 14, UIPalette.InkTertiary);
        SetRect(foot, Vector2.zero, new Vector2(1, 0), new Vector2(0, 36), new Vector2(-112, 30), new Vector2(0.5f, 0));
    }

    private static void BuildReplay(PageLayout page)
    {
        RectTransform main = page.Find("panel_main");
        Stretch(main);
        Image mainImage = main.GetComponent<Image>();
        if (mainImage != null) { mainImage.color = Color.clear; mainImage.raycastTarget = false; }
        foreach (Image image in main.GetComponentsInChildren<Image>(true)) {
            if (image.GetComponent<Selectable>() == null && image.name.StartsWith("panel")) image.color = Color.clear;
        }
        RectTransform side = page.Find("panel_main/panel_side");
        Paper(side, Alpha(UIPalette.Paper, 0.95f));
        page.Dual(side, Box(948, 336, 610, 320), Box(0, 1010, 720, 270));
        RectTransform title = page.Find("panel_main/txt_title");
        StyleText(title, "复盘  ·  本地对局", 14, mediumFont, Alpha(UIPalette.Paper, 0.6f));
        page.Dual(title, Box(948, 60, 520, 30), Box(32, 30, 560, 30));
        RectTransform summary = page.Find("panel_main/txt_summary");
        StyleText(summary, "黑棋 棋手  ·  白棋 KataGo", 17, regularFont, Alpha(UIPalette.Paper, 0.7f));
        page.Dual(summary, Box(948, 176, 570, 70), Box(32, 126, 650, 58));
        RectTransform status = page.Find("panel_main/txt_status");
        StyleText(status, null, 15, regularFont, Alpha(UIPalette.Paper, 0.6f));
        page.Dual(status, Box(948, 256, 570, 64), Box(32, 184, 650, 44));
        RectTransform cursor = page.Find("panel_main/panel_controls/txt_move_cursor");
        StyleText(cursor, "0 / 0", 24, serifFont, UIPalette.Ink);
        RectTransform controls = page.Find("panel_main/panel_controls");
        RemoveLayout(controls);
        Paper(controls, Alpha(UIPalette.Paper, 0.95f));
        page.Dual(controls, Box(948, 680, 610, 62), Box(32, 1148, 656, 64));
        string[] steps = { "btn_first", "btn_prev", "btn_next", "btn_last" };
        string[] labels = { "|<", "<", ">", ">|" };
        float[] positions = { 0, 116, 410, 526 };
        for (int index = 0; index < steps.Length; index++) {
            RectTransform button = (RectTransform)controls.Find(steps[index]);
            RemoveLayout(button);
            ButtonStyle(button.GetComponent<Button>(), false);
            RectTransform text = Label(button, "txt_navigation", labels[index], 24, UIPalette.Ink, mediumFont);
            Stretch(text);
            text.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            page.Dual(button, Box(positions[index], 0, 80, 60), Box(index < 2 ? index * 124 : 452 + (index - 2) * 124, 0, 80, 60));
        }
        Place(cursor, Box(210, 0, 190, 60));
        page.Dual(cursor, Box(210, 0, 190, 60), Box(220, 0, 196, 60));
        cursor.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        RectTransform detail = page.Find("panel_main/panel_side/txt_move_detail");
        StyleText(detail, "黑 Q16", 18, regularFont, UIPalette.InkSecondary);
        page.Dual(detail, Box(28, 28, 554, 54), Box(32, 218, 656, 44));
        RectTransform chart = page.Find("panel_main/panel_side/rect_chart_area");
        page.Dual(chart, Box(28, 120, 554, 172), Box(32, 26, 656, 104));
        Image chartPlate = chart.GetComponent<Image>();
        if (chartPlate != null) Paper(chart, UIPalette.PaperRaised);
        RectTransform placeholder = page.root.GetComponent<ReplayPageUI>().txt_analysis_placeholder.rectTransform;
        StyleText(placeholder, "胜率与目差", 14, regularFont, UIPalette.InkTertiary);
        Place(placeholder, Box(28, 78, 550, 26));
        placeholder.GetComponent<TextMeshProUGUI>().text = string.Empty;
        foreach (TextMeshProUGUI legend in chart.GetComponentsInChildren<TextMeshProUGUI>(true)) {
            if (legend.name.StartsWith("txt_chart_legend")) legend.gameObject.SetActive(false);
        }
        Active(page.landscape, placeholder.gameObject, true);
        Active(page.portrait, placeholder.gameObject, false);
        RectTransform close = page.Find("panel_main/btn_close");
        ButtonStyle(close.GetComponent<Button>(), false, false, true);
        StyleText(close.GetComponentInChildren<TextMeshProUGUI>().rectTransform, "退出", 18, mediumFont, UIPalette.Paper);
        page.Dual(close, Box(1470, 60, 88, 50), Box(598, 66, 88, 50));
        RectTransform actions = Rect(main, "panel_replay_actions");
        page.Dual(actions, Box(948, 764, 610, 70), Box(32, 226, 656, 48));
        RectTransform ai = actions.Find("btn_ai_analysis") as RectTransform ?? page.Find("panel_main/Panel/btn_ai_analysis");
        ai.SetParent(actions, false);
        RectTransform ownership = actions.Find("btn_ownership") as RectTransform ?? page.Find("panel_main/btn_ownership");
        ownership.SetParent(actions, false);
        RectTransform export = actions.Find("btn_export_sgf") as RectTransform ?? page.Find("panel_main/btn_export_sgf");
        export.SetParent(actions, false);
        RectTransform tryPanel = actions.Find("panel_try_mode") as RectTransform ?? page.Find("panel_main/panel_try_mode");
        tryPanel.SetParent(actions, false);
        RectTransform[] actionButtons = { tryPanel, ownership, ai, export };
        for (int index = 0; index < actionButtons.Length; index++) {
            RectTransform rect = actionButtons[index];
            Button button = rect.GetComponentInChildren<Button>(true);
            if (button != null) {
                ButtonStyle(button, false, false, true);
                TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text == null) {
                    RectTransform label = Label(button.transform, "txt_action_label", index == 0 ? "取消试下" : "AI 分析", 17, UIPalette.Paper, mediumFont);
                    Stretch(label);
                    label.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
                } else {
                    StyleText(text.rectTransform, null, 17, mediumFont, UIPalette.Paper);
                }
            }
            page.Dual(rect, Box(index * 152, 0, 142, 58), Box(index * 164, 0, 154, 48));
            if (button.transform != rect) Stretch((RectTransform)button.transform);
        }
        main.Find("Panel")?.gameObject.SetActive(false);
        RectTransform board = page.Find("panel_main/panel_board");
        page.Dual(board, Box(30, 38, 860, 810), Box(14, 290, 692, 692));
        page.Find("panel_main/panel_board/txt_board").gameObject.SetActive(false);
        RectTransform colors = page.Find("panel_main/panel_move_color");
        page.Dual(colors, Box(948, 844, 610, 46), Box(32, 244, 656, 40));
        RectTransform shape = page.Find("panel_main/panel_replay_shape_result");
        page.Dual(shape, Box(948, 306, 610, 30), Box(32, 186, 656, 36));
        foreach (TextMeshProUGUI text in shape.GetComponentsInChildren<TextMeshProUGUI>(true)) text.color = UIPalette.Paper;
        RectTransform moveHeading = Label(main, "txt_replay_heading", "复盘", 40, UIPalette.Paper, serifFont);
        page.Dual(moveHeading, Box(948, 100, 520, 66), Box(32, 64, 520, 60));
        page.Bind("txt_replay_heading", moveHeading.GetComponent<TextMeshProUGUI>());
        tryPanel.gameObject.SetActive(false);
        colors.gameObject.SetActive(false);
        shape.gameObject.SetActive(false);
        chart.GetComponent<Image>().raycastTarget = false;
        BuildReplayChart(page);
    }

    private static void BuildUser(PageLayout page)
    {
        RectTransform main = page.Find("panel_main");
        page.Window(main, 1140, 792);
        Paper(main);
        Header(page, main, page.Find("panel_main/txt_title"), page.Find("panel_main/btn_close"), "我的资料", "棋手  ·  资料");
        Hide(main, "txt_subtitle", "panel_replay");
        RectTransform local = page.Find("panel_main/panel_local_card");
        Paper(local, UIPalette.PaperRaised);
        page.Dual(local, Box(56, 188, 488, 420), Box(32, 196, 624, 280));
        Hide(local, "txt_local_title");
        RectTransform avatar = (RectTransform)local.Find("img_avatar");
        Paper(avatar, UIPalette.PaperSunken);
        Place(avatar, Box(28, 34, 64, 64));
        Place((RectTransform)local.Find("txt_user_name"), Box(112, 32, 250, 52));
        StyleText((RectTransform)local.Find("txt_user_name"), null, 30, serifFont, UIPalette.Ink);
        Place((RectTransform)local.Find("txt_user_id"), Box(112, 90, 330, 30));
        RectTransform edit = (RectTransform)local.Find("btn_edit_name");
        ButtonStyle(edit.GetComponent<Button>(), false, false, true);
        page.Dual(edit, Box(358, 34, 106, 48), Box(476, 34, 120, 48));
        RectTransform win = (RectTransform)local.Find("panel_win_count");
        RectTransform lose = (RectTransform)local.Find("panel_lose_count");
        RemoveLayout(win);
        RemoveLayout(lose);
        Paper(win, Color.clear);
        Paper(lose, Color.clear);
        Place(win, Box(28, 156, 190, 112));
        Place(lose, Box(252, 156, 190, 112));
        foreach (RectTransform stat in new[] { win, lose }) {
            TextMeshProUGUI value = stat.GetComponentsInChildren<TextMeshProUGUI>().First(text => text.name != "txt_label");
            StyleText(value.rectTransform, null, 46, serifFont, UIPalette.Ink);
            value.color = UIPalette.Ink;
            value.alignment = TextAlignmentOptions.TopLeft;
            Place(value.rectTransform, Box(0, 0, 190, 80));
            Place((RectTransform)stat.Find("txt_label"), Box(0, 80, 190, 28));
        }
        RectTransform ogs = page.Find("panel_main/panel_ogs_card");
        Paper(ogs, UIPalette.PaperRaised);
        page.Dual(ogs, Box(568, 188, 516, 420), Box(32, 484, 624, 490));
        StyleText((RectTransform)ogs.Find("txt_ogs_title"), "OGS 账号", 30, serifFont, UIPalette.Ink);
        Place((RectTransform)ogs.Find("txt_ogs_title"), Box(28, 24, 440, 50));
        RectTransform account = (RectTransform)ogs.Find("sr_ogs_account");
        Stretch(account, 28, 88, 28, 20);
        foreach (Transform state in account) {
            Stretch((RectTransform)state);
            Image image = state.GetComponent<Image>();
            if (image != null) image.color = Color.clear;
            if (state.name != "panel_ogs_logged_in") {
                foreach (TextMeshProUGUI text in state.GetComponentsInChildren<TextMeshProUGUI>(true)) {
                    StyleText(text.rectTransform, null, 18, regularFont, UIPalette.InkSecondary);
                }
                foreach (Button button in state.GetComponentsInChildren<Button>(true)) ButtonStyle(button, false, false, true);
            }
        }
        RectTransform logged = (RectTransform)account.Find("panel_ogs_logged_in");
        RemoveLayout(logged);
        Place((RectTransform)logged.Find("img_ogs_avatar"), Box(0, 0, 52, 52));
        string[] fields = { "txt_ogs_username", "txt_ogs_id", "txt_ogs_rating_overall", "txt_ogs_ranking", "txt_ogs_rating_19", "txt_ogs_rating_13", "txt_ogs_rating_9", "txt_ogs_country", "txt_ogs_registered", "txt_ogs_tags", "txt_ogs_about" };
        for (int index = 0; index < fields.Length; index++) {
            RectTransform text = (RectTransform)logged.Find(fields[index]);
            if (index < 2) Place(text, Box(68, index * 36, 388, 36));
            else Place(text, Box((index - 2) % 2 * 232, 78 + (index - 2) / 2 * 38, index == fields.Length - 1 ? 456 : 226, 36));
            StyleText(text, null, index == 0 ? 22 : 14, index == 0 ? mediumFont : regularFont, index == 0 ? UIPalette.Ink : UIPalette.InkSecondary);
        }
        foreach (string name in new[] { "btn_ogs_refresh", "btn_ogs_logout" }) {
            RectTransform button = (RectTransform)logged.Find(name);
            ButtonStyle(button.GetComponent<Button>(), false, false, true);
            SetRect(button, Vector2.zero, Vector2.zero, new Vector2(name == "btn_ogs_refresh" ? 0 : 134, 0), new Vector2(126, 40), Vector2.zero);
        }
        Paper((RectTransform)logged.Find("img_ogs_avatar"), UIPalette.PaperSunken);
        RectTransform actions = page.Find("panel_main/panel_actions");
        Paper(actions, Color.clear);
        page.Dual(actions, Box(56, 648, 1028, 90), Box(32, 1004, 624, 150));
        RemoveLayout(actions);
        RectTransform recent = (RectTransform)actions.Find("btn_open_recent_replays");
        RectTransform friends = (RectTransform)actions.Find("btn_open_ogs_friends");
        foreach (RectTransform button in new[] { recent, friends }) ButtonStyle(button.GetComponent<Button>(), false, false, true);
        foreach (RectTransform button in new[] { recent, friends }) {
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.fontSize = 22;
            label.font = mediumFont;
        }
        page.Dual(recent, Box(0, 0, 488, 46), Box(0, 0, 624, 48));
        page.Dual(friends, Box(512, 0, 516, 46), Box(0, 78, 624, 48));
        page.Dual((RectTransform)actions.Find("txt_recent_replays_summary"), Box(0, 50, 488, 28), Box(0, 50, 624, 26));
        page.Dual((RectTransform)actions.Find("txt_ogs_friend_summary"), Box(512, 50, 516, 28), Box(0, 128, 624, 26));
        RectTransform tip = page.Find("panel_main/txt_save_tip");
        SetRect(tip, Vector2.zero, new Vector2(1, 0), new Vector2(0, 20), new Vector2(-112, 28), new Vector2(0.5f, 0));
    }

    private static void BuildList(PageLayout page, bool friends)
    {
        RectTransform window = page.Find("Window");
        page.Window(window, 1140, 792);
        Paper(window);
        RectTransform header = page.Find("Window/Header");
        RemoveLayout(header);
        Paper(header, Color.clear);
        Stretch(header, 56, 40, 56, 0);
        header.anchorMin = new Vector2(0, 1);
        header.sizeDelta = new Vector2(-112, 140);
        header.pivot = new Vector2(0.5f, 1);
        header.anchoredPosition = new Vector2(0, -40);
        StyleText((RectTransform)header.Find("txt_title"), friends ? "OGS 好友" : "最近对局", 36, serifFont, UIPalette.Ink);
        Place((RectTransform)header.Find("txt_title"), Box(0, 28, 560, 56));
        RectTransform eyebrow = Label(header, "txt_list_eyebrow", friends ? "棋友  ·  OGS" : "棋谱  ·  本机存档", 14, UIPalette.InkTertiary);
        Place(eyebrow, Box(0, 0, 520, 26));
        RectTransform close = (RectTransform)header.Find("btn_close");
        IconButton(close, "icon_close");
        SetRect(close, Vector2.one, Vector2.one, new Vector2(8, 12), new Vector2(48, 48), Vector2.one);
        string[] actions = friends ? new[] { "btn_add_friend", "btn_friend_requests" } : new[] { "btn_import_sgf", "btn_free_layout" };
        for (int index = 0; index < actions.Length; index++) {
            RectTransform button = (RectTransform)header.Find(actions[index]);
            ButtonStyle(button.GetComponent<Button>(), false, false, true);
            Place(button, Box(index * 196, 92, 184, 48));
            StyleText(button.GetComponentInChildren<TextMeshProUGUI>().rectTransform, null, 18, mediumFont, index == 0 ? UIPalette.Accent : UIPalette.InkSecondary);
        }
        RectTransform body = page.Find("Window/" + (friends ? "sr_ogs_friend_state" : "sr_recent_replay_state"));
        Stretch(body, 56, 202, 56, 104);
        Capture(page.landscape, body);
        Stretch(body, 32, 208, 32, 100);
        Capture(page.portrait, body);
        foreach (Transform state in body) {
            Image plate = state.GetComponent<Image>();
            if (plate != null) plate.color = Color.clear;
            foreach (TextMeshProUGUI text in state.GetComponentsInChildren<TextMeshProUGUI>(true)) {
                StyleText(text.rectTransform, null, 18, regularFont, UIPalette.InkSecondary);
                text.alignment = TextAlignmentOptions.Center;
            }
            foreach (Button button in state.GetComponentsInChildren<Button>(true)) ButtonStyle(button, false, false, true);
        }
        RectTransform list = body.Find("Content/" + (friends ? "content_friend_list" : "content_replay_list")) as RectTransform;
        if (list != null) {
            Stretch(list);
            VerticalLayoutGroup layout = list.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.spacing = 8;
        }
        RectTransform footer = page.Find("Window/Footer");
        RemoveLayout(footer);
        Paper(footer, Color.clear);
        SetRect(footer, Vector2.zero, new Vector2(1, 0), new Vector2(0, 26), new Vector2(-112, 56), new Vector2(0.5f, 0));
        RectTransform prev = (RectTransform)footer.Find("btn_prev_page");
        RectTransform next = (RectTransform)footer.Find("btn_next_page");
        ButtonStyle(prev.GetComponent<Button>(), false, false, true);
        ButtonStyle(next.GetComponent<Button>(), false, false, true);
        SetRect(prev, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190, 0), new Vector2(136, 48), new Vector2(0.5f, 0.5f));
        SetRect(next, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(190, 0), new Vector2(136, 48), new Vector2(0.5f, 0.5f));
        Stretch((RectTransform)footer.Find("txt_page"), 190, 0, 190, 0);
        foreach (Image image in page.content.GetComponentsInChildren<Image>(true)) {
            if (image.name.ToLowerInvariant().Contains("mask")) image.color = UIPalette.Scrim;
        }
    }

    private static void BuildProfile(PageLayout page)
    {
        RectTransform window = page.Find("Window");
        page.Window(window, 1000, 740);
        Paper(window);
        RectTransform header = page.Find("Window/Header");
        RemoveLayout(header);
        Paper(header, Color.clear);
        Stretch(header, 40, 32, 40, 0);
        SetRect(header, new Vector2(0, 1), Vector2.one, new Vector2(0, -32), new Vector2(-80, 90), new Vector2(0.5f, 1));
        RectTransform title = page.Find("Window/Header/Title");
        StyleText(title, "棋友资料", 36, serifFont, UIPalette.Ink);
        Place(title, Box(0, 10, 540, 60));
        RectTransform close = page.Find("Window/Header/btn_close");
        IconButton(close, "icon_close");
        SetRect(close, Vector2.one, Vector2.one, Vector2.zero, new Vector2(48, 48), Vector2.one);
        Place(page.Find("Window/img_avatar"), Box(40, 142, 76, 76));
        Paper(page.Find("Window/img_avatar"), UIPalette.PaperSunken);
        StyleText(page.Find("Window/txt_username"), null, 30, serifFont, UIPalette.Ink);
        page.Dual(page.Find("Window/txt_username"), Box(140, 138, 790, 52), Box(140, 138, 480, 52));
        page.Dual(page.Find("Window/txt_status"), Box(140, 192, 790, 32), Box(140, 192, 480, 32));
        RectTransform rows = page.Find("Window/InfoRows");
        RemoveLayout(rows);
        page.Dual(rows, Box(40, 266, 920, 300), Box(40, 280, 608, 640));
        string[] names = { "UserId", "Country", "Overall", "Ranking", "Rating19", "Rating13", "Rating9", "Registered", "About" };
        for (int index = 0; index < names.Length; index++) {
            RectTransform row = (RectTransform)rows.Find(names[index]);
            RemoveLayout(row);
            Paper(row, Color.clear);
            page.Dual(row, Box(index % 2 * 472, index / 2 * 54, 438, index == 8 ? 100 : 48), Box(0, index * 62, 600, index == 8 ? 130 : 52));
            RectTransform text = (RectTransform)row.Find("Text");
            Stretch(text);
            StyleText(text, null, 18, regularFont, index == 2 ? UIPalette.Ink : UIPalette.InkSecondary);
        }
        foreach (Transform row in rows) if (row.name.StartsWith("RowDivider")) row.gameObject.SetActive(false);
        Hide(window, "ProfileDivider");
        RectTransform note = page.Find("Window/txt_note");
        StyleText(note, null, 14, regularFont, UIPalette.InkTertiary);
        SetRect(note, Vector2.zero, new Vector2(1, 0), new Vector2(0, 120), new Vector2(-80, 40), new Vector2(0.5f, 0));
        RectTransform invite = page.Find("Window/btn_invite_game");
        RectTransform delete = page.Find("Window/btn_delete_friend");
        ButtonStyle(invite.GetComponent<Button>(), true);
        invite.GetComponentInChildren<TextMeshProUGUI>().color = UIPalette.Paper;
        ButtonStyle(delete.GetComponent<Button>(), false, false, true);
        SetRect(invite, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 40), new Vector2(256, 58), new Vector2(1, 0));
        SetRect(delete, Vector2.zero, Vector2.zero, new Vector2(40, 40), new Vector2(190, 58), Vector2.zero);
        foreach (Image image in page.content.GetComponentsInChildren<Image>(true)) {
            if (image.name.ToLowerInvariant().Contains("mask")) image.color = UIPalette.Scrim;
        }
    }

    private static void BuildLoading(PageLayout page)
    {
        RectTransform background = page.Find("bg");
        background.GetComponent<Image>().sprite = null;
        background.GetComponent<Image>().color = UIPalette.Table;
        RectTransform status = page.Find("bg/panel_status");
        Paper(status, Color.clear);
        SetRect(status, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(600, 260), new Vector2(0.5f, 0.5f));
        RectTransform brand = Label(background, "txt_loading_brand", "弈", 88, Alpha(UIPalette.Paper, 0.95f), serifFont);
        SetRect(brand, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 86), new Vector2(260, 130), new Vector2(0.5f, 0.5f));
        brand.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
        StyleText(page.Find("bg/panel_status/txt_loading"), "正在准备棋局", 30, serifFont, UIPalette.Paper);
        Place(page.Find("bg/panel_status/txt_loading"), Box(0, 12, 600, 60));
        StyleText(page.Find("bg/panel_status/txt_detail"), null, 16, regularFont, Alpha(UIPalette.Paper, 0.6f));
        Place(page.Find("bg/panel_status/txt_detail"), Box(0, 90, 600, 60));
        RectTransform track = page.Find("bg/panel_status/img_progress_track");
        Place(track, Box(0, 182, 600, 3));
        track.GetComponent<Image>().color = Alpha(UIPalette.Paper, 0.15f);
        page.Find("bg/panel_status/img_progress_track/img_progress_fill").GetComponent<Image>().color = UIPalette.Paper;
        RectTransform percent = page.Find("bg/panel_status/txt_percent");
        StyleText(percent, null, 15, regularFont, Alpha(UIPalette.Paper, 0.65f));
        Place(percent, Box(0, 206, 600, 32));
    }

    private static void BuildConfirm(PageLayout page)
    {
        RectTransform main = page.Find("panel_main");
        Paper(main, Alpha(UIPalette.Paper, 0.96f));
        page.Find("mask").GetComponent<Image>().color = UIPalette.Scrim;
        Hide(main, "img_header", "img_accent_line", "img_tip_icon");
        StateRoot layout = Rect(page.content, "sr_dialog_layout").GetComponent<StateRoot>();
        if (layout == null) layout = page.content.Find("sr_dialog_layout").gameObject.AddComponent<StateRoot>();
        layout.EditableStates.Clear();
        foreach (string name in new[] { "Landscape", "Portrait", "ScoreLandscape", "ScorePortrait" }) {
            bool portrait = name.EndsWith("Portrait", StringComparison.Ordinal);
            bool score = name.StartsWith("Score", StringComparison.Ordinal);
            var state = new StateConfig { name = name };
            SetRect(main, portrait ? Vector2.zero : new Vector2(0.5f, 0.5f), portrait ? new Vector2(1, 0) : new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(portrait ? 0 : 600, score ? 480 : 430), portrait ? new Vector2(0.5f, 0) : new Vector2(0.5f, 0.5f));
            Capture(state, main);
            RectTransform title = page.Find("panel_main/txt_title");
            SetRect(title, new Vector2(0, 1), Vector2.one, new Vector2(0, -38), new Vector2(-80, score ? 36 : 52), new Vector2(0.5f, 1));
            Capture(state, title);
            RectTransform content = page.Find("panel_main/txt_content");
            Stretch(content, 40, score ? 90 : 114, 40, score ? 126 : 188);
            Capture(state, content);
            layout.EditableStates.Add(state);
        }
        page.Bind("sr_dialog_layout", layout);
        layout.SetState(0, true);
        page.landscape.Elements.AddRange(layout.States[0].Elements);
        page.portrait.Elements.AddRange(layout.States[1].Elements);
        StyleText(page.Find("panel_main/txt_title"), null, 30, serifFont, UIPalette.Ink);
        StyleText(page.Find("panel_main/txt_content"), null, 22, regularFont, UIPalette.InkSecondary);
        page.Find("panel_main/txt_content").GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.TopLeft;
        RectTransform input = page.Find("panel_main/input_content");
        SetRect(input, Vector2.zero, new Vector2(1, 0), new Vector2(0, 122), new Vector2(-80, 58), new Vector2(0.5f, 0));
        ButtonStyle(input.GetComponent<Selectable>(), false, true);
        RectTransform confirm = page.Find("panel_main/btn_confirm");
        RectTransform cancel = page.Find("panel_main/btn_cancel");
        ButtonStyle(confirm.GetComponent<Button>(), true);
        ButtonStyle(cancel.GetComponent<Button>(), false);
        StateRoot buttons = Rect(page.content, "sr_dialog_buttons").GetComponent<StateRoot>();
        if (buttons == null) buttons = page.content.Find("sr_dialog_buttons").gameObject.AddComponent<StateRoot>();
        buttons.EditableStates.Clear();
        for (int index = 0; index < 4; index++) {
            var state = new StateConfig { name = new[] { "Both", "ConfirmOnly", "CancelOnly", "Neither" }[index] };
            SetRect(confirm, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(index == 0 ? 134 : 0, 40), new Vector2(246, 58), new Vector2(0.5f, 0));
            Capture(state, confirm);
            SetRect(cancel, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(index == 0 ? -134 : 0, 40), new Vector2(246, 58), new Vector2(0.5f, 0));
            Capture(state, cancel);
            Active(state, confirm.gameObject, index == 0 || index == 1);
            Active(state, cancel.gameObject, index == 0 || index == 2);
            buttons.EditableStates.Add(state);
        }
        buttons.SetState(0, true);
        page.Bind("sr_dialog_buttons", buttons);
    }

    private static void BuildWidgets()
    {
        foreach (string name in new[] { "OgsFriendItemWidget", "ReplayArchiveItemWidget", "LanRoomItemWidget" }) {
            string path = "Assets/UI/Prefab/Widget/" + name + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try {
                ButtonStyle(root.GetComponent<Button>(), false, false, true);
                foreach (Image image in root.GetComponentsInChildren<Image>(true)) {
                    if (image.name.Contains("accent")) image.color = UIPalette.Accent;
                    else if (image.name.Contains("avatar")) Paper(image.rectTransform, UIPalette.PaperSunken);
                    else if (image.GetComponent<Selectable>() == null) image.color = Color.clear;
                }
                foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(true)) {
                    bool title = text.name.Contains("title") || text.name.Contains("username") || text.name == "txt_room_name";
                    StyleText(text.rectTransform, null, title ? 20 : 14, title ? mediumFont : regularFont, title ? UIPalette.Ink : UIPalette.InkSecondary);
                    text.alignment = TextAlignmentOptions.TopLeft;
                    text.enableWordWrapping = false;
                }
                LayoutWidgetRow(root, name);
                if (name == "OgsFriendItemWidget") BuildFriendInvitationActions(root);
                RectTransform line = Rect(root.transform, "img_row_divider");
                Image divider = line.GetComponent<Image>() ?? line.gameObject.AddComponent<Image>();
                divider.color = UIPalette.Hairline;
                divider.raycastTarget = false;
                SetRect(line, Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(-32, 1), new Vector2(0.5f, 0));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    private static void Header(PageLayout page, RectTransform parent, RectTransform title, RectTransform close, string value, string category)
    {
        StyleText(title, value, 36, serifFont, UIPalette.Ink);
        page.Dual(title, Box(56, 80, 900, 60), Box(32, 80, 560, 60));
        RectTransform eyebrow = Label(parent, "txt_header_eyebrow", category, 14, UIPalette.InkTertiary);
        page.Dual(eyebrow, Box(56, 40, 900, 28), Box(32, 40, 560, 28));
        IconButton(close, "icon_close");
        SetRect(close, Vector2.one, Vector2.one, new Vector2(-32, -32), new Vector2(48, 48), Vector2.one);
        RectTransform line = Rect(parent, "img_header_divider");
        Image image = line.GetComponent<Image>() ?? line.gameObject.AddComponent<Image>();
        image.color = UIPalette.Hairline;
        image.raycastTarget = false;
        SetRect(line, new Vector2(0, 1), Vector2.one, new Vector2(0, -164), new Vector2(-112, 1), new Vector2(0.5f, 1));
    }

    private static void LayoutWidgetRow(GameObject root, string name)
    {
        if (name == "OgsFriendItemWidget") {
            Place((RectTransform)root.transform.Find("img_avatar"), Box(16, 16, 52, 52));
            RowText(root.transform, "txt_username", 84, 4, 200, 34);
            RowText(root.transform, "txt_meta", 84, 37, 116, 24);
            RowText(root.transform, "txt_rating", 84, 61, 116, 24);
            RectTransform status = (RectTransform)root.transform.Find("txt_status");
            SetRect(status, Vector2.one, Vector2.one, new Vector2(-116, -10), new Vector2(84, 26), new Vector2(1, 1));
            RectTransform profile = (RectTransform)root.transform.Find("btn_profile");
            SetRect(profile, Vector2.one, Vector2.one, new Vector2(-16, -38), new Vector2(88, 38), Vector2.one);
            ButtonStyle(profile.GetComponent<Button>(), false, false, true);
        } else if (name == "ReplayArchiveItemWidget") {
            RowText(root.transform, "txt_title", 16, 4, 188, 36);
            RowText(root.transform, "txt_meta", 16, 44, 188, 26);
            foreach (string field in new[] { "txt_result", "txt_status" }) {
                RectTransform text = (RectTransform)root.transform.Find(field);
                SetRect(text, Vector2.one, Vector2.one, new Vector2(-16, field == "txt_result" ? -8 : -46), new Vector2(164, 30), Vector2.one);
                text.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.TopRight;
            }
            Place((RectTransform)root.transform.Find("img_accent"), Box(0, 14, 2, 58));
        } else {
            root.transform.Find("txt_room").gameObject.SetActive(false);
            RectTransform content = (RectTransform)root.transform.Find("content");
            RemoveLayout(content);
            Stretch(content);
            RowText(content, "txt_room_name", 16, 8, 100, 34);
            RowText(content, "txt_host", 16, 46, 270, 26);
            RowText(content, "txt_config", 16, 76, 110, 26);
            foreach (string field in new[] { "txt_player_count", "txt_endpoint", "txt_join_hint" }) {
                RectTransform text = (RectTransform)content.Find(field);
                float top = field == "txt_player_count" ? 8 : field == "txt_endpoint" ? 46 : 76;
                float right = field == "txt_endpoint" ? 110 : 16;
                SetRect(text, Vector2.one, Vector2.one, new Vector2(-right, -top), new Vector2(field == "txt_endpoint" ? 154 : 84, 30), Vector2.one);
                text.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.TopRight;
            }
        }
    }

    private static void RowText(Transform parent, string name, float left, float top, float right, float height)
    {
        RectTransform rect = (RectTransform)parent.Find(name);
        SetRect(rect, new Vector2(0, 1), Vector2.one, new Vector2((left - right) * 0.5f, -top), new Vector2(-left - right, height), new Vector2(0.5f, 1));
    }

    private static void BuildFriendInvitationActions(GameObject root)
    {
        RectTransform accept = InvitationButton(root.transform, "btn_accept", "同意", 110, UIPalette.Accent);
        RectTransform reject = InvitationButton(root.transform, "btn_reject", "拒绝", 16, UIPalette.InkSecondary);
        StateRoot modes = Rect(root.transform, "sr_row_mode").GetComponent<StateRoot>();
        if (modes == null) modes = root.transform.Find("sr_row_mode").gameObject.AddComponent<StateRoot>();
        modes.EditableStates.Clear();
        foreach (bool invitation in new[] { false, true }) {
            var state = new StateConfig { name = invitation ? "Invitation" : "Normal" };
            Active(state, root.transform.Find("btn_profile").gameObject, !invitation);
            Active(state, accept.gameObject, invitation);
            Active(state, reject.gameObject, invitation);
            modes.EditableStates.Add(state);
        }
        modes.SetState(0, true);
        UIBinderEditor editor = root.GetComponent<UIBinderEditor>();
        UIBinderBase binder = root.GetComponent<UIBinderBase>();
        foreach (Object target in new Object[] { accept.GetComponent<Button>(), reject.GetComponent<Button>(), modes }) {
            string fieldName = target.name;
            UIBinderNode node = editor.nodeList.Find(item => item.name == fieldName);
            if (node == null) editor.nodeList.Add(new UIBinderNode(fieldName, target)); else node.value = target;
            binder.GetType().GetField(fieldName)?.SetValue(binder, target);
        }
        if (editor.nodeList.Any(node => binder.GetType().GetField(node.name) == null)) UICodeGenerator.ExportUIScripts(editor);
        binder.generatedTime = editor.generateTime;
    }

    private static RectTransform InvitationButton(Transform parent, string name, string caption, float right, Color color)
    {
        RectTransform rect = Rect(parent, name);
        Image image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        Button button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        RectTransform label = Label(rect, "txt_label", caption, 16, color, mediumFont);
        Stretch(label);
        ButtonStyle(button, false, false, true);
        label.GetComponent<TextMeshProUGUI>().color = color;
        SetRect(rect, Vector2.one, Vector2.one, new Vector2(-right, -38), new Vector2(88, 42), Vector2.one);
        return rect;
    }

    private static void BuildDuelEnd()
    {
        const string path = "Assets/UI/Prefab/Page/DuelPage.prefab";
        RootState rootState = new RootState(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try {
            DuelPageUI binder = root.GetComponent<DuelPageUI>();
            RectTransform result = (RectTransform)binder.panel_game_end_result.transform;
            foreach (StateConfig state in binder.sr_platform.EditableStates) {
                state.Elements.RemoveAll(element => element.target == result ||
                    element.target is Component component && component.transform.IsChildOf(result));
            }
            DuelPageV33PrefabTool.ConfigureGameEndStates(binder.sr_platform.EditableStates[0], binder.sr_platform.EditableStates[1], result);
            binder.sr_platform.SetState(0, true);
            rootState.Restore(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static RectTransform Label(Transform parent, string name, string value, float size, Color color, TMP_FontAsset font = null)
    {
        RectTransform rect = Rect(parent, name);
        TextMeshProUGUI text = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
        StyleText(rect, value, size, font ?? regularFont, color);
        text.alignment = TextAlignmentOptions.TopLeft;
        if (rect.parent != null && rect.parent.GetComponent<Button>() != null) text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return rect;
    }

    private static void StyleText(RectTransform rect, string value, float size, TMP_FontAsset font, Color color)
    {
        TextMeshProUGUI text = rect.GetComponent<TextMeshProUGUI>();
        if (value != null) text.text = value;
        text.font = font;
        text.fontSharedMaterial = font.material;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.fontStyle = FontStyles.Normal;
        text.color = color;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        if (rect.parent != null && rect.parent.GetComponent<Button>() != null) text.alignment = TextAlignmentOptions.Center;
    }

    private static string SettingCaption(string dropdownName)
    {
        switch (dropdownName) {
            case "dropdown_ai_difficulty": return "棋力";
            case "dropdown_player_color": return "执子";
            case "dropdown_handicap": return "让子";
            case "dropdown_ogs_time_option": return "匹配用时";
            case "dropdown_hold_time": return "主时间";
            case "dropdown_byoyomi_count": return "读秒次数";
            case "dropdown_byoyomi_time": return "读秒时间";
            default: throw new InvalidOperationException("Unknown setup dropdown: " + dropdownName);
        }
    }

    private static void Paper(RectTransform rect, Color? color = null)
    {
        Image image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        image.sprite = Sprite(UIThemeTextureTool.Panel);
        image.type = Image.Type.Sliced;
        image.color = color ?? Alpha(UIPalette.Paper, 0.96f);
        image.raycastTarget = image.color.a > 0f;
    }

    private static void ButtonStyle(Selectable button, bool primary, bool input = false, bool ghost = false)
    {
        Image image = button.targetGraphic as Image;
        image.sprite = ghost ? null : Sprite(input ? UIThemeTextureTool.Input : UIThemeTextureTool.Button);
        image.type = ghost ? Image.Type.Simple : Image.Type.Sliced;
        image.color = ghost ? Color.clear : Color.white;
        image.raycastTarget = true;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = input ? UIPalette.InputColors : primary ? InkButtonColors() : UIPalette.PaperButtonColors;
        if (button is Button && button.GetComponent<UIButtonFeedback>() == null) button.gameObject.AddComponent<UIButtonFeedback>();
        foreach (TextMeshProUGUI label in button.GetComponentsInChildren<TextMeshProUGUI>(true)) {
            label.color = primary ? UIPalette.Paper : UIPalette.Ink;
            if (button is Button) {
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = 18;
                label.font = mediumFont;
                label.fontSharedMaterial = mediumFont.material;
                label.fontStyle = FontStyles.Normal;
            }
        }
    }

    private static ColorBlock InkButtonColors()
    {
        ColorBlock colors = UIPalette.PaperButtonColors;
        colors.normalColor = UIPalette.Ink;
        colors.highlightedColor = UIPalette.InkSecondary;
        colors.pressedColor = UIPalette.InkCard;
        colors.selectedColor = UIPalette.Ink;
        colors.disabledColor = Alpha(UIPalette.Ink, UIPalette.DisabledAlpha);
        return colors;
    }

    private static void IconButton(RectTransform rect, string iconName)
    {
        foreach (Transform child in rect) child.gameObject.SetActive(false);
        Button button = rect.GetComponent<Button>();
        ButtonStyle(button, false, false, true);
        RectTransform icon = Rect(rect, "img_theme_icon");
        icon.gameObject.SetActive(true);
        Image image = icon.GetComponent<Image>() ?? icon.gameObject.AddComponent<Image>();
        image.sprite = Sprite(iconName);
        image.color = UIPalette.InkSecondary;
        image.raycastTarget = false;
        SetRect(icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24), new Vector2(0.5f, 0.5f));
    }

    private static void ConfigureListContent(RectTransform content, float spacing)
    {
        SetRect(content, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 1));
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private static void RemoveLayout(RectTransform rect)
    {
        foreach (LayoutGroup layout in rect.GetComponents<LayoutGroup>()) Object.DestroyImmediate(layout);
        foreach (ContentSizeFitter fitter in rect.GetComponents<ContentSizeFitter>()) Object.DestroyImmediate(fitter);
    }

    private static void Hide(Transform parent, params string[] names)
    {
        foreach (string name in names) parent.Find(name)?.gameObject.SetActive(false);
    }

    private static RectTransform Rect(Transform parent, string name)
    {
        RectTransform existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;
        GameObject target = new GameObject(name, typeof(RectTransform));
        target.layer = 5;
        target.transform.SetParent(parent, false);
        return (RectTransform)target.transform;
    }

    private static Rect Box(float left, float top, float width, float height) => new Rect(left, top, width, height);

    private static void Place(RectTransform rect, Rect box)
    {
        SetRect(rect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(box.x, -box.y), box.size, new Vector2(0, 1));
    }

    private static void Stretch(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        SetRect(rect, Vector2.zero, Vector2.one, new Vector2((left - right) * 0.5f, (bottom - top) * 0.5f), new Vector2(-left - right, -top - bottom), new Vector2(0.5f, 0.5f));
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void Capture(StateConfig state, RectTransform target)
    {
        state.Elements.RemoveAll(existing => existing.target == target && existing.elementType == StateElementType.RectTransform);
        var element = new StateElement { name = target.name, elementType = StateElementType.RectTransform, target = target };
        element.Capture();
        state.Elements.Add(element);
    }

    private static void Active(StateConfig state, GameObject target, bool active)
    {
        var element = new StateElement { name = target.name, elementType = StateElementType.GameObjectActive, target = target };
        element.Property.boolValue = active;
        state.Elements.Add(element);
    }

    private static Color Alpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
    private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(UIThemeTextureTool.SpritePath(name));
}
