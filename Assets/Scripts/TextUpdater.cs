using TMPro;
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
        textComponent.text = string.Format("Battery {0} %", ((TelloLib.Tello.state != null) ? ("" + TelloLib.Tello.state.batteryPercentage) : " - "));
    }
}
