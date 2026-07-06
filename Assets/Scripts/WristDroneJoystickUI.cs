using TelloLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class WristDroneJoystickUI : MonoBehaviour
{
    [SerializeField] private Vector3 m_LeftPanelOffset = new Vector3(-0.07f, 0.04f, 0.11f);
    [SerializeField] private Vector3 m_RightPanelOffset = new Vector3(0.07f, 0.04f, 0.11f);
    [SerializeField] private float m_PanelScale = 0.00055f;
    [SerializeField] private bool m_FaceMainCamera = true;
    [SerializeField, Range(0.1f, 1f)] private float m_AxisStrength = 1f;

    private XRHandSubsystem handSubsystem;
    RectTransform m_LeftPanel;
    RectTransform m_RightPanel;
    Vector2 m_LeftStick;
    Vector2 m_RightStick;
    bool m_HasJoystickInput;
    bool m_SendZeroOnce;
    Sprite m_CircleSprite;
    Font m_Font;

    void Awake()
    {
        EnsureInputObjectsEnabled();
        m_Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        m_CircleSprite = CreateCircleSprite(128);
        m_LeftPanel = CreatePanel("Left Wrist Drone Joystick", true);
        m_RightPanel = CreatePanel("Right Wrist Drone Joystick", false);
        m_LeftPanel.gameObject.SetActive(false);
        m_RightPanel.gameObject.SetActive(false);
    }

    void Start()
    {
        GetHandSubsystem();
    }

    void Update()
    {
        if (!CheckHandSubsystem())
            return;

        TrackHands();
    }

    void OnDestroy()
    {
        if (!CheckHandSubsystem())
            return;

        handSubsystem.trackingAcquired -= OnHandTrackingAcquired;
        handSubsystem.trackingLost -= OnHandTrackingLost;
    }

    void LateUpdate()
    {
        if (!m_HasJoystickInput && !m_SendZeroOnce)
            return;

        float lx = m_RightStick.x * m_AxisStrength;
        float ly = m_RightStick.y * m_AxisStrength;
        float rx = m_LeftStick.x * m_AxisStrength;
        float ry = m_LeftStick.y * m_AxisStrength;

        Tello.controllerState.setAxis(lx, ly, rx, ry);
        m_SendZeroOnce = false;
    }

    public void SetStick(bool leftWristJoystick, Vector2 value)
    {
        if (leftWristJoystick)
            m_LeftStick = value;
        else
            m_RightStick = value;

        m_HasJoystickInput = m_LeftStick != Vector2.zero || m_RightStick != Vector2.zero;
        if (!m_HasJoystickInput)
            m_SendZeroOnce = true;
    }

    bool CheckHandSubsystem()
    {
        if (handSubsystem == null)
        {
#if !UNITY_EDITOR
            Debug.LogError("Could not find Hand Subsystem");
#endif
            enabled = false;
            return false;
        }

        return true;
    }

    void GetHandSubsystem()
    {
        XRGeneralSettings xrGeneralSettings = XRGeneralSettings.Instance;
        if (xrGeneralSettings == null)
        {
            Debug.LogError("XR general settings not set");
            return;
        }

        XRManagerSettings manager = xrGeneralSettings.Manager;
        if (manager != null)
        {
            XRLoader loader = manager.activeLoader;
            if (loader != null)
            {
                handSubsystem = loader.GetLoadedSubsystem<XRHandSubsystem>();
                Debug.Log($"[WristJoystick] handSubsystem = {(handSubsystem == null ? "NULL" : "OK")}");
                if (!CheckHandSubsystem())
                    return;

                handSubsystem.Start();
                handSubsystem.trackingAcquired += OnHandTrackingAcquired;
                handSubsystem.trackingLost += OnHandTrackingLost;
            }
        }
    }

    void OnHandTrackingAcquired(XRHand hand)
    {
        if (hand.handedness == Handedness.Left && m_LeftPanel != null)
            m_LeftPanel.gameObject.SetActive(true);

        if (hand.handedness == Handedness.Right && m_RightPanel != null)
            m_RightPanel.gameObject.SetActive(true);
    }

    void OnHandTrackingLost(XRHand hand)
    {
        if (hand.handedness == Handedness.Left && m_LeftPanel != null)
            m_LeftPanel.gameObject.SetActive(false);

        if (hand.handedness == Handedness.Right && m_RightPanel != null)
            m_RightPanel.gameObject.SetActive(false);
    }

    void TrackHands()
    {
        if (!handSubsystem.running)
            return;

        AttachPanelToJoint(m_LeftPanel, handSubsystem.leftHand.GetJoint(XRHandJointID.Wrist), m_LeftPanelOffset);
        AttachPanelToJoint(m_RightPanel, handSubsystem.rightHand.GetJoint(XRHandJointID.Wrist), m_RightPanelOffset);
    }

    void AttachPanelToJoint(RectTransform panel, XRHandJoint joint, Vector3 localOffset)
    {
        if (panel == null)
            return;

        if (joint.id == XRHandJointID.Invalid || !joint.TryGetPose(out Pose pose))
        {
            panel.gameObject.SetActive(false);
            return;
        }

        if (!panel.gameObject.activeSelf)
            panel.gameObject.SetActive(true);

        panel.position = pose.position + pose.rotation * localOffset;

        Camera mainCamera = Camera.main;
        if (m_FaceMainCamera && mainCamera != null)
        {
            Vector3 toPanel = panel.position - mainCamera.transform.position;
            if (toPanel.sqrMagnitude > 0.0001f)
                panel.rotation = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
        }
        else
        {
            panel.rotation = pose.rotation;
        }
    }

    RectTransform CreatePanel(string panelName, bool leftWristJoystick)
    {
        GameObject canvasObject = new GameObject(panelName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(460f, 300f);
        rect.localScale = Vector3.one * m_PanelScale;

        CreateCircle(rect, "Base", Vector2.zero, new Vector2(260f, 260f), new Color(0.1f, 0.12f, 0.14f, 0.45f));
        CreateCircle(rect, "Center", Vector2.zero, new Vector2(92f, 92f), new Color(0.35f, 0.55f, 0.7f, 0.22f));

        if (leftWristJoystick)
        {
            CreateCommandButton(rect, "TAKE OFF", new Vector2(-138f, -128f), new Vector2(128f, 46f), Tello.takeOff);
            CreateLabel(rect, "Fly Up / Down", new Vector2(-80f, 132f), 24);
            CreateLabel(rect, "Turn Left / Right", new Vector2(100f, 0f), 18);
            CreateButton(rect, "UP", new Vector2(0f, 76f), new Vector2(80f, 86f), leftWristJoystick, new Vector2(0f, 1f));
            CreateButton(rect, "DOWN", new Vector2(0f, -76f), new Vector2(80f, 86f), leftWristJoystick, new Vector2(0f, -1f));
            CreateButton(rect, "YAW L", new Vector2(-92f, 0f), new Vector2(100f, 74f), leftWristJoystick, new Vector2(-1f, 0f));
            CreateButton(rect, "YAW R", new Vector2(92f, 0f), new Vector2(100f, 74f), leftWristJoystick, new Vector2(1f, 0f));
            CreateButton(rect, "STOP", Vector2.zero, new Vector2(82f, 58f), leftWristJoystick, Vector2.zero);
        }
        else
        {
            CreateCommandButton(rect, "LAND", new Vector2(138f, -128f), new Vector2(128f, 46f), Tello.land);
            CreateLabel(rect, "Fly Forward / Backward", new Vector2(0f, 132f), 24);
            CreateLabel(rect, "Fly Left / Right", new Vector2(-112f, 0f), 18);
            CreateButton(rect, "FWD", new Vector2(0f, 76f), new Vector2(80f, 86f), leftWristJoystick, new Vector2(0f, 1f));
            CreateButton(rect, "BACK", new Vector2(0f, -76f), new Vector2(80f, 86f), leftWristJoystick, new Vector2(0f, -1f));
            CreateButton(rect, "LEFT", new Vector2(-92f, 0f), new Vector2(100f, 74f), leftWristJoystick, new Vector2(-1f, 0f));
            CreateButton(rect, "RIGHT", new Vector2(92f, 0f), new Vector2(100f, 74f), leftWristJoystick, new Vector2(1f, 0f));
            CreateButton(rect, "STOP", Vector2.zero, new Vector2(82f, 58f), leftWristJoystick, Vector2.zero);
        }

        return rect;
    }

    void CreateCircle(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.sprite = m_CircleSprite;
        image.color = color;
        image.raycastTarget = false;
    }

    void CreateButton(RectTransform parent, string text, Vector2 position, Vector2 size, bool leftWristJoystick, Vector2 axis)
    {
        GameObject go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(WristJoystickButton));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.color = axis == Vector2.zero ? new Color(0.15f, 0.27f, 0.34f, 0.75f) : new Color(0.95f, 0.33f, 0.04f, 0.18f);

        var outline = go.GetComponent<Outline>();
        outline.effectColor = axis == Vector2.zero ? new Color(0.2f, 0.75f, 1f, 0.8f) : new Color(1f, 0.32f, 0f, 0.95f);
        outline.effectDistance = new Vector2(3f, -3f);

        var button = go.GetComponent<WristJoystickButton>();
        button.Initialize(this, leftWristJoystick, axis);

        CreateLabel(rect, text, Vector2.zero, axis == Vector2.zero ? 18 : 22);
    }

    void CreateCommandButton(RectTransform parent, string text, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
    {
        GameObject go = new GameObject(text, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.color = new Color(0.05f, 0.2f, 0.28f, 0.82f);

        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0.2f, 0.75f, 1f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);

        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);

        CreateLabel(rect, text, Vector2.zero, 16);
    }

    void CreateLabel(RectTransform parent, string text, Vector2 position, int fontSize)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(220f, 42f);

        var label = go.GetComponent<Text>();
        label.font = m_Font;
        label.text = text;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.fontSize = fontSize;
        label.raycastTarget = false;
    }

    Sprite CreateCircleSprite(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f - 1f;
        float borderStart = radius - 4f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = distance <= radius ? 1f : 0f;
                if (distance > borderStart && distance <= radius)
                    texture.SetPixel(x, y, new Color(0.22f, 0.75f, 1f, alpha));
                else
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static void EnsureInputObjectsEnabled()
    {
        foreach (EventSystem eventSystem in Resources.FindObjectsOfTypeAll<EventSystem>())
        {
            if (eventSystem == null || !eventSystem.gameObject.scene.IsValid())
                continue;

            eventSystem.gameObject.SetActive(true);

            if (eventSystem.GetComponent<XRUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<XRUIInputModule>();

            if (eventSystem.GetComponent("UnityEngine.InputSystem.UI.InputSystemUIInputModule") is Behaviour inputSystemUiInputModule)
                inputSystemUiInputModule.enabled = false;
        }

        foreach (XRInteractionManager interactionManager in Resources.FindObjectsOfTypeAll<XRInteractionManager>())
        {
            if (interactionManager != null && interactionManager.gameObject.scene.IsValid())
                interactionManager.gameObject.SetActive(true);
        }
    }
}

public class WristJoystickButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    WristDroneJoystickUI m_Owner;
    bool m_LeftWristJoystick;
    Vector2 m_Axis;
    bool m_IsPressed;

    public void Initialize(WristDroneJoystickUI owner, bool leftWristJoystick, Vector2 axis)
    {
        m_Owner = owner;
        m_LeftWristJoystick = leftWristJoystick;
        m_Axis = axis;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        m_IsPressed = true;
        m_Owner.SetStick(m_LeftWristJoystick, m_Axis);
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
        m_Owner.SetStick(m_LeftWristJoystick, Vector2.zero);
    }
}
