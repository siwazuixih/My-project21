using UnityEngine;
using UnityEngine.EventSystems;

public sealed class TorqueCurvePointerHandler :
    MonoBehaviour,
    IPointerClickHandler
{
    private ServoTighteningController controller;
    private bool requireDoubleClick;
    private bool requireDirectHit;

    public void Initialize(
        ServoTighteningController owner,
        bool doubleClick,
        bool directHitOnly = false
    )
    {
        controller = owner;
        requireDoubleClick = doubleClick;
        requireDirectHit = directHitOnly;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (controller == null || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        if (
            requireDirectHit
            && eventData.pointerCurrentRaycast.gameObject != gameObject
        )
        {
            return;
        }

        if (requireDoubleClick && eventData.clickCount < 2)
        {
            return;
        }

        if (requireDoubleClick)
        {
            controller.ToggleCurvePopup();
        }
        else
        {
            controller.CloseCurvePopup();
        }
    }
}
