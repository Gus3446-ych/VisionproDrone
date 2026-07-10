using UnityEngine;
using UnityEngine.EventSystems;

public class PressedButtonStatusReporter : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    bool m_IsPressed;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (m_IsPressed)
            return;

        m_IsPressed = true;
        DroneInputStatus.SetButtonPressed(name, true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Release();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Release();
    }

    void OnDisable()
    {
        Release();
    }

    void Release()
    {
        if (!m_IsPressed)
            return;

        m_IsPressed = false;
        DroneInputStatus.SetButtonPressed(name, false);
    }
}
