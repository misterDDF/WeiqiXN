using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ReplayAnalysisChartGraphic : MaskableGraphic
{
    private const float AxisThickness = 1.5f;
    private const float LineThickness = 1f;
    private const float PointSize = 1.5f;
    private static readonly Color AxisColor = new Color(1f, 1f, 1f, 0.25f);
    private static readonly Color WinrateColor = new Color(0.25f, 0.72f, 1f, 0.95f);
    private static readonly Color ScoreLeadColor = new Color(1f, 0.76f, 0.28f, 0.95f);

    private readonly List<ReplayChartPoint> points = new List<ReplayChartPoint>();
    private int moveCount;
    private float maxScoreLeadAbs = 1f;

    public void SetData(IReadOnlyList<ReplayChartPoint> sourcePoints, int totalMoveCount)
    {
        points.Clear();
        if (sourcePoints != null) {
            for (int i = 0; i < sourcePoints.Count; i++) {
                ReplayChartPoint point = sourcePoints[i];
                if (point != null) {
                    points.Add(point);
                }
            }
        }

        moveCount = Mathf.Max(totalMoveCount, 1);
        maxScoreLeadAbs = ResolveMaxScoreLeadAbs();
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) {
            return;
        }

        AddHorizontalLine(vh, rect, rect.yMin, AxisColor, AxisThickness);
        AddHorizontalLine(vh, rect, rect.yMin + rect.height * 0.5f, AxisColor, AxisThickness);
        AddHorizontalLine(vh, rect, rect.yMax, AxisColor, AxisThickness);

        DrawWinrateLine(vh, rect);
        DrawScoreLeadLine(vh, rect);
    }

    private void DrawWinrateLine(VertexHelper vh, Rect rect)
    {
        Vector2? previous = null;
        foreach (ReplayChartPoint point in points) {
            if (point == null || !point.hasWinrate) {
                previous = null;
                continue;
            }

            Vector2 current = GetChartPosition(rect, point.moveIndex, Mathf.Clamp01(point.blackWinrate));
            AddPoint(vh, current, WinrateColor);
            if (previous.HasValue) {
                AddSegment(vh, previous.Value, current, WinrateColor, LineThickness);
            }

            previous = current;
        }
    }

    private void DrawScoreLeadLine(VertexHelper vh, Rect rect)
    {
        Vector2? previous = null;
        foreach (ReplayChartPoint point in points) {
            if (point == null || !point.hasScoreLead) {
                previous = null;
                continue;
            }

            float normalized = Mathf.InverseLerp(-maxScoreLeadAbs, maxScoreLeadAbs, point.scoreLead);
            Vector2 current = GetChartPosition(rect, point.moveIndex, normalized);
            AddPoint(vh, current, ScoreLeadColor);
            if (previous.HasValue) {
                AddSegment(vh, previous.Value, current, ScoreLeadColor, LineThickness);
            }

            previous = current;
        }
    }

    private Vector2 GetChartPosition(Rect rect, int moveIndex, float normalizedY)
    {
        float normalizedX = moveCount <= 0 ? 0f : Mathf.Clamp01((float)moveIndex / moveCount);
        return new Vector2(
            Mathf.Lerp(rect.xMin, rect.xMax, normalizedX),
            Mathf.Lerp(rect.yMin, rect.yMax, Mathf.Clamp01(normalizedY)));
    }

    private float ResolveMaxScoreLeadAbs()
    {
        float maxAbs = 1f;
        foreach (ReplayChartPoint point in points) {
            if (point != null && point.hasScoreLead) {
                maxAbs = Mathf.Max(maxAbs, Mathf.Abs(point.scoreLead));
            }
        }

        return maxAbs;
    }

    private void AddHorizontalLine(VertexHelper vh, Rect rect, float y, Color lineColor, float thickness)
    {
        AddSegment(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), lineColor, thickness);
    }

    private void AddPoint(VertexHelper vh, Vector2 center, Color pointColor)
    {
        float halfSize = PointSize * 0.5f;
        AddQuad(vh, center + new Vector2(-halfSize, -halfSize), center + new Vector2(halfSize, halfSize), pointColor);
    }

    private void AddSegment(VertexHelper vh, Vector2 start, Vector2 end, Color segmentColor, float thickness)
    {
        Vector2 delta = end - start;
        if (delta.sqrMagnitude <= 0.001f) {
            AddPoint(vh, start, segmentColor);
            return;
        }

        Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (thickness * 0.5f);
        int index = vh.currentVertCount;
        vh.AddVert(start - normal, segmentColor, Vector2.zero);
        vh.AddVert(start + normal, segmentColor, Vector2.zero);
        vh.AddVert(end + normal, segmentColor, Vector2.zero);
        vh.AddVert(end - normal, segmentColor, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }

    private void AddQuad(VertexHelper vh, Vector2 min, Vector2 max, Color quadColor)
    {
        int index = vh.currentVertCount;
        vh.AddVert(new Vector2(min.x, min.y), quadColor, Vector2.zero);
        vh.AddVert(new Vector2(min.x, max.y), quadColor, Vector2.zero);
        vh.AddVert(new Vector2(max.x, max.y), quadColor, Vector2.zero);
        vh.AddVert(new Vector2(max.x, min.y), quadColor, Vector2.zero);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }
}
