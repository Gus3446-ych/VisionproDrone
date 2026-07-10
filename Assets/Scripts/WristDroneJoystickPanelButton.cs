using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class WristDroneJoystickPanelButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
{
    [SerializeField] WristDroneJoystickPanel m_Owner;
    [SerializeField] Vector2 m_Axis;
    [Header("Hover Effect")]
    [SerializeField] Color m_HoverColor = new Color(1f, 0.45f, 0.08f, 0.65f);
    [SerializeField] Color m_PressedColor = new Color(0.1f, 0.65f, 1f, 0.9f);
    [SerializeField] float m_HoverScale = 1.08f;

    Graphic m_Graphic;
    Color m_NormalColor = Color.white;
    Vector3 m_NormalScale = Vector3.one;
    bool m_PointerPressed;
    bool m_DirectHandPressed;
    bool m_PointerHover;
    bool m_DirectHover;
    bool m_WasPressed;

    public Vector2 Axis => m_Axis;

    void Awake()
    {
        CacheVisualState();
        UpdateVisualState();
    }

    public void Configure(WristDroneJoystickPanel owner)
    {
        m_Owner = owner;
        CacheVisualState();
        UpdateVisualState();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        m_PointerPressed = true;
        Debug.Log($"[WristJoystick] Button pointer down: {name}, axis = {m_Axis}", this);
        UpdatePressedState();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Debug.Log($"[WristJoystick] Button pointer up: {name}", this);
        m_PointerPressed = false;
        UpdatePressedState();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        m_PointerHover = true;
        UpdateVisualState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Debug.Log($"[WristJoystick] Button pointer exit: {name}", this);
        m_PointerHover = false;
        m_PointerPressed = false;
        UpdatePressedState();
    }

    public void SetDirectHover(bool hovered)
    {
        if (m_DirectHover == hovered)
            return;

        m_DirectHover = hovered;
        UpdateVisualState();
    }

    public void PressFromDirectHand()
    {
        if (m_DirectHandPressed)
            return;

        m_DirectHandPressed = true;
        Debug.Log($"[WristJoystick] Button direct hand press: {name}, axis = {m_Axis}", this);
        UpdatePressedState();
    }

    public void ReleaseFromDirectHand()
    {
        if (!m_DirectHandPressed)
            return;

        m_DirectHandPressed = false;
        Debug.Log($"[WristJoystick] Button direct hand release: {name}", this);
        UpdatePressedState();
    }

    void OnDisable()
    {
        if (IsPressed)
            Debug.Log($"[WristJoystick] Button disabled while pressed: {name}", this);

        m_PointerPressed = false;
        m_DirectHandPressed = false;
        m_PointerHover = false;
        m_DirectHover = false;
        UpdatePressedState();
    }

    bool IsPressed => m_PointerPressed || m_DirectHandPressed;
    bool IsHovered => m_PointerHover || m_DirectHover;

    void UpdatePressedState()
    {
        bool pressed = IsPressed;
        if (m_WasPressed != pressed)
        {
            m_WasPressed = pressed;

            if (m_Owner != null)
                m_Owner.SetStick(this, pressed ? m_Axis : Vector2.zero);

            DroneInputStatus.SetButtonPressed(name, pressed);

            if (!pressed)
                Debug.Log($"[WristJoystick] Button released: {name}", this);
        }

        UpdateVisualState();
    }

    void CacheVisualState()
    {
        if (m_Graphic == null)
            m_Graphic = GetComponent<Graphic>();

        if (m_Graphic != null)
            m_NormalColor = m_Graphic.color;

        m_NormalScale = transform.localScale;
    }

    void UpdateVisualState()
    {
        if (m_Graphic != null)
        {
            if (IsPressed)
                m_Graphic.color = m_PressedColor;
            else if (IsHovered)
                m_Graphic.color = m_HoverColor;
            else
                m_Graphic.color = m_NormalColor;
        }

        transform.localScale = IsHovered || IsPressed ? m_NormalScale * m_HoverScale : m_NormalScale;
    }
}
