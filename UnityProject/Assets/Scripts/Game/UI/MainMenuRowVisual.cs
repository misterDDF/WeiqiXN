using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MainMenuRowVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private Image background;
    [SerializeField] private Image accent;
    [SerializeField] private Image arrow;
    [SerializeField] private TextMeshProUGUI title;

    private bool isPointerInside;
    private bool isPressed;
    private bool isSelected;

    public void Configure(Image backgroundImage, Image accentImage, Image arrowImage, TextMeshProUGUI titleText)
    {
        background = backgroundImage;
        accent = accentImage;
        arrow = arrowImage;
        title = titleText;
        RefreshVisual();
    }

    private void OnEnable()
    {
        RefreshVisual();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerInside = true;
        RefreshVisual();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerInside = false;
        isPressed = false;
        RefreshVisual();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPressed = true;
        RefreshVisual();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
        RefreshVisual();
    }

    public void OnSelect(BaseEventData eventData)
    {
        isSelected = true;
        RefreshVisual();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        isSelected = false;
        RefreshVisual();
    }

    private void RefreshVisual()
    {
        bool isFocused = isPointerInside || isSelected;
        if (background != null) {
            background.color = isPressed
                ? WithAlpha(UIPalette.Ink, 0.08f)
                : isFocused ? WithAlpha(UIPalette.Ink, 0.05f) : Color.clear;
        }
        if (accent != null) {
            accent.gameObject.SetActive(isFocused);
        }
        if (arrow != null) {
            arrow.gameObject.SetActive(isFocused);
        }
        if (title != null) {
            title.color = isFocused ? UIPalette.Accent : UIPalette.Ink;
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
