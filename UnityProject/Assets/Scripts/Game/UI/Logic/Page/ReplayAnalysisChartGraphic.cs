using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ReplayAnalysisChartGraphic : MaskableGraphic
{
    private const float AxisThickness = 1f;
    private const float LineThickness = 1.75f;
    private const int CircleSegments = 16;
    private static readonly Color AxisColor = UIPalette.Hairline;
    private static readonly Color WinrateColor = UIPalette.Analysis;
    private static readonly Color ScoreLeadColor = UIPalette.Accent;

    private readonly List<ReplayChartPoint> points = new List<ReplayChartPoint>();
    private readonly List<Vector2> linePoints = new List<Vector2>();
    private int moveCount;
    private int cursorMoveIndex = -1;
    private float maxScoreLeadAbs = 1f;
    private bool whitePerspective;

    public void SetData(IReadOnlyList<ReplayChartPoint> sourcePoints, int totalMoveCount)
    {
        points.Clear();
        if (sourcePoints != null) {
            for (int index = 0; index < sourcePoints.Count; index++) {
                ReplayChartPoint point = sourcePoints[index];
                if (point != null) points.Add(point);
            }
        }

        moveCount = Mathf.Max(totalMoveCount, 1);
        maxScoreLeadAbs = 1f;
        foreach (ReplayChartPoint point in points) {
            if (point.hasScoreLead) maxScoreLeadAbs = Mathf.Max(maxScoreLeadAbs, Mathf.Abs(point.scoreLead));
        }
        SetVerticesDirty();
    }

    public void SetPerspective(bool useWhitePerspective)
    {
        if (whitePerspective == useWhitePerspective) return;
        whitePerspective = useWhitePerspective;
        SetVerticesDirty();
    }

    public void SetCursorMoveIndex(int moveIndex)
    {
        if (cursorMoveIndex == moveIndex) return;
        cursorMoveIndex = moveIndex;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        float feather = 0.85f / Mathf.Max(canvas != null ? canvas.scaleFactor : 1f, 0.1f);
        for (int index = 0; index < 3; index++) {
            float axisY = Mathf.Lerp(rect.yMin, rect.yMax, index * 0.5f);
            linePoints.Clear();
            linePoints.Add(new Vector2(rect.xMin, axisY));
            linePoints.Add(new Vector2(rect.xMax, axisY));
            DrawPolyline(vertexHelper, AxisColor, AxisThickness, feather);
        }
        DrawSeries(vertexHelper, rect, true, feather);
        DrawSeries(vertexHelper, rect, false, feather);
        foreach (ReplayChartPoint point in points) {
            if (point.moveIndex != cursorMoveIndex) continue;
            if (point.hasWinrate) DrawCursorPoint(vertexHelper, GetChartPosition(rect, point, true), WinrateColor, feather);
            if (point.hasScoreLead) DrawCursorPoint(vertexHelper, GetChartPosition(rect, point, false), ScoreLeadColor, feather);
            break;
        }
    }

    private void DrawSeries(VertexHelper vertexHelper, Rect rect, bool winrate, float feather)
    {
        Color seriesColor = winrate ? WinrateColor : ScoreLeadColor;
        linePoints.Clear();
        foreach (ReplayChartPoint point in points) {
            if (!(winrate ? point.hasWinrate : point.hasScoreLead)) {
                DrawPolyline(vertexHelper, seriesColor, LineThickness, feather);
                linePoints.Clear();
                continue;
            }
            Vector2 current = GetChartPosition(rect, point, winrate);
            if (linePoints.Count == 0 || (current - linePoints[linePoints.Count - 1]).sqrMagnitude > 0.001f) {
                linePoints.Add(current);
            }
        }
        DrawPolyline(vertexHelper, seriesColor, LineThickness, feather);
    }

    private Vector2 GetChartPosition(Rect rect, ReplayChartPoint point, bool winrate)
    {
        float normalized = winrate ? Mathf.Clamp01(point.blackWinrate)
            : Mathf.InverseLerp(-maxScoreLeadAbs, maxScoreLeadAbs, point.scoreLead);
        if (whitePerspective) normalized = 1f - normalized;
        return new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Clamp01((float)point.moveIndex / moveCount)),
            Mathf.Lerp(rect.yMin, rect.yMax, normalized));
    }

    private void DrawPolyline(VertexHelper vertexHelper, Color lineColor, float thickness, float feather)
    {
        if (linePoints.Count == 0) return;
        float halfWidth = thickness * 0.5f;
        if (linePoints.Count == 1) {
            DrawCircle(vertexHelper, linePoints[0], halfWidth, lineColor, feather);
            return;
        }

        Color transparent = new Color(lineColor.r, lineColor.g, lineColor.b, 0f);
        int firstIndex = vertexHelper.currentVertCount;
        for (int index = 0; index < linePoints.Count; index++) {
            Vector2 previousDirection = index > 0 ? (linePoints[index] - linePoints[index - 1]).normalized
                : (linePoints[1] - linePoints[0]).normalized;
            Vector2 nextDirection = index + 1 < linePoints.Count ? (linePoints[index + 1] - linePoints[index]).normalized : previousDirection;
            Vector2 previousNormal = new Vector2(-previousDirection.y, previousDirection.x);
            Vector2 nextNormal = new Vector2(-nextDirection.y, nextDirection.x);
            Vector2 join = (previousNormal + nextNormal).normalized;
            if (join.sqrMagnitude < 0.001f) join = nextNormal;
            join *= Mathf.Min(1f / Mathf.Max(Vector2.Dot(join, nextNormal), 0.01f), 2f);
            Vector2 center = linePoints[index];
            vertexHelper.AddVert(center - join * (halfWidth + feather), transparent, Vector2.zero);
            vertexHelper.AddVert(center - join * halfWidth, lineColor, Vector2.zero);
            vertexHelper.AddVert(center + join * halfWidth, lineColor, Vector2.zero);
            vertexHelper.AddVert(center + join * (halfWidth + feather), transparent, Vector2.zero);
            if (index == 0) continue;
            int previousIndex = firstIndex + (index - 1) * 4;
            for (int strip = 0; strip < 3; strip++) {
                vertexHelper.AddTriangle(previousIndex + strip, previousIndex + strip + 1, previousIndex + strip + 5);
                vertexHelper.AddTriangle(previousIndex + strip, previousIndex + strip + 5, previousIndex + strip + 4);
            }
        }
        DrawCircle(vertexHelper, linePoints[0], halfWidth, lineColor, feather);
        DrawCircle(vertexHelper, linePoints[linePoints.Count - 1], halfWidth, lineColor, feather);
    }

    private void DrawCursorPoint(VertexHelper vertexHelper, Vector2 center, Color pointColor, float feather)
    {
        DrawCircle(vertexHelper, center, 3.5f, UIPalette.PaperRaised, feather);
        DrawCircle(vertexHelper, center, 2.25f, pointColor, feather);
    }

    private void DrawCircle(VertexHelper vertexHelper, Vector2 center, float radius, Color pointColor, float feather)
    {
        int centerIndex = vertexHelper.currentVertCount;
        vertexHelper.AddVert(center, pointColor, Vector2.zero);
        Color transparent = new Color(pointColor.r, pointColor.g, pointColor.b, 0f);
        for (int index = 0; index <= CircleSegments; index++) {
            float angle = index * Mathf.PI * 2f / CircleSegments;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            vertexHelper.AddVert(center + direction * radius, pointColor, Vector2.zero);
            vertexHelper.AddVert(center + direction * (radius + feather), transparent, Vector2.zero);
            if (index == 0) continue;
            int previousIndex = centerIndex + 1 + (index - 1) * 2;
            vertexHelper.AddTriangle(centerIndex, previousIndex, previousIndex + 2);
            vertexHelper.AddTriangle(previousIndex, previousIndex + 1, previousIndex + 3);
            vertexHelper.AddTriangle(previousIndex, previousIndex + 3, previousIndex + 2);
        }
    }
}
