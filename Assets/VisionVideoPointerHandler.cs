using UnityEngine;
using UnityEngine.EventSystems;

public sealed class VisionVideoPointerHandler :
    MonoBehaviour,
    IPointerClickHandler
{
    private VisionImageReceiver receiver;
    private bool requireDoubleClick;
    private bool requireDirectHit;

    public void Initialize(
        VisionImageReceiver owner,
        bool doubleClick,
        bool directHitOnly = false
    )
    {
        receiver = owner;
        requireDoubleClick = doubleClick;
        requireDirectHit = directHitOnly;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (receiver == null || eventData.button != PointerEventData.InputButton.Left)
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
            receiver.ToggleVideoPopup();
        }
        else
        {
            receiver.CloseVideoPopup();
        }
    }
}
