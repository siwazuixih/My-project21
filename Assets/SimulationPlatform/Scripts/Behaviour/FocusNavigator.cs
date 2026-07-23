using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class FocusNavigator : MonoBehaviour
{
    public GameObject[] focusList;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                NavigateToPrev();
            }
            else
            {
                NavigateToNext();
            }
        }
    }

    private void NavigateToNext()
    {
        if (focusList == null || focusList.Length == 0)
        {
            return;
        }

        GameObject current = EventSystem.current?.currentSelectedGameObject;
        int currentIndex = -1;

        for (int i = 0; i < focusList.Length; i++)
        {
            if (focusList[i] == current)
            {
                currentIndex = i;
                break;
            }
        }

        int nextIndex = (currentIndex + 1) % focusList.Length;
        SelectGameObject(focusList[nextIndex]);
    }

    private void NavigateToPrev()
    {
        if (focusList == null || focusList.Length == 0)
        {
            return;
        }

        GameObject current = EventSystem.current?.currentSelectedGameObject;
        int currentIndex = -1;

        for (int i = 0; i < focusList.Length; i++)
        {
            if (focusList[i] == current)
            {
                currentIndex = i;
                break;
            }
        }

        int prevIndex = (currentIndex - 1 + focusList.Length) % focusList.Length;
        SelectGameObject(focusList[prevIndex]);
    }

    private void SelectGameObject(GameObject target)
    {
        if (target != null)
        {
            Selectable selectable = target.GetComponent<Selectable>();
            if (selectable != null)
            {
                selectable.Select();
            }
        }
    }
}