using System.Collections.Generic;
using TelloLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class WristDroneJoystickPanel : MonoBehaviour
{
    [SerializeField] bool m_LeftWristJoystick = true;
    [SerializeField] bool m_ApplyWristPose = true;
    [SerializeField] Vector3 m_WorldOffset = new Vector3(0f, 0.08f, 0f);
    [SerializeField] Vector3 m_PanelLocalEuler;
    [SerializeField] float m_PanelScale = 0.00055f;
    [SerializeField] bool m_FaceMainCamera = true;
    [SerializeField, Range(0.1f, 1f)] float m_AxisStrength = 1f;
    [Header("Direct Hand Input")]
    [SerializeField] bool m_EnableDirectTouch = true;
    [SerializeField] bool m_EnablePinchPress = true;
    [SerializeField] float m_DirectTouchDistance = 0.025f;
    [SerializeField] float m_HoverDistance = 0.055f;
    [SerializeField] float m_PinchDistance = 0.025f;
    [SerializeField] float m_TouchPadding = 12f;

    static Vector2 s_LeftStick;
    static Vector2 s_RightStick;
    static bool s_HasJoystickInput;
    static bool s_SendZeroOnce;
    static readonly List<XRHandSubsystem> s_HandSubsystems = new List<XRHandSubsystem>();

    readonly List<PanelRootState> m_PanelRoots = new List<PanelRootState>();
    readonly List<WristDroneJoystickPanelButton> m_JoystickButtons = new List<WristDroneJoystickPanelButton>();
    readonly List<ButtonTouchTarget> m_CommandButtons = new List<ButtonTouchTarget>();
    XRHandSubsystem m_HandSubsystem;
    WristDroneJoystickPanelButton m_DirectPressedJoystickButton;
    ButtonTouchTarget m_DirectPressedCommandButton;
    bool m_TakeOffSliderTriggered;

    struct PanelRootState
    {
        public RectTransform RectTransform;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
    }

    void Awake()
    {
        EnsureInputObjectsEnabled();
        EnsureTakeOffButtonBetweenPanels();
        CachePanelRoots();
        WireInteractiveControls();
        EnsureButtonLabels();
    }

    void LateUpdate()
    {
        if (m_ApplyWristPose)
            ApplyWristOffsetAndFacing();

        UpdateDirectHandInput();
        ApplyDroneInput();
    }

    void OnDisable()
    {
        ReleaseDirectPressedControls();
        ClearSticks();
    }

    public void SetStick(Vector2 value)
    {
        if (m_LeftWristJoystick)
            s_LeftStick = value;
        else
            s_RightStick = value;

        Debug.Log($"[WristJoystick] {(m_LeftWristJoystick ? "Left" : "Right")} stick input = {value}", this);

        s_HasJoystickInput = s_LeftStick != Vector2.zero || s_RightStick != Vector2.zero;
        if (!s_HasJoystickInput)
            s_SendZeroOnce = true;
    }

    public void SetStick(WristDroneJoystickPanelButton sourceButton, Vector2 value)
    {
        bool targetLeftStick = m_LeftWristJoystick;
        Transform buttonTransform = sourceButton != null ? sourceButton.transform : null;

        if (IsUnderPanel(buttonTransform, "Panel1"))
            targetLeftStick = true;
        else if (IsUnderPanel(buttonTransform, "Panel2"))
            targetLeftStick = false;

        SetStick(targetLeftStick, value);
    }

    void SetStick(bool targetLeftStick, Vector2 value)
    {
        if (targetLeftStick)
            s_LeftStick = value;
        else
            s_RightStick = value;

        Debug.Log($"[WristJoystick] {(targetLeftStick ? "Left" : "Right")} stick input = {value}", this);

        s_HasJoystickInput = s_LeftStick != Vector2.zero || s_RightStick != Vector2.zero;
        if (!s_HasJoystickInput)
            s_SendZeroOnce = true;
    }

    public void TakeOff()
    {
        Debug.Log("[WristJoystick] TakeOff command invoked", this);
        Tello.takeOff();
    }

    public void OnTakeOffSliderChanged(float value)
    {
        if (!m_LeftWristJoystick)
            return;

        Debug.Log($"[WristJoystick] TakeOff slider value = {value:0.000}", this);

        if (value < 0.99f)
        {
            m_TakeOffSliderTriggered = false;
            return;
        }

        if (value < 0.999f || m_TakeOffSliderTriggered)
            return;

        m_TakeOffSliderTriggered = true;
        Debug.Log("[WristJoystick] TakeOff slider reached the end. Sending takeoff.", this);
        Tello.takeOff();
    }

    public void Land()
    {
        Debug.Log("[WristJoystick] Land command invoked", this);
        ClearSticks();
        Tello.land();
    }

    void ClearSticks()
    {
        s_LeftStick = Vector2.zero;
        s_RightStick = Vector2.zero;
        s_HasJoystickInput = false;
        s_SendZeroOnce = true;
        Tello.controllerState.setAxis(0f, 0f, 0f, 0f);
    }

    void EnsureTakeOffButtonBetweenPanels()
    {
        if (!m_LeftWristJoystick)
            return;

        Transform offset = transform.Find("Offset");
        if (offset == null || offset.Find("TAKE OFF") != null)
            return;

        var panel1 = offset.Find("Panel1") as RectTransform;
        var panel2 = offset.Find("Panel2") as RectTransform;
        if (panel1 == null || panel2 == null)
            return;

        GameObject go = new GameObject("TAKE OFF",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(TrackedDeviceGraphicRaycaster),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Outline),
            typeof(Button));
        go.transform.SetParent(offset, false);

        var rectTransform = go.GetComponent<RectTransform>();
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one * m_PanelScale;
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = (panel1.anchoredPosition + panel2.anchoredPosition) * 0.5f;
        rectTransform.sizeDelta = new Vector2(190f, 86f);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        var image = go.GetComponent<Image>();
        image.color = new Color(0.05f, 0.2f, 0.28f, 0.82f);

        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0.2f, 0.75f, 1f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(TakeOff);

        EnsureCenteredLabel(rectTransform, "TAKE OFF", 18);
    }

    void EnsureButtonLabels()
    {
        foreach (WristDroneJoystickPanelButton joystickButton in GetComponentsInChildren<WristDroneJoystickPanelButton>(true))
        {
            if (joystickButton != null)
                EnsureCenteredLabel(joystickButton.GetComponent<RectTransform>(), joystickButton.name, 18);
        }

        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (button != null)
                EnsureCenteredLabel(button.GetComponent<RectTransform>(), button.name, 18);
        }
    }

    static void EnsureCenteredLabel(RectTransform parent, string text, int fontSize)
    {
        if (parent == null || parent.Find("Label") != null)
            return;

        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);

        var rectTransform = go.GetComponent<RectTransform>();
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = parent.sizeDelta.sqrMagnitude > 1f ? parent.sizeDelta : new Vector2(128f, 46f);

        var label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.text = text;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.fontSize = fontSize;
        label.raycastTarget = false;
    }

    void ApplyDroneInput()
    {
        if (!s_HasJoystickInput && !s_SendZeroOnce)
            return;

        float lx = s_RightStick.x * m_AxisStrength;
        float ly = s_RightStick.y * m_AxisStrength;
        float rx = s_LeftStick.x * m_AxisStrength;
        float ry = s_LeftStick.y * m_AxisStrength;

        Tello.controllerState.setAxis(lx, ly, rx, ry);
        s_SendZeroOnce = false;
    }

    void ApplyWristOffsetAndFacing()
    {
        if (m_PanelRoots.Count == 0)
            return;

        Camera mainCamera = Camera.main;

        for (int i = 0; i < m_PanelRoots.Count; i++)
        {
            PanelRootState panelRoot = m_PanelRoots[i];
            RectTransform rectTransform = panelRoot.RectTransform;
            if (rectTransform == null)
                continue;

            rectTransform.localPosition = panelRoot.LocalPosition + m_WorldOffset;

            if (!m_FaceMainCamera || mainCamera == null)
            {
                rectTransform.localRotation = panelRoot.LocalRotation * Quaternion.Euler(m_PanelLocalEuler);
                continue;
            }

            Vector3 toPanel = rectTransform.position - mainCamera.transform.position;
            if (toPanel.sqrMagnitude > 0.0001f)
                rectTransform.rotation = Quaternion.LookRotation(toPanel.normalized, Vector3.up);
        }
    }

    void CachePanelRoots()
    {
        m_PanelRoots.Clear();

        foreach (RectTransform rectTransform in GetComponentsInChildren<RectTransform>(true))
        {
            if (rectTransform == null || rectTransform == transform)
                continue;

            bool isDirectChild = rectTransform.parent == transform;
            bool isNamedPanel = rectTransform.name == "Panel" || rectTransform.name == "Panel1" || rectTransform.name == "Panel2";
            bool isWorldCanvas = rectTransform.GetComponent<Canvas>() != null;
            if (!isDirectChild || (!isNamedPanel && !isWorldCanvas))
                continue;

            rectTransform.localScale = Vector3.one * m_PanelScale;
            m_PanelRoots.Add(new PanelRootState
            {
                RectTransform = rectTransform,
                LocalPosition = rectTransform.localPosition,
                LocalRotation = rectTransform.localRotation
            });
        }

        Debug.Log($"[WristJoystick] Cached {m_PanelRoots.Count} panel root(s) for {(m_LeftWristJoystick ? "Left" : "Right")} wrist.", this);
    }

    void WireInteractiveControls()
    {
        m_JoystickButtons.Clear();
        m_CommandButtons.Clear();

        foreach (WristDroneJoystickPanelButton joystickButton in GetComponentsInChildren<WristDroneJoystickPanelButton>(true))
        {
            if (joystickButton == null)
                continue;

            if (joystickButton.GetComponent<Button>() != null)
            {
                joystickButton.enabled = false;
                continue;
            }

            joystickButton.Configure(this);
            m_JoystickButtons.Add(joystickButton);
            EnsureClickableSize(joystickButton.GetComponent<RectTransform>(), joystickButton.name);
            Debug.Log($"[WristJoystick] Connected button '{joystickButton.name}' to {(m_LeftWristJoystick ? "Left" : "Right")} panel, axis = {joystickButton.Axis}", joystickButton);
        }

        foreach (Button commandButton in GetComponentsInChildren<Button>(true))
        {
            if (commandButton == null)
                continue;

            EnsureClickableSize(commandButton.GetComponent<RectTransform>(), commandButton.name);
            if (commandButton.GetComponent<PressedButtonStatusReporter>() == null)
                commandButton.gameObject.AddComponent<PressedButtonStatusReporter>();

            m_CommandButtons.Add(new ButtonTouchTarget(commandButton));
            Debug.Log($"[WristJoystick] Command button ready: {commandButton.name}", commandButton);
        }
    }

    void UpdateDirectHandInput()
    {
        if (!m_EnableDirectTouch && !m_EnablePinchPress)
            return;

        ResetDirectHoverState();

        WristDroneJoystickPanelButton hoverJoystickButton = null;
        ButtonTouchTarget hoverCommandButton = default;
        WristDroneJoystickPanelButton pressJoystickButton = null;
        ButtonTouchTarget pressCommandButton = default;

        ApplyHandInputCandidate(true, ref hoverJoystickButton, ref hoverCommandButton, ref pressJoystickButton, ref pressCommandButton);
        ApplyHandInputCandidate(false, ref hoverJoystickButton, ref hoverCommandButton, ref pressJoystickButton, ref pressCommandButton);

        if (hoverJoystickButton != null)
            hoverJoystickButton.SetDirectHover(true);

        if (hoverCommandButton.Button != null)
            SetCommandButtonHover(hoverCommandButton, true, hoverCommandButton.Button == m_DirectPressedCommandButton.Button);

        if (pressJoystickButton != null || pressCommandButton.Button != null)
        {
            if (pressJoystickButton != null)
            {
                if (m_DirectPressedCommandButton.Button != null)
                    ReleaseCommandButton();

                if (m_DirectPressedJoystickButton != pressJoystickButton)
                {
                    if (m_DirectPressedJoystickButton != null)
                        m_DirectPressedJoystickButton.ReleaseFromDirectHand();

                    m_DirectPressedJoystickButton = pressJoystickButton;
                    m_DirectPressedJoystickButton.PressFromDirectHand();
                }

                return;
            }

            if (pressCommandButton.Button != null)
            {
                if (m_DirectPressedJoystickButton != null)
                    ReleaseJoystickButton();

                if (m_DirectPressedCommandButton.Button != pressCommandButton.Button)
                {
                    ReleaseCommandButton();
                    m_DirectPressedCommandButton = pressCommandButton;
                    SetCommandButtonHover(m_DirectPressedCommandButton, true, true);
                    DroneInputStatus.SetButtonPressed(m_DirectPressedCommandButton.Button.name, true);
                    m_DirectPressedCommandButton.Button.onClick.Invoke();
                    Debug.Log($"[WristJoystick] Direct hand command invoked: {m_DirectPressedCommandButton.Button.name}", m_DirectPressedCommandButton.Button);
                }

                return;
            }
        }

        ReleaseDirectPressedControls();
    }

    void ApplyHandInputCandidate(
        bool leftHand,
        ref WristDroneJoystickPanelButton hoverJoystickButton,
        ref ButtonTouchTarget hoverCommandButton,
        ref WristDroneJoystickPanelButton pressJoystickButton,
        ref ButtonTouchTarget pressCommandButton)
    {
        if (!TryGetFingerPoses(leftHand, out Pose indexPose, out Pose thumbPose, out bool thumbTracked))
            return;

        float closestDistance = float.MaxValue;
        WristDroneJoystickPanelButton closestJoystick = FindClosestJoystickButton(indexPose.position, m_HoverDistance, ref closestDistance);
        ButtonTouchTarget closestCommand = FindClosestCommandButton(indexPose.position, m_HoverDistance, ref closestDistance);

        if (closestJoystick != null)
            hoverJoystickButton = closestJoystick;
        else if (closestCommand.Button != null)
            hoverCommandButton = closestCommand;

        bool touchPress = m_EnableDirectTouch && closestDistance <= m_DirectTouchDistance;
        bool pinchPress = m_EnablePinchPress && thumbTracked && closestDistance <= m_HoverDistance &&
                          Vector3.Distance(indexPose.position, thumbPose.position) <= m_PinchDistance;

        if (!touchPress && !pinchPress)
            return;

        if (closestJoystick != null)
            pressJoystickButton = closestJoystick;
        else if (closestCommand.Button != null)
            pressCommandButton = closestCommand;
    }

    WristDroneJoystickPanelButton FindClosestJoystickButton(Vector3 fingertipPosition, float maxDistance, ref float closestDistance)
    {
        WristDroneJoystickPanelButton closest = null;

        for (int i = 0; i < m_JoystickButtons.Count; i++)
        {
            WristDroneJoystickPanelButton button = m_JoystickButtons[i];
            if (button == null || !button.gameObject.activeInHierarchy)
                continue;

            RectTransform rectTransform = button.GetComponent<RectTransform>();
            if (!TryGetRectTouchDistance(rectTransform, fingertipPosition, maxDistance, out float distance))
                continue;

            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            closest = button;
        }

        return closest;
    }

    ButtonTouchTarget FindClosestCommandButton(Vector3 fingertipPosition, float maxDistance, ref float closestDistance)
    {
        ButtonTouchTarget closest = default;

        for (int i = 0; i < m_CommandButtons.Count; i++)
        {
            ButtonTouchTarget target = m_CommandButtons[i];
            if (target.Button == null || !target.Button.gameObject.activeInHierarchy)
                continue;

            if (!TryGetRectTouchDistance(target.RectTransform, fingertipPosition, maxDistance, out float distance))
                continue;

            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            closest = target;
        }

        return closest;
    }

    bool TryGetRectTouchDistance(RectTransform rectTransform, Vector3 fingertipPosition, float maxDistance, out float distance)
    {
        distance = float.MaxValue;

        if (rectTransform == null)
            return false;

        distance = Mathf.Abs(Vector3.Dot(fingertipPosition - rectTransform.position, rectTransform.forward));
        if (distance > maxDistance)
            return false;

        Vector3 local = rectTransform.InverseTransformPoint(fingertipPosition);
        Rect rect = rectTransform.rect;
        rect.xMin -= m_TouchPadding;
        rect.xMax += m_TouchPadding;
        rect.yMin -= m_TouchPadding;
        rect.yMax += m_TouchPadding;

        return rect.Contains(new Vector2(local.x, local.y));
    }

    bool TryGetFingerPoses(bool leftHand, out Pose indexPose, out Pose thumbPose, out bool thumbTracked)
    {
        indexPose = default;
        thumbPose = default;
        thumbTracked = false;

        if (m_HandSubsystem == null || !m_HandSubsystem.running)
            m_HandSubsystem = FindHandSubsystem();

        if (m_HandSubsystem == null)
            return false;

        XRHand hand = leftHand ? m_HandSubsystem.leftHand : m_HandSubsystem.rightHand;
        XRHandJoint indexTip = hand.GetJoint(XRHandJointID.IndexTip);
        XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);

        if (indexTip.trackingState == XRHandJointTrackingState.None || !indexTip.TryGetPose(out indexPose))
            return false;

        thumbTracked = thumbTip.trackingState != XRHandJointTrackingState.None && thumbTip.TryGetPose(out thumbPose);
        return true;
    }

    static XRHandSubsystem FindHandSubsystem()
    {
        s_HandSubsystems.Clear();
        SubsystemManager.GetSubsystems(s_HandSubsystems);

        for (int i = 0; i < s_HandSubsystems.Count; i++)
        {
            if (s_HandSubsystems[i] != null && s_HandSubsystems[i].running)
                return s_HandSubsystems[i];
        }

        return null;
    }

    void ResetDirectHoverState()
    {
        for (int i = 0; i < m_JoystickButtons.Count; i++)
        {
            if (m_JoystickButtons[i] != null)
                m_JoystickButtons[i].SetDirectHover(false);
        }

        for (int i = 0; i < m_CommandButtons.Count; i++)
        {
            ButtonTouchTarget target = m_CommandButtons[i];
            if (target.Button != null && target.Button != m_DirectPressedCommandButton.Button)
                SetCommandButtonHover(target, false, false);
        }
    }

    void ReleaseDirectPressedControls()
    {
        ReleaseJoystickButton();
        ReleaseCommandButton();
    }

    void ReleaseJoystickButton()
    {
        if (m_DirectPressedJoystickButton == null)
            return;

        m_DirectPressedJoystickButton.ReleaseFromDirectHand();
        m_DirectPressedJoystickButton = null;
    }

    void ReleaseCommandButton()
    {
        if (m_DirectPressedCommandButton.Button == null)
            return;

        SetCommandButtonHover(m_DirectPressedCommandButton, false, false);
        DroneInputStatus.SetButtonPressed(m_DirectPressedCommandButton.Button.name, false);
        m_DirectPressedCommandButton = default;
    }

    static void SetCommandButtonHover(ButtonTouchTarget target, bool hovered, bool pressed)
    {
        if (target.Graphic == null)
            return;

        if (pressed)
            target.Graphic.color = new Color(0.1f, 0.65f, 1f, 0.9f);
        else if (hovered)
            target.Graphic.color = new Color(1f, 0.45f, 0.08f, 0.65f);
        else
            target.Graphic.color = target.NormalColor;
    }

    static void EnsureClickableSize(RectTransform rectTransform, string controlName)
    {
        if (rectTransform == null || rectTransform.sizeDelta.sqrMagnitude > 1f)
            return;

        if (controlName == "LAND" || controlName == "TAKE OFF")
            rectTransform.sizeDelta = new Vector2(128f, 46f);
        else if (controlName == "STOP")
            rectTransform.sizeDelta = new Vector2(82f, 58f);
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

    static bool IsUnderPanel(Transform transform, string panelName)
    {
        while (transform != null)
        {
            if (transform.name == panelName)
                return true;

            transform = transform.parent;
        }

        return false;
    }

    readonly struct ButtonTouchTarget
    {
        public ButtonTouchTarget(Button button)
        {
            Button = button;
            RectTransform = button != null ? button.GetComponent<RectTransform>() : null;
            Graphic = button != null ? button.targetGraphic : null;
            NormalColor = Graphic != null ? Graphic.color : Color.white;
        }

        public Button Button { get; }
        public RectTransform RectTransform { get; }
        public Graphic Graphic { get; }
        public Color NormalColor { get; }
    }
}
