using UnityEngine;
using UnityEngine.UI;

public class FunctionSwitch : MonoBehaviour
{
    public Button[] nextBtn;
    public Button[] prevBtn;
    public GameObject[] nextView;
    public GameObject[] prevView;
    public GameObject[] currentView;

    void Start()
    {
        foreach (Button btn in nextBtn)
        {
            if (btn != null)
            {
                btn.onClick.AddListener(ShowNextView);
            }
        }

        foreach (Button btn in prevBtn)
        {
            if (btn != null)
            {
                btn.onClick.AddListener(ShowPrevView);
            }
        }
    }

    public void ShowNextView()
    {
        HideGameObjects(currentView);
        HideGameObjects(prevView);
        ShowGameObjects(nextView);
    }

    public void ShowPrevView()
    {
        HideGameObjects(currentView);
        HideGameObjects(nextView);
        ShowGameObjects(prevView);
    }

    private void ShowGameObjects(GameObject[] objects)
    {
        if (objects == null)
        {
            return;
        }

        foreach (GameObject obj in objects)
        {
            if (obj != null)
            {
                obj.SetActive(true);
            }
        }
    }

    private void HideGameObjects(GameObject[] objects)
    {
        if (objects == null)
        {
            return;
        }

        foreach (GameObject obj in objects)
        {
            if (obj != null)
            {
                obj.SetActive(false);
            }
        }
    }
}