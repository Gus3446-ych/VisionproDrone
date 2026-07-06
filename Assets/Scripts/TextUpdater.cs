using TMPro;
using TelloLib;
using UnityEngine;

public class TextUpdater : MonoBehaviour
{
    private TMP_Text textComponent;

    void Awake()
    {
        textComponent = GetComponent<TMP_Text>();

        if (textComponent == null)
        {
            Debug.LogError($"{nameof(TextUpdater)} requires a TextMeshPro or TextMeshProUGUI component.", this);
            enabled = false;
        }
    }

    void Update()
    {
        var state = Tello.state;
        if (state == null)
        {
            textComponent.text = "BAT --%  WIFI --%  SPD --cm/s  ALT --cm";
            return;
        }

        textComponent.text = string.Format(
            "BAT {0}%  WIFI {1}%  SPD {2}cm/s  ALT {3}cm",
            state.batteryPercentage,
            state.wifiStrength,
            state.flySpeed,
            state.height);
    }
}
